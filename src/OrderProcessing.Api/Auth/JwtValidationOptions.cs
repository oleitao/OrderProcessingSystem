namespace OrderProcessing.Api.Auth;

public sealed class JwtValidationOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string JwksUri { get; set; } = string.Empty;
    public int RefreshIntervalSeconds { get; set; } = 300;
}
