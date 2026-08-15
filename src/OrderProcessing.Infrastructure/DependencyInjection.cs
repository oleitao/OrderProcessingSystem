using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Infrastructure.Database;
using OrderProcessing.Infrastructure.Repositories;

namespace OrderProcessing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OrderDb")
            ?? throw new InvalidOperationException("Connection string 'OrderDb' is not configured.");

        services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        return services;
    }
}
