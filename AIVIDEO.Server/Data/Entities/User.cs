using System.ComponentModel.DataAnnotations;

namespace AIVIDEO.Server.Data.Entities;

/// <summary>
/// An account. Passwords are never stored — only a hash produced by ASP.NET Core's
/// <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>, which salts and
/// iterates internally.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Stored lower-cased and used as the login identifier. Unique.</summary>
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Opaque PasswordHasher output (algorithm + salt + iterations + hash).</summary>
    [MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginUtc { get; set; }
}
