using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace OrderProcessing.IdentityService.Services;

/// <summary>
/// Holds the RSA key pair used to sign access tokens. Generated fresh in memory on every startup
/// — simple and fine for learning, but it means every restart invalidates all previously-issued
/// tokens (and the Api's cached JWKS) since the "old" public key stops matching. A real deployment
/// would persist the key (a certificate, a key vault) and rotate it deliberately instead.
/// </summary>
public sealed class RsaKeyProvider : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    public string KeyId { get; } = Guid.NewGuid().ToString("N");

    public SigningCredentials GetSigningCredentials()
    {
        var key = new RsaSecurityKey(_rsa) { KeyId = KeyId };
        return new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>
    /// Builds the JWK by hand from only the public RSA parameters (Modulus/Exponent) instead of
    /// relying on a library conversion helper's assumptions about what "public" means — this is
    /// the one place a bug would leak the private key to anyone who calls /.well-known/jwks.json,
    /// so it's worth being explicit rather than trusting a black-box default.
    /// </summary>
    public JsonWebKey GetPublicJwk()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);

        return new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = SecurityAlgorithms.RsaSha256,
            Kid = KeyId,
            N = Base64UrlEncoder.Encode(parameters.Modulus),
            E = Base64UrlEncoder.Encode(parameters.Exponent)
        };
    }

    public void Dispose() => _rsa.Dispose();
}
