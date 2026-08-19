using System.ComponentModel.DataAnnotations;
using AIVIDEO.Server.Data.Entities;

namespace AIVIDEO.Server.Contracts;

public sealed record RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(128), MinLength(2)]
    public string DisplayName { get; init; } = string.Empty;

    // Deliberately modest floor. The real defence is the salted, iterated hash; an
    // over-strict policy here mostly trains users to write passwords down.
    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

public sealed record AuthResponse
{
    public required string Token { get; init; }

    public required DateTimeOffset ExpiresUtc { get; init; }

    public required UserResponse User { get; init; }
}

public sealed record UserResponse
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public static UserResponse From(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName
    };
}
