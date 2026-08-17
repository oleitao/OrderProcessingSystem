using Microsoft.EntityFrameworkCore;
using OrderProcessing.IdentityService.Models;

namespace OrderProcessing.IdentityService.Data;

// Named "IdentityDataContext", not "IdentityDbContext" — that name collides with
// Microsoft.AspNetCore.Identity.EntityFrameworkCore's own base class, which this project
// deliberately does not use (see Program.cs for why: PasswordHasher<T> without the rest of ASP.NET
// Core Identity's UserManager/RoleManager/store machinery).
public sealed class IdentityDataContext(DbContextOptions<IdentityDataContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(50).IsRequired();
            entity.Property(user => user.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            // A refresh token is looked up by its hash on every /auth/refresh call — this index is
            // what keeps that a fast lookup instead of a table scan.
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.Property(token => token.ExpiresAtUtc).IsRequired();
            entity.Property(token => token.CreatedAtUtc).IsRequired();

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
