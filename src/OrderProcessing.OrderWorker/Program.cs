using OrderProcessing.Infrastructure;
using OrderProcessing.OrderWorker;
using OrderProcessing.OrderWorker.Consumers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.Configure<OrderProcessingOptions>(builder.Configuration.GetSection(OrderProcessingOptions.SectionName));
builder.Services.AddHostedService<OrderCreatedConsumer>();
builder.Services.AddProcessingRecovery(builder.Configuration);

var host = builder.Build();
host.Run();

// Kept internal (unlike Api/Program.cs, which is deliberately public for WebApplicationFactory<Program>):
// IntegrationTests references both this project and Api's, and an implicit top-level Program class
// is public by default — without this, the two "Program" types collide (CS0433) the moment both
// are referenced by the same project.
internal partial class Program;
