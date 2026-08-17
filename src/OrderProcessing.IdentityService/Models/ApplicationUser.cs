namespace OrderProcessing.IdentityService.Models;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

// Named "ApplicationUser", not "User" — ControllerBase already exposes a "User" property
// (the request's ClaimsPrincipal), and a same-named type in scope inside a controller resolves
// to that property instead of this class, producing confusing unrelated overload-resolution errors.
public sealed class ApplicationUser
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = Roles.Customer;
    public DateTime CreatedAtUtc { get; private set; }

    // Required by EF Core for materialization; kept private so users can only be built through Create.
    private ApplicationUser() { }

    public static ApplicationUser Create(string email, string passwordHash, string role = Roles.Customer)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    // Only PasswordHasher<ApplicationUser> should ever call this — it needs to rewrite the stored
    // hash when the hasher reports the old one uses an outdated algorithm.
    public void UpdatePasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
