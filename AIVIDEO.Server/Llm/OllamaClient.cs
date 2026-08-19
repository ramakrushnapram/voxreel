using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIVIDEO.Server.Configuration;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Llm;

public sealed class OllamaException(string message) : Exception(message);

public interface IOllamaClient
{
    /// <summary>True when the local Ollama server responds. Cheap check used by status and gating.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Model names currently pulled locally.</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Single-shot generation for a system+user prompt. Returns the completion text.</summary>
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);

    /// <summary>Embedding vector for a piece of text, used for RAG similarity.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed client over Ollama's local HTTP API (/api/tags, /api/generate, /api/embeddings).
/// Streaming is disabled so each call returns one complete JSON response.
/// </summary>
public sealed class OllamaClient(
    HttpClient httpClient,
    IOptionsMonitor<OllamaOptions> options,
    ILogger<OllamaClient> logger) : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await httpClient.GetAsync(Url("/api/tags"), cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            // Any failure (connection refused, timeout) means Ollama isn't running. Not an error
            // to log loudly — it's the normal "not installed yet" state.
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var doc = await httpClient.GetFromJsonAsync<TagsResponse>(Url("/api/tags"), JsonOptions, cancellationToken);
            return doc?.Models?.Select(m => m.Name ?? "").Where(n => n.Length > 0).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var opts = options.CurrentValue;
        var request = new GenerateRequest
        {
            Model = opts.ChatModel,
            System = systemPrompt,
            Prompt = userPrompt,
            Stream = false
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, Url("/api/generate"))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A 404 here almost always means the model isn't pulled yet.
            throw new OllamaException(
                $"Ollama returned {(int)response.StatusCode}. If this is 404, pull the model first: " +
                $"ollama pull {opts.ChatModel}. Body: {Truncate(body)}");
        }

        var parsed = JsonSerializer.Deserialize<GenerateResponse>(body, JsonOptions);
        return parsed?.Response?.Trim()
               ?? throw new OllamaException($"Ollama returned an unreadable response: {Truncate(body)}");
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var opts = options.CurrentValue;
        var request = new EmbeddingsRequest { Model = opts.EmbeddingModel, Prompt = text };

        using var message = new HttpRequestMessage(HttpMethod.Post, Url("/api/embeddings"))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new OllamaException(
                $"Ollama embeddings returned {(int)response.StatusCode}. Pull the model first: " +
                $"ollama pull {opts.EmbeddingModel}. Body: {Truncate(body)}");
        }

        var parsed = JsonSerializer.Deserialize<EmbeddingsResponse>(body, JsonOptions);
        if (parsed?.Embedding is null || parsed.Embedding.Length == 0)
        {
            throw new OllamaException($"Ollama returned an empty embedding: {Truncate(body)}");
        }

        return parsed.Embedding;
    }

    private string Url(string path) => $"{options.CurrentValue.BaseUrl.TrimEnd('/')}{path}";

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";

    // ---- wire types ----
    private sealed record GenerateRequest
    {
        [JsonPropertyName("model")] public string Model { get; init; } = "";
        [JsonPropertyName("system")] public string System { get; init; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; init; }
    }

    private sealed record GenerateResponse
    {
        [JsonPropertyName("response")] public string? Response { get; init; }
    }

    private sealed record EmbeddingsRequest
    {
        [JsonPropertyName("model")] public string Model { get; init; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
    }

    private sealed record EmbeddingsResponse
    {
        [JsonPropertyName("embedding")] public float[]? Embedding { get; init; }
    }

    private sealed record TagsResponse
    {
        [JsonPropertyName("models")] public List<TagModel>? Models { get; init; }
    }

    private sealed record TagModel
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
    }
}
