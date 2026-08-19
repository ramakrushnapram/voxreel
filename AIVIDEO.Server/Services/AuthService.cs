using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AIVIDEO.Server.Services;

/// <summary>Raised for register/login failures the controller turns into a 400/401.</summary>
public sealed class AuthException(string message) : Exception(message);

/// <summary>
/// Registration, login, and JWT issuance.
///
/// Passwords go through <see cref="PasswordHasher{TUser}"/>, which salts and iterates
/// (PBKDF2) internally — the plaintext is never stored or logged. Login failures return a
/// single generic message whether the email is unknown or the password is wrong, so the
/// endpoint cannot be used to enumerate which addresses have accounts.
/// </summary>
public sealed class AuthService(
    AppDbContext db,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger)
{
    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = Normalise(request.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new AuthException("An account with that email already exists.");
        }

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim()
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registered user {UserId}.", user.Id);
        return BuildResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = Normalise(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Verify even when the user is missing, against a throwaway hash, so a wrong email and
        // a wrong password take the same time. Skipping the check for missing users would leak
        // account existence through response timing.
        var verification = user is null
            ? PasswordVerificationResult.Failed
            : Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            throw new AuthException("Incorrect email or password.");
        }

        // PasswordHasher asks for a rehash when its parameters have been strengthened since
        // the hash was written. Honour it so stored hashes stay current.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
        }

        user.LastLoginUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return BuildResponse(user);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    private AuthResponse BuildResponse(User user)
    {
        var opts = jwtOptions.Value;
        var expires = DateTimeOffset.UtcNow.AddHours(opts.ExpiryHours);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("name", user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AuthResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresUtc = expires,
            User = UserResponse.From(user)
        };
    }

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
