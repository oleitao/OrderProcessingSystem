using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderProcessing.Infrastructure.Database;

/// <summary>
/// Lets `dotnet ef` build an <see cref="OrderDbContext"/> at design time without going through
/// the Api host's DI container. Only used for migrations tooling, never at runtime.
/// </summary>
public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    private const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=orderprocessing;Username=orderprocessing;Password=orderprocessing";

    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__ORDERDB") ?? LocalDevConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<OrderDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new OrderDbContext(optionsBuilder.Options);
    }
}
