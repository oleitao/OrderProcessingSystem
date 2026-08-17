using Microsoft.IdentityModel.Tokens;

namespace OrderProcessing.Api.Auth;

/// <summary>
/// Holds the signing keys fetched from IdentityService's JWKS endpoint. JwtBearer's
/// IssuerSigningKeyResolver runs synchronously on every request, so the keys must already be here
/// — this cache is populated/refreshed out-of-band by JwksCacheRefresherWorker, never fetched
/// inline during token validation (no blocking HTTP calls on the request path).
/// </summary>
public sealed class JwksCache
{
    private volatile IReadOnlyList<SecurityKey> _keys = Array.Empty<SecurityKey>();

    public IReadOnlyList<SecurityKey> GetKeys() => _keys;

    public void SetKeys(IReadOnlyList<SecurityKey> keys) => _keys = keys;
}
