using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIVIDEO.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/video-projects")]
public sealed class VideoProjectsController(
    AppDbContext db,
    LongFormQueue queue) : ControllerBase
{
    /// <summary>Creates a project and queues the build. Returns immediately; poll for progress.</summary>
    [HttpPost]
    public async Task<ActionResult<VideoProjectResponse>> Create(
        [FromBody] CreateVideoProjectRequest request,
        CancellationToken cancellationToken)
    {
        var hasScript = !string.IsNullOrWhiteSpace(request.ScriptText);
        if (string.IsNullOrWhiteSpace(request.Topic) && !hasScript)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Nothing to build from",
                Detail = "Provide a topic, or paste a script/transcript.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var project = new VideoProject
        {
            UserId = User.GetUserId(),
            Title = request.Title.Trim(),
            Topic = request.Topic.Trim(),
            ScriptText = hasScript ? request.ScriptText!.Trim() : string.Empty,
            TargetMinutes = Math.Clamp(request.TargetMinutes, 1, 20),
            AspectRatio = request.AspectRatio,
            VisualStyle = string.IsNullOrWhiteSpace(request.VisualStyle) ? "cinematic" : request.VisualStyle.Trim(),
            UseRag = request.UseRag,
            BackgroundMusic = request.BackgroundMusic,
            Subtitles = request.Subtitles,
            Quality = string.Equals(request.Quality, "draft", StringComparison.OrdinalIgnoreCase) ? "draft" : "high",
            Status = VideoProjectStatus.Draft
        };

        db.VideoProjects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        await queue.EnqueueAsync(project.Id);

        return Ok(VideoProjectResponse.From(project, includeScenes: false));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VideoProjectResponse>>> List(CancellationToken cancellationToken)
    {
        var projects = await db.VideoProjects
            .Where(p => p.UserId == User.GetUserId())
            .Include(p => p.Scenes)
            .OrderByDescending(p => p.CreatedUtc)
            .ToListAsync(cancellationToken);

        return Ok(projects.Select(p => VideoProjectResponse.From(p, includeScenes: false)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoProjectResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var project = await db.VideoProjects
            .Include(p => p.Scenes)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == User.GetUserId(), cancellationToken);

        return project is null ? NotFound() : Ok(VideoProjectResponse.From(project, includeScenes: true));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var project = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == User.GetUserId(), cancellationToken);
        if (project is null) return NotFound();

        db.VideoProjects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
