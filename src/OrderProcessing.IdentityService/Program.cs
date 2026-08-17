using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.IdentityService.Data;
using OrderProcessing.IdentityService.Models;
using OrderProcessing.IdentityService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<IdentityDataContext>((serviceProvider, options) =>
{
    var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("IdentityDb")
        ?? throw new InvalidOperationException("Connection string 'IdentityDb' is not configured.");

    options.UseSqlite(connectionString);
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<RsaKeyProvider>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
// PasswordHasher<T> used standalone (PBKDF2 hashing only) — deliberately not the rest of
// ASP.NET Core Identity (UserManager/RoleManager/stores): this project's ApplicationUser/RefreshToken
// entities and AuthController already cover what those would add, without the extra machinery.
builder.Services.AddSingleton<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDataContext>(name: "sqlite");

var app = builder.Build();

// Applies pending migrations and, in Development, seeds an Admin account so ownership-vs-Admin
// authorization scenarios can be exercised without building a separate role-promotion endpoint.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDataContext>();
    await dbContext.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
        await SeedDevelopmentAdminAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

// Exposes only the public key, in JWK format — this is what lets OrderProcessing.Api validate
// tokens without ever holding (or needing) the private signing key.
app.MapGet("/.well-known/jwks.json", (RsaKeyProvider keyProvider) =>
{
    var jwk = keyProvider.GetPublicJwk();

    return Results.Ok(new
    {
        keys = new[]
        {
            new
            {
                kty = jwk.Kty,
                use = jwk.Use,
                kid = jwk.Kid,
                alg = jwk.Alg,
                n = jwk.N,
                e = jwk.E
            }
        }
    });
});

app.MapHealthChecks("/health");

app.Run();

static async Task SeedDevelopmentAdminAsync(IServiceProvider services)
{
    var dbContext = services.GetRequiredService<IdentityDataContext>();
    var passwordHasher = services.GetRequiredService<IPasswordHasher<ApplicationUser>>();

    const string adminEmail = "admin@example.com";

    if (await dbContext.Users.AnyAsync(user => user.Email == adminEmail))
        return;

    var placeholder = ApplicationUser.Create(adminEmail, "placeholder");
    var passwordHash = passwordHasher.HashPassword(placeholder, "Admin123!");
    var admin = ApplicationUser.Create(adminEmail, passwordHash, Roles.Admin);

    dbContext.Users.Add(admin);
    await dbContext.SaveChangesAsync();
}

// Exposes the top-level Program for future test hosts (WebApplicationFactory<Program>).
public partial class Program;
