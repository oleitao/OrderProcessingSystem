using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OrderProcessing.Api.Auth;

/// <summary>
/// Validates JWTs issued by OrderProcessing.IdentityService. The Api never issues, stores, or
/// checks passwords for tokens — it only verifies the signature (against IdentityService's
/// public key, fetched via JWKS) and reads claims (see JwksCacheRefresherWorker/JwksCache).
///
/// Lives in the Api project rather than Infrastructure: Microsoft.AspNetCore.Authentication.JwtBearer
/// pulls in the ASP.NET Core shared framework, which OrderWorker's lean runtime image (no ASP.NET
/// Core) can't satisfy — putting it in Infrastructure would drag that dependency into every
/// consumer, including hosts that have nothing to do with HTTP.
/// </summary>
public static class JwtAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtValidationOptions>(configuration.GetSection(JwtValidationOptions.SectionName));
        services.AddHttpClient();
        services.AddSingleton<JwksCache>();
        services.AddHostedService<JwksCacheRefresherWorker>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configure<TDep1, TDep2> resolves JwksCache/JwtValidationOptions from DI once the
        // container is built — AddJwtBearer's own configure lambda runs too early for that.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksCache, IOptions<JwtValidationOptions>>((bearerOptions, jwksCache, jwtValidationOptions) =>
            {
                var validation = jwtValidationOptions.Value;

                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = validation.Issuer,
                    ValidateAudience = true,
                    ValidAudience = validation.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    // Reads from JwksCache, populated out-of-band by JwksCacheRefresherWorker — this
                    // resolver must stay synchronous, so it never makes an HTTP call itself.
                    IssuerSigningKeyResolver = (_, _, kid, _) =>
                        jwksCache.GetKeys().Where(key => key.KeyId == kid).ToList()
                };
            });

        services.AddAuthorization();

        return services;
    }
}
