using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Llm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVIDEO.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/llm")]
public sealed class LlmController(LlmService llm, ILogger<LlmController> logger) : ControllerBase
{
    [HttpPost("enhance-prompt")]
    public async Task<ActionResult<EnhancePromptResponse>> Enhance(
        [FromBody] EnhancePromptRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var enhanced = await llm.EnhancePromptAsync(request.Prompt, cancellationToken);
            return Ok(new EnhancePromptResponse { Enhanced = enhanced });
        }
        catch (LlmUnavailableException ex)
        {
            return Problem(ex);
        }
    }

    [HttpPost("script")]
    public async Task<ActionResult<ScriptResponse>> Script(
        [FromBody] ScriptRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await llm.GenerateScriptAsync(
                User.GetUserId(), request.Topic, request.TargetMinutes, request.UseRag, cancellationToken);

            var wordCount = result.Script.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return Ok(new ScriptResponse
            {
                Script = result.Script,
                WordCount = wordCount,
                GroundingChunksUsed = result.GroundingChunksUsed
            });
        }
        catch (LlmUnavailableException ex)
        {
            return Problem(ex);
        }
    }

    // Ollama being down is a 503 (a dependency is unavailable), not a client error.
    private ObjectResult Problem(LlmUnavailableException ex)
    {
        logger.LogWarning("LLM feature unavailable: {Message}", ex.Message);
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
        {
            Title = "Local LLM unavailable",
            Detail = ex.Message,
            Status = StatusCodes.Status503ServiceUnavailable
        });
    }
}
