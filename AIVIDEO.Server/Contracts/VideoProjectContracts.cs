using System.ComponentModel.DataAnnotations;
using AIVIDEO.Server.Data.Entities;

namespace AIVIDEO.Server.Contracts;

public sealed record CreateVideoProjectRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(1000, MinimumLength = 1)]
    public string Topic { get; init; } = string.Empty;

    public int TargetMinutes { get; init; } = 2;

    public string AspectRatio { get; init; } = "16:9";

    public bool UseRag { get; init; }

    public bool BackgroundMusic { get; init; } = true;
}

public sealed record SceneResponse
{
    public required int Index { get; init; }
    public required string NarrationText { get; init; }
    public string? VisualPrompt { get; init; }
    public int DurationMs { get; init; }
    public string? ImageUrl { get; init; }

    public static SceneResponse From(Scene s) => new()
    {
        Index = s.Index,
        NarrationText = s.NarrationText,
        VisualPrompt = s.VisualPrompt,
        DurationMs = s.DurationMs,
        ImageUrl = s.ImageAssetId is null ? null : $"/api/assets/{s.ImageAssetId}/raw"
    };
}

public sealed record VideoProjectResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Topic { get; init; }
    public required int TargetMinutes { get; init; }
    public required string Status { get; init; }
    public required int Progress { get; init; }
    public string? ErrorMessage { get; init; }
    public string? VideoUrl { get; init; }
    public int SceneCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public IReadOnlyList<SceneResponse> Scenes { get; init; } = [];

    public static VideoProjectResponse From(VideoProject p, bool includeScenes) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Topic = p.Topic,
        TargetMinutes = p.TargetMinutes,
        Status = p.Status.ToString(),
        Progress = p.Progress,
        ErrorMessage = p.ErrorMessage,
        VideoUrl = p.OutputAssetId is null ? null : $"/api/assets/{p.OutputAssetId}/raw",
        SceneCount = p.Scenes.Count,
        CreatedUtc = p.CreatedUtc,
        Scenes = includeScenes
            ? p.Scenes.OrderBy(s => s.Index).Select(SceneResponse.From).ToList()
            : []
    };
}
