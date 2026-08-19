using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Pollo;
using AIVIDEO.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVIDEO.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/generations")]
public sealed class GenerationsController(
    GenerationService generationService,
    ILogger<GenerationsController> logger) : ControllerBase
{
    /// <summary>M3 — prompt to a short clip.</summary>
    [HttpPost("text-to-video")]
    public Task<ActionResult<GenerationResponse>> TextToVideo(
        [FromBody] CreateTextToVideoRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(() => generationService.CreateTextToVideoAsync(User.GetUserId(), request, cancellationToken));

    /// <summary>M2 — animate a still.</summary>
    [HttpPost("image-to-video")]
    public Task<ActionResult<GenerationResponse>> ImageToVideo(
        [FromBody] CreateImageToVideoRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(() => generationService.CreateImageToVideoAsync(User.GetUserId(), request, cancellationToken));

    /// <summary>M1 — generate or edit a still.</summary>
    [HttpPost("image")]
    public Task<ActionResult<GenerationResponse>> Image(
        [FromBody] CreateImageRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(() => generationService.CreateImageAsync(User.GetUserId(), request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GenerationResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var generation = await generationService.GetAsync(User.GetUserId(), id, cancellationToken);
        return generation is null
            ? NotFound(new ProblemDetails { Title = "Generation not found", Status = StatusCodes.Status404NotFound })
            : Ok(GenerationResponse.From(generation));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenerationResponse>>> List(
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var generations = await generationService.ListAsync(User.GetUserId(), take, cancellationToken);
        return Ok(generations.Select(GenerationResponse.From).ToList());
    }

    /// <summary>
    /// Maps the two failure modes onto the right status codes: a caller mistake is a 400,
    /// while an upstream rejection is a 502 because the request itself was well-formed.
    /// </summary>
    private async Task<ActionResult<GenerationResponse>> RunAsync(Func<Task<Data.Entities.GenerationRequest>> action)
    {
        try
        {
            var generation = await action();
            return Ok(GenerationResponse.From(generation));
        }
        catch (GenerationValidationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid generation request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (PolloApiException ex)
        {
            logger.LogError(ex, "Pollo rejected a generation request.");
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Pollo API error",
                Detail = ex.Message,
                Status = StatusCodes.Status502BadGateway
            });
        }
    }
}
