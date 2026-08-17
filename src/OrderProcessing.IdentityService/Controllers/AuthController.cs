using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderProcessing.IdentityService.Data;
using OrderProcessing.IdentityService.Models;
using OrderProcessing.IdentityService.Services;

namespace OrderProcessing.IdentityService.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(
    IdentityDataContext dbContext,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IJwtTokenService tokenService,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var alreadyExists = await dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken);
        if (alreadyExists)
            return Conflict(new ProblemDetails { Title = "Email already registered.", Status = StatusCodes.Status409Conflict });

        // PasswordHasher.HashPassword needs an ApplicationUser instance for its API shape, but
        // doesn't read anything from it — the password is hashed independently of who the user is.
        var placeholder = ApplicationUser.Create(normalizedEmail, "placeholder");
        var passwordHash = passwordHasher.HashPassword(placeholder, request.Password);
        var user = ApplicationUser.Create(normalizedEmail, passwordHash);

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent registration with the same email — the unique index
            // on Email is the real guarantee, this pre-check above was only a fast path. await isn't
            // allowed inside a catch filter, so the recheck happens in the body instead.
            var stillConflicts = await dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
            if (!stillConflicts)
                throw;

            return Conflict(new ProblemDetails { Title = "Email already registered.", Status = StatusCodes.Status409Conflict });
        }

        logger.LogInformation("User registered. UserId: {UserId}", user.Id);

        var response = await IssueTokensAsync(user, cancellationToken);
        return CreatedAtAction(nameof(Register), response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // Same "invalid credentials" response whether the email doesn't exist or the password is
        // wrong — never let a client distinguish the two, or login becomes a way to enumerate
        // which emails are registered.
        if (user is null)
            return Unauthorized(new ProblemDetails { Title = "Invalid credentials.", Status = StatusCodes.Status401Unauthorized });

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
            return Unauthorized(new ProblemDetails { Title = "Invalid credentials.", Status = StatusCodes.Status401Unauthorized });

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.UpdatePasswordHash(passwordHasher.HashPassword(user, request.Password));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("User logged in. UserId: {UserId}", user.Id);

        var response = await IssueTokensAsync(user, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var existingToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null || !existingToken.IsActive)
            return Unauthorized(new ProblemDetails { Title = "Invalid or expired refresh token.", Status = StatusCodes.Status401Unauthorized });

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == existingToken.UserId, cancellationToken);
        if (user is null)
            return Unauthorized(new ProblemDetails { Title = "Invalid or expired refresh token.", Status = StatusCodes.Status401Unauthorized });

        // Rotate: the presented refresh token is single-use. Revoking it here means a stolen-and-
        // replayed old token stops working the moment the legitimate client refreshes first.
        existingToken.Revoke();

        logger.LogInformation("Access token refreshed. UserId: {UserId}", user.Id);

        var response = await IssueTokensAsync(user, cancellationToken);
        return Ok(response);
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var (rawRefreshToken, refreshTokenEntity) = tokenService.CreateRefreshToken(user.Id);

        dbContext.RefreshTokens.Add(refreshTokenEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            accessToken,
            rawRefreshToken,
            DateTime.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenLifetimeMinutes),
            user.Id,
            user.Email,
            user.Role);
    }
}
