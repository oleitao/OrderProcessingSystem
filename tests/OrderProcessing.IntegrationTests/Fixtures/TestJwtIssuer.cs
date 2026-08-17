using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace OrderProcessing.IntegrationTests.Fixtures;

/// <summary>
/// Mints JWTs signed with an in-memory RSA key, mirroring the shape of tokens
/// OrderProcessing.IdentityService issues (see JwtTokenService) — without standing up a real
/// IdentityService for these tests. OrderProcessingApiFactory pre-seeds the Api's JwksCache with
/// this same key's public half, so tokens minted here validate exactly like real ones would.
/// </summary>
public sealed class TestJwtIssuer : IDisposable
{
    public const string Issuer = "OrderProcessing.IdentityService";
    public const string Audience = "OrderProcessing.Api";

    private readonly RSA _rsa = RSA.Create(2048);

    public string KeyId { get; } = Guid.NewGuid().ToString("N");

    public RsaSecurityKey PublicKey =>
        new(_rsa.ExportParameters(includePrivateParameters: false)) { KeyId = KeyId };

    public string IssueToken(Guid userId, string email, string role)
    {
        var signingKey = new RsaSecurityKey(_rsa) { KeyId = KeyId };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _rsa.Dispose();
}
