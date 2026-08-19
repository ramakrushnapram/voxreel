using System.Text;
using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Llm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVIDEO.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/documents")]
public sealed class DocumentsController(LlmService llm) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> List(CancellationToken cancellationToken)
    {
        var docs = await llm.ListDocumentsAsync(User.GetUserId(), cancellationToken);
        return Ok(docs.Select(DocumentResponse.From).ToList());
    }

    /// <summary>Ingest pasted text as a RAG document.</summary>
    [HttpPost]
    public async Task<ActionResult<DocumentResponse>> Create(
        [FromBody] IngestDocumentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var doc = await llm.IngestDocumentAsync(User.GetUserId(), request.Name, request.Text, cancellationToken);
            return Ok(DocumentResponse.From(doc));
        }
        catch (LlmUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Local LLM unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
    }

    /// <summary>Ingest an uploaded .txt/.md/.srt/.vtt file as a RAG document.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(10L * 1024 * 1024)]
    public async Task<ActionResult<DocumentResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No file supplied", Status = 400 });
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var text = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            var doc = await llm.IngestDocumentAsync(User.GetUserId(), file.FileName, text, cancellationToken);
            return Ok(DocumentResponse.From(doc));
        }
        catch (LlmUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Local LLM unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await llm.DeleteDocumentAsync(User.GetUserId(), id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
