namespace OrderProcessing.IdentityService.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "OrderProcessing.IdentityService";
    public string Audience { get; set; } = "OrderProcessing.Api";
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}
