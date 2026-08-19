using System.ComponentModel.DataAnnotations;

namespace AIVIDEO.Server.Data.Entities;

/// <summary>
/// A reference document a user uploads for RAG (a transcript, notes, a style guide). It is
/// split into <see cref="DocumentChunk"/>s, each embedded so the LLM can ground its output
/// in the user's own material.
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int ChunkCount { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}

/// <summary>
/// One embedded slice of a document. The embedding is stored as a float array (PostgreSQL
/// double precision[]) and similarity is computed in memory at query time — no pgvector
/// extension required, which keeps the app clone-and-run on any PostgreSQL.
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DocumentId { get; set; }

    public Document? Document { get; set; }

    /// <summary>Denormalised from the parent so retrieval can filter by owner without a join.</summary>
    public Guid UserId { get; set; }

    public int Ordinal { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Embedding vector. Length depends on the embedding model (e.g. 768 for nomic-embed-text).</summary>
    public float[] Embedding { get; set; } = [];
}
