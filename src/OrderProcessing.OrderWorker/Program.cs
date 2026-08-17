using OrderProcessing.Infrastructure;
using OrderProcessing.OrderWorker.Consumers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.AddHostedService<OrderCreatedConsumer>();

var host = builder.Build();
host.Run();
