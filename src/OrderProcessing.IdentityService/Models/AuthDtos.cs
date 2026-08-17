using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.IdentityService.Models;

public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters.")] string Password);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record RefreshRequest([Required] string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    Guid UserId,
    string Email,
    string Role);
