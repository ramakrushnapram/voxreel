namespace AIVIDEO.Server.Configuration;

/// <summary>
/// Pollo AI platform settings. Bound from the "Pollo" configuration section.
/// The API key must come from user-secrets or an environment variable, never appsettings.json.
/// </summary>
public sealed class PolloOptions
{
    public const string SectionName = "Pollo";

    /// <summary>Platform API root. Endpoints are appended as /generation/{brand}/{model}.</summary>
    public string BaseUrl { get; set; } = "https://pollo.ai/api/platform";

    /// <summary>Sent as the x-api-key header. Obtain from https://api.pollo.ai/api-keys.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional public callback URL. Pollo cannot reach localhost, so this stays empty in
    /// local development and the polling service (<see cref="PollIntervalSeconds"/>) drives
    /// completion instead.
    /// </summary>
    public string? WebhookUrl { get; set; }

    public string ClientSource { get; set; } = "aivideo";

    /// <summary>Upper bound on in-flight Pollo tasks. Long-form renders fan out hard; this is the throttle.</summary>
    public int MaxConcurrentTasks { get; set; } = 5;

    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>A task still unfinished after this long is marked failed rather than polled forever.</summary>
    public int TaskTimeoutMinutes { get; set; } = 30;

    public PolloModelOptions Models { get; set; } = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Model routing by role. Values are "{brand}/{model}" path segments.
///
/// Defaults deliberately point at the two endpoints whose request schemas have been verified
/// against the live docs (pollo-v2-5 for video, nano-banana-pro for stills). Other models in
/// the catalogue accept different field names and casing — notably kling-v3-omni uses
/// uppercase "720P" where pollo-v2-5 uses lowercase "720p" — so switching a role to another
/// model requires checking that model's schema first.
/// </summary>
public sealed class PolloModelOptions
{
    /// <summary>Hero / hook shots. Highest quality, highest cost.</summary>
    public string Hero { get; set; } = "pollo/pollo-v2-5";

    /// <summary>Bulk B-roll for long-form. Cheapest acceptable video tier.</summary>
    public string Broll { get; set; } = "pollo/pollo-v2-5";

    /// <summary>Animating a single supplied image.</summary>
    public string ImageToVideo { get; set; } = "pollo/pollo-v2-5";

    /// <summary>Character consistency across scenes via subject references.</summary>
    public string CharacterLock { get; set; } = "kling-ai/kling-v3-omni/ref2video";

    /// <summary>Still image generation and editing. Powers Ken Burns scenes.</summary>
    public string Still { get; set; } = "google/nano-banana-pro/image";
}
