using OrderProcessing.IdentityService.Models;

namespace OrderProcessing.IdentityService.Services;

public interface IJwtTokenService
{
    /// <summary>Short-lived, signed JWT the Api validates against IdentityService's public key.</summary>
    string CreateAccessToken(ApplicationUser user);

    /// <summary>
    /// Long-lived opaque token. Returns both the raw value (sent to the client once, never stored)
    /// and the entity to persist (which stores only its hash — see RefreshToken.TokenHash).
    /// </summary>
    (string RawToken, RefreshToken Entity) CreateRefreshToken(Guid userId);

    string HashRefreshToken(string rawToken);
}
