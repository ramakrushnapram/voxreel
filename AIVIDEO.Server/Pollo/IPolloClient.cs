namespace AIVIDEO.Server.Pollo;

public interface IPolloClient
{
    /// <summary>
    /// Submits a generation to "{BaseUrl}/generation/{modelPath}" and returns the task id.
    /// </summary>
    /// <param name="modelPath">"{brand}/{model}", e.g. "pollo/pollo-v2-5".</param>
    Task<PolloTaskCreatedResponse> CreateGenerationAsync(
        string modelPath,
        PolloGenerationRequest request,
        CancellationToken cancellationToken = default);

    Task<PolloTaskStatusResponse> GetTaskStatusAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the asset stream so it can be written straight to disk without buffering in memory.</summary>
    Task<HttpResponseMessage> DownloadAsync(
        string url,
        CancellationToken cancellationToken = default);
}
