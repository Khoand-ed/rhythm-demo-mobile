using System.Security.Claims;

namespace Promuse.Api.Infrastructure;

public static class ClaimsExtensions
{
    /// <summary>
    /// MapInboundClaims is off in Program.cs, so `sub` arrives under its own name
    /// rather than being rewritten to the .NET nameidentifier URI. The fallback
    /// covers a token minted before that was turned off.
    /// </summary>
    public static bool TryGetAccountId(this ClaimsPrincipal user, out Guid accountId)
    {
        string? sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out accountId);
    }
}
