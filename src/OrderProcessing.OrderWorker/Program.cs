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
