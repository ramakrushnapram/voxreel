using System.ComponentModel.DataAnnotations;

namespace AIVIDEO.Server.Data.Entities;

/// <summary>
/// Lifecycle of a long-form video build. The pipeline advances through these in order; a
/// failure stamps <see cref="VideoProject.ErrorMessage"/> and stops at the current stage.
/// </summary>
public enum VideoProjectStatus
{
    Draft = 0,
    Planning = 1,      // LLM breaking the script into scenes
    Narrating = 2,     // TTS per scene (this sets the timeline)
    GeneratingVisuals = 3,
    Assembling = 4,    // FFmpeg concat + mux
    Ready = 5,
    Failed = 6
}

/// <summary>
/// A long-form video assembled from many short pieces. Because a single AI clip caps at 15s,
/// the finished video is built as: script → scenes → per-scene narration + visual → stitched
/// with FFmpeg. Narration length drives each scene's duration.
/// </summary>
public class VideoProject
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Topic { get; set; } = string.Empty;

    public int TargetMinutes { get; set; } = 2;

    [MaxLength(16)]
    public string AspectRatio { get; set; } = "16:9";

    /// <summary>Whether scene visuals come from the free provider or Pollo.</summary>
    [MaxLength(16)]
    public string Provider { get; set; } = "free";

    /// <summary>Visual style key applied to every scene's image prompt (cinematic, cartoon, anime, …).</summary>
    [MaxLength(32)]
    public string VisualStyle { get; set; } = "cinematic";

    /// <summary>Ground scene planning/script in the user's RAG documents.</summary>
    public bool UseRag { get; set; }

    /// <summary>Mix a synthesized ambient music bed under the narration (ducked when the voice plays).</summary>
    public bool BackgroundMusic { get; set; } = true;

    /// <summary>Burn the narration into the video as on-screen captions.</summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>"high" (1080p, slower) or "draft" (720p, fast preview).</summary>
    [MaxLength(16)]
    public string Quality { get; set; } = "high";

    public string ScriptText { get; set; } = string.Empty;

    public VideoProjectStatus Status { get; set; } = VideoProjectStatus.Draft;

    /// <summary>0–100 for the UI progress bar.</summary>
    public int Progress { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>The finished MP4, once assembly succeeds.</summary>
    public Guid? OutputAssetId { get; set; }

    public MediaAsset? OutputAsset { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Scene> Scenes { get; set; } = new List<Scene>();
}

/// <summary>
/// One beat of the video: a line of narration and the visual shown while it plays. Duration is
/// measured from the synthesized narration, not guessed, so audio and picture stay in sync.
/// </summary>
public class Scene
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VideoProjectId { get; set; }

    public VideoProject? VideoProject { get; set; }

    public Guid UserId { get; set; }

    public int Index { get; set; }

    public string NarrationText { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string VisualPrompt { get; set; } = string.Empty;

    public Guid? ImageAssetId { get; set; }

    public Guid? AudioAssetId { get; set; }

    /// <summary>Set from the narration WAV once synthesized; drives how long the visual is shown.</summary>
    public int DurationMs { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }
}
