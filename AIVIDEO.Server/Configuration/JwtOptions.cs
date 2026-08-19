namespace AIVIDEO.Server.Configuration;

/// <summary>
/// JWT signing settings. The key is a secret and must come from user-secrets or an
/// environment variable — never appsettings.json.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 signing key. At least 32 bytes; short keys are rejected at startup.</summary>
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = "voxreel";

    public string Audience { get; set; } = "voxreel";

    public int ExpiryHours { get; set; } = 72;

    public bool IsConfigured => Key.Length >= 32;
}
