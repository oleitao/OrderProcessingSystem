using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OrderProcessing.IdentityService.Models;

namespace OrderProcessing.IdentityService.Services;

public sealed class JwtTokenService(RsaKeyProvider keyProvider, IOptions<JwtOptions> options) : IJwtTokenService
{
    public string CreateAccessToken(ApplicationUser user)
    {
        var now = DateTime.UtcNow;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: options.Value.Issuer,
            audience: options.Value.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(options.Value.AccessTokenLifetimeMinutes),
            signingCredentials: keyProvider.GetSigningCredentials());

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string RawToken, RefreshToken Entity) CreateRefreshToken(Guid userId)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = HashRefreshToken(rawToken);
        var entity = RefreshToken.Create(userId, hash, TimeSpan.FromDays(options.Value.RefreshTokenLifetimeDays));

        return (rawToken, entity);
    }

    // Refresh tokens are high-entropy random values (unlike passwords), so a fast cryptographic
    // hash is appropriate here — no need for the deliberately-slow PBKDF2 used for PasswordHash.
    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
