using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AIVIDEO.Server.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id from the token's subject claim.
    ///
    /// On a controller marked [Authorize] the principal is always present and valid, so a
    /// missing or malformed subject is a bug in token issuance, not user input — hence throw
    /// rather than return null and force every caller into a null check.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        // ASP.NET maps "sub" to ClaimTypes.NameIdentifier by default; accept either so this
        // keeps working if claim-type mapping is later turned off.
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (Guid.TryParse(raw, out var id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated principal is missing a valid user id claim.");
    }
}
