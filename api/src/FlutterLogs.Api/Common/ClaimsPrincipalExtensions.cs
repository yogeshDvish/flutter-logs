using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FlutterLogs.Api.Common;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Reads the user id from the access token's <c>sub</c> claim.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
