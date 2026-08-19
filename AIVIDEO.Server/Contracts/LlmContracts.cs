using System.ComponentModel.DataAnnotations;
using AIVIDEO.Server.Data.Entities;

namespace AIVIDEO.Server.Contracts;

public sealed record EnhancePromptRequest
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Prompt { get; init; } = string.Empty;
}

public sealed record EnhancePromptResponse
{
    public required string Enhanced { get; init; }
}

public sealed record ScriptRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string Topic { get; init; } = string.Empty;

    public int TargetMinutes { get; init; } = 3;

    /// <summary>When true, ground the script in the user's uploaded documents.</summary>
    public bool UseRag { get; init; }
}

public sealed record ScriptResponse
{
    public required string Script { get; init; }

    public required int WordCount { get; init; }

    public required int GroundingChunksUsed { get; init; }
}

public sealed record IngestDocumentRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string Text { get; init; } = string.Empty;
}

public sealed record DocumentResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int ChunkCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }

    public static DocumentResponse From(Document d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        ChunkCount = d.ChunkCount,
        CreatedUtc = d.CreatedUtc
    };
}
