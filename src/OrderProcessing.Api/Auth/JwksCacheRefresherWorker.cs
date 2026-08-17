using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OrderProcessing.Api.Auth;

/// <summary>
/// Periodically fetches IdentityService's public JWKS and refreshes JwksCache. This is the only
/// place that talks to IdentityService over HTTP — the JwtBearer IssuerSigningKeyResolver only
/// ever reads the cache this worker fills, keeping token validation itself synchronous.
/// </summary>
public sealed class JwksCacheRefresherWorker(
    IHttpClientFactory httpClientFactory,
    JwksCache cache,
    IOptions<JwtValidationOptions> options,
    ILogger<JwksCacheRefresherWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var steadyStateInterval = TimeSpan.FromSeconds(options.Value.RefreshIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // IdentityService may not be reachable yet (e.g. still starting up). The cache just
                // keeps whatever it had — empty on first failure, meaning every request fails auth
                // until a later refresh succeeds. Same "degrade, don't crash" approach as
                // RabbitMqTopologyInitializer.
                logger.LogWarning(ex, "Could not refresh the JWKS cache from {JwksUri}.", options.Value.JwksUri);
            }

            try
            {
                // While the cache is still empty, retry sooner than the steady-state interval so
                // the Api recovers quickly once IdentityService becomes reachable.
                var delay = cache.GetKeys().Count == 0 ? TimeSpan.FromSeconds(5) : steadyStateInterval;
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down; the while condition above will now exit the loop.
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient();

        var response = await httpClient.GetFromJsonAsync<JwksResponse>(options.Value.JwksUri, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("JWKS endpoint returned an empty response.");

        var keys = response.Keys
            .Where(jwk => jwk.Kty == "RSA")
            .Select(ToSecurityKey)
            .ToList();

        cache.SetKeys(keys);

        logger.LogInformation("JWKS cache refreshed. KeyCount: {KeyCount}", keys.Count);
    }

    private static RsaSecurityKey ToSecurityKey(JwkDto jwk)
    {
        var parameters = new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
            Exponent = Base64UrlEncoder.DecodeBytes(jwk.E)
        };

        return new RsaSecurityKey(RSA.Create(parameters)) { KeyId = jwk.Kid };
    }

    private sealed record JwksResponse(JwkDto[] Keys);

    private sealed record JwkDto(string Kty, string Use, string Kid, string Alg, string N, string E);
}
