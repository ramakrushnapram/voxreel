using System.Net.Http.Json;
using System.Text.Json;
using AIVIDEO.Server.Configuration;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Pollo;

/// <summary>
/// Typed HttpClient over the Pollo platform API. Authentication is the x-api-key header,
/// applied per request rather than on the shared client so a key rotated in configuration
/// takes effect without restarting the process.
/// </summary>
public sealed class PolloClient(
    HttpClient httpClient,
    IOptionsMonitor<PolloOptions> options,
    ILogger<PolloClient> logger) : IPolloClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<PolloTaskCreatedResponse> CreateGenerationAsync(
        string modelPath,
        PolloGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var opts = options.CurrentValue;
        EnsureConfigured(opts);

        var path = $"{opts.BaseUrl.TrimEnd('/')}/generation/{modelPath.Trim('/')}";

        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Add("x-api-key", opts.ApiKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Pollo generation failed {Status} for {Path}: {Body}",
                (int)response.StatusCode, path, Truncate(body));
            throw new PolloApiException(
                $"Pollo returned {(int)response.StatusCode} for {modelPath}.",
                (int)response.StatusCode,
                body);
        }

        var created = Unwrap<PolloTaskCreatedResponse>(body);

        if (created is null || string.IsNullOrWhiteSpace(created.TaskId))
        {
            // A 200 with no task id means the response envelope differs from what the docs
            // showed. Surface the raw body — guessing at the shape here would hide the problem.
            throw new PolloApiException(
                $"Pollo accepted the request for {modelPath} but returned no taskId. Raw body: {Truncate(body)}",
                (int)response.StatusCode,
                body);
        }

        logger.LogInformation("Pollo task {TaskId} created via {Model} (status {Status}).",
            created.TaskId, modelPath, created.Status);

        return created;
    }

    public async Task<PolloTaskStatusResponse> GetTaskStatusAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var opts = options.CurrentValue;
        EnsureConfigured(opts);

        var path = $"{opts.BaseUrl.TrimEnd('/')}/generation/{Uri.EscapeDataString(taskId)}/status";

        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.Add("x-api-key", opts.ApiKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PolloApiException(
                $"Pollo status check returned {(int)response.StatusCode} for task {taskId}.",
                (int)response.StatusCode,
                body);
        }

        return Unwrap<PolloTaskStatusResponse>(body)
               ?? throw new PolloApiException($"Unreadable status response for task {taskId}: {Truncate(body)}");
    }

    public Task<HttpResponseMessage> DownloadAsync(string url, CancellationToken cancellationToken = default) =>
        httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    /// <summary>
    /// Deserialises a response that may or may not be wrapped in an envelope.
    ///
    /// The published examples show the payload at the root, but the quick-start describes
    /// responses in terms of "a success status and a task_id" without showing the envelope.
    /// Rather than commit to one shape, try the root first and fall back to a nested "data"
    /// object. This costs one extra parse and removes an entire class of integration failure.
    /// </summary>
    private static T? Unwrap<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var direct = root.Deserialize<T>(JsonOptions);
            if (IsPopulated(direct))
            {
                return direct;
            }

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                return data.Deserialize<T>(JsonOptions);
            }

            return direct;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Distinguishes "parsed into an all-null object" from "genuinely parsed". Without this the
    /// root-level attempt always appears to succeed and the "data" fallback is never reached.
    /// </summary>
    private static bool IsPopulated<T>(T? value) => value switch
    {
        null => false,
        PolloTaskCreatedResponse created => !string.IsNullOrWhiteSpace(created.TaskId),
        PolloTaskStatusResponse status => !string.IsNullOrWhiteSpace(status.TaskId) || status.Generations is not null,
        _ => true
    };

    private static void EnsureConfigured(PolloOptions opts)
    {
        if (!opts.IsConfigured)
        {
            throw new PolloApiException(
                "Pollo:ApiKey is not configured. Set it with: " +
                "dotnet user-secrets set \"Pollo:ApiKey\" \"<key>\" --project AIVIDEO.Server");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value : value[..1000] + "…";
}
