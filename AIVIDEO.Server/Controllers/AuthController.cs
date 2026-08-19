using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVIDEO.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.RegisterAsync(request, cancellationToken));
        }
        catch (AuthException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Registration failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.LoginAsync(request, cancellationToken));
        }
        catch (AuthException ex)
        {
            // 401, not 400: the request was well-formed, the credentials were rejected.
            return Unauthorized(new ProblemDetails
            {
                Title = "Login failed",
                Detail = ex.Message,
                Status = StatusCodes.Status401Unauthorized
            });
        }
    }

    /// <summary>Returns the current user, or 401 if the token is missing or invalid. Used by the client to rehydrate a session.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var user = await authService.GetByIdAsync(User.GetUserId(), cancellationToken);
        return user is null ? Unauthorized() : Ok(UserResponse.From(user));
    }
}
