namespace AIVIDEO.Server.Configuration;

/// <summary>
/// Local LLM settings (Ollama). Everything runs on the user's machine — no API key, no cost,
/// nothing leaves the box. The app works without Ollama; these features simply report as
/// unavailable until it is installed and running.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>
    /// Ollama's local server. Uses 127.0.0.1 rather than "localhost" deliberately: Ollama binds
    /// to IPv4 only, but .NET resolves "localhost" to ::1 (IPv6) first, which hangs until the
    /// probe times out and makes Ollama look unavailable when it is running fine.
    /// </summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Chat/instruct model for prompt enhancement and script writing. Pull with `ollama pull llama3.2`.</summary>
    public string ChatModel { get; set; } = "llama3.2";

    /// <summary>Embedding model for RAG. Pull with `ollama pull nomic-embed-text`.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>How long to allow a single generation (script writing can be slow on CPU).</summary>
    public int TimeoutSeconds { get; set; } = 180;
}
