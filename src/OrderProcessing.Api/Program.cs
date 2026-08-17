using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderProcessing.Api.Auth;
using OrderProcessing.Api.Middleware;
using OrderProcessing.Application;
using OrderProcessing.Infrastructure;
using OrderProcessing.Infrastructure.Database;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);

var app = builder.Build();

// Applies pending EF Core migrations on startup — idempotent (already-applied migrations are
// skipped), and is what lets `docker compose up` bring up a fresh environment with no manual
// `dotnet ef database update` step. Runs before Kestrel starts listening, so anything depending
// on this container's health check (order-worker) only sees it healthy once the schema is ready.
using (var migrationScope = app.Services.CreateScope())
{
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Checks PostgreSQL (via OrderDbContext) and RabbitMQ (via RabbitMqHealthCheck) — registered in
// AddApplicationHealthChecks. Response is JSON so each dependency's status is visible on its own,
// not just an overall Healthy/Unhealthy.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (httpContext, report) =>
    {
        httpContext.Response.ContentType = "application/json";

        var payload = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds
            })
        });

        await httpContext.Response.WriteAsync(payload);
    }
});

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
