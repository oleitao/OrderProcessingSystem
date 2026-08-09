using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderProcessing.Infrastructure.Database;

namespace OrderProcessing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OrderDb")
            ?? throw new InvalidOperationException("Connection string 'OrderDb' is not configured.");

        services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
