using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIVIDEO.Server.Llm;

/// <summary>Raised for LLM feature failures the controller turns into a 400/503.</summary>
public sealed class LlmUnavailableException(string message) : Exception(message);

/// <summary>
/// High-level local-LLM features built on <see cref="IOllamaClient"/>: image-prompt
/// enhancement, video-script writing, and RAG (retrieval over the user's own documents).
///
/// Every entry point first checks Ollama is reachable and fails with an actionable message
/// if not, so the feature degrades to "install Ollama" guidance rather than a raw error.
/// </summary>
public sealed class LlmService(
    IOllamaClient ollama,
    AppDbContext db,
    ILogger<LlmService> logger)
{
    private const int MaxChunkChars = 900;
    private const int TopKChunks = 5;

    public async Task<string> EnhancePromptAsync(string prompt, CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);

        const string system =
            "You rewrite short image ideas into vivid, detailed prompts for an AI image model. " +
            "Return ONLY the improved prompt as a single line — no preamble, no quotes, no explanation. " +
            "Add concrete detail about subject, composition, lighting, lens, mood, and style.";

        var result = await ollama.GenerateAsync(system, prompt, cancellationToken);
        // Models sometimes wrap output in quotes or add a "Prompt:" label despite instructions.
        return Clean(result);
    }

    /// <summary>
    /// Writes a narration script for a topic. When the user has documents, relevant chunks are
    /// retrieved and prepended so the script is grounded in their material (RAG).
    /// </summary>
    public async Task<ScriptResult> GenerateScriptAsync(
        Guid userId,
        string topic,
        int targetMinutes,
        bool useRag,
        CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);

        var grounding = "";
        var usedChunks = 0;

        if (useRag)
        {
            var chunks = await RetrieveAsync(userId, topic, cancellationToken);
            usedChunks = chunks.Count;
            if (chunks.Count > 0)
            {
                grounding = "\n\nGround the script in these reference excerpts. Prefer their facts, " +
                            "tone, and terminology:\n" +
                            string.Join("\n---\n", chunks.Select(c => c.Text));
            }
        }

        // ~150 spoken words per minute is the working figure the pipeline uses elsewhere.
        var targetWords = Math.Clamp(targetMinutes, 1, 30) * 150;

        var system =
            "You are a scriptwriter for narrated long-form videos. Write a clear, engaging " +
            "voiceover script in plain spoken language — no scene numbers, camera directions, or " +
            "markdown headers, just the words to be spoken. Aim for a strong hook in the first " +
            "two sentences.";

        var user =
            $"Write an approximately {targetWords}-word narration script about: {topic}.{grounding}";

        var script = await ollama.GenerateAsync(system, user, cancellationToken);

        return new ScriptResult(script.Trim(), usedChunks);
    }

    /// <summary>
    /// Splits text into chunks, embeds each, and stores the document. Returns the saved document.
    /// </summary>
    public async Task<Document> IngestDocumentAsync(
        Guid userId,
        string name,
        string text,
        CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);

        var pieces = Chunk(text).ToList();
        if (pieces.Count == 0)
        {
            throw new LlmUnavailableException("The document appears to be empty.");
        }

        var document = new Document { UserId = userId, Name = name, ChunkCount = pieces.Count };

        for (var i = 0; i < pieces.Count; i++)
        {
            var embedding = await ollama.EmbedAsync(pieces[i], cancellationToken);
            document.Chunks.Add(new DocumentChunk
            {
                UserId = userId,
                Ordinal = i,
                Text = pieces[i],
                Embedding = embedding
            });
        }

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ingested document {Id} ({Chunks} chunks) for user {User}.",
            document.Id, pieces.Count, userId);

        return document;
    }

    public Task<List<Document>> ListDocumentsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Documents.Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteDocumentAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);
        if (doc is null) return false;
        db.Documents.Remove(doc);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Retrieves the top-K chunks most similar to the query, scored by cosine similarity in
    /// memory. Fine for the modest per-user corpus this app expects; swap for pgvector if a
    /// single user ever accumulates tens of thousands of chunks.
    /// </summary>
    private async Task<List<DocumentChunk>> RetrieveAsync(Guid userId, string query, CancellationToken cancellationToken)
    {
        var all = await db.DocumentChunks.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
        if (all.Count == 0) return [];

        var queryEmbedding = await ollama.EmbedAsync(query, cancellationToken);

        return all
            .Select(c => (Chunk: c, Score: Cosine(queryEmbedding, c.Embedding)))
            .OrderByDescending(x => x.Score)
            .Take(TopKChunks)
            .Select(x => x.Chunk)
            .ToList();
    }

    private async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (!await ollama.IsAvailableAsync(cancellationToken))
        {
            throw new LlmUnavailableException(
                "Ollama isn't running. Install it from ollama.com, then pull the models: " +
                "`ollama pull llama3.2` and `ollama pull nomic-embed-text`. The server reaches it at " +
                "http://localhost:11434.");
        }
    }

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length != a.Length) return 0f;
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var denom = MathF.Sqrt(na) * MathF.Sqrt(nb);
        return denom == 0 ? 0 : dot / denom;
    }

    /// <summary>Splits on paragraph boundaries, packing up to <see cref="MaxChunkChars"/> per chunk.</summary>
    private static IEnumerable<string> Chunk(string text)
    {
        var paragraphs = text.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var current = new System.Text.StringBuilder();
        foreach (var p in paragraphs)
        {
            if (current.Length + p.Length > MaxChunkChars && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (p.Length > MaxChunkChars)
            {
                // A single very long paragraph is hard-split so no chunk exceeds the model's comfort.
                for (var i = 0; i < p.Length; i += MaxChunkChars)
                {
                    yield return p.Substring(i, Math.Min(MaxChunkChars, p.Length - i));
                }
                continue;
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(p);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static string Clean(string s) =>
        s.Trim().Trim('"').Replace("Prompt:", "", StringComparison.OrdinalIgnoreCase).Trim();
}

public sealed record ScriptResult(string Script, int GroundingChunksUsed);
