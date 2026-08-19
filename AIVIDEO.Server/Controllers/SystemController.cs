using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Llm;
using AIVIDEO.Server.Pollo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Controllers;

/// <summary>
/// Configuration diagnostics. The UI calls this on load so the two things that silently
/// break generation — a missing API key and an unreachable database — are visible up front
/// rather than as a confusing failure on the first render attempt.
/// </summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(
    AppDbContext db,
    IOllamaClient ollama,
    IOptionsMonitor<PolloOptions> polloOptions,
    IOptionsMonitor<StorageOptions> storageOptions) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<SystemStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var pollo = polloOptions.CurrentValue;
        var storage = storageOptions.CurrentValue;

        // Cheap reachability check; returns quickly whether or not Ollama is installed.
        var ollamaAvailable = await ollama.IsAvailableAsync(cancellationToken);
        var ollamaModels = ollamaAvailable
            ? await ollama.ListModelsAsync(cancellationToken)
            : [];

        bool databaseReachable;
        string? databaseError = null;

        try
        {
            databaseReachable = await db.Database.CanConnectAsync(cancellationToken);
            if (!databaseReachable)
            {
                databaseError = "PostgreSQL did not accept the connection. Check ConnectionStrings:Default.";
            }
        }
        catch (Exception ex)
        {
            databaseReachable = false;
            databaseError = Summarise(ex);
        }

        return Ok(new SystemStatusResponse
        {
            PolloConfigured = pollo.IsConfigured,
            DatabaseReachable = databaseReachable,
            DatabaseError = databaseError,
            PublicBaseUrlConfigured = storage.HasPublicBaseUrl,
            OllamaAvailable = ollamaAvailable,
            OllamaModels = ollamaModels,
            Models = new Dictionary<string, string>
            {
                ["Hero"] = pollo.Models.Hero,
                ["Broll"] = pollo.Models.Broll,
                ["ImageToVideo"] = pollo.Models.ImageToVideo,
                ["CharacterLock"] = pollo.Models.CharacterLock,
                ["Still"] = pollo.Models.Still
            },
            AllowedClipLengths = PolloLimits.AllowedLengths,
            MaxClipSeconds = PolloLimits.MaxClipSeconds
        });
    }

    /// <summary>
    /// Reduces a driver exception to one actionable line.
    ///
    /// Npgsql's Message property carries the whole stack trace, which rendered into the UI
    /// banner is unreadable and exposes connection internals. Only the first line is useful,
    /// and the common auth failure gets a concrete next step instead of a raw SQLSTATE.
    /// </summary>
    private static string Summarise(Exception ex)
    {
        var firstLine = ex.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "Unknown database error.";

        if (firstLine.Contains("28P01", StringComparison.Ordinal) ||
            firstLine.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase))
        {
            return "PostgreSQL rejected the credentials. Update the password in ConnectionStrings:Default.";
        }

        return firstLine.Length > 200 ? firstLine[..200] + "…" : firstLine;
    }
}
