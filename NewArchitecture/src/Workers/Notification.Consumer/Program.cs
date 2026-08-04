using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.DependencyInjection;
using NovaStack.Infrastructure.Logging;
using NovaStack.Infrastructure.Messaging.Options;
using Notification.Consumer.Consumers;
using Serilog;

LoggingExtensions.BootstrapLogger();

try
{
    Log.Information("Starting Notification.Consumer...");

    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();

    // ── Native Messaging ──────────────────────────────────────────────────────
    var messagingOptions = builder.Configuration
        .GetSection(MessagingOptions.SectionName)
        .Get<MessagingOptions>() ?? new MessagingOptions();

    builder.Services.Configure<MessagingOptions>(
        builder.Configuration.GetSection(MessagingOptions.SectionName));

    builder.Services.AddScoped<NotificationConsumer>();

    if (messagingOptions.Provider == MessagingProvider.RabbitMQ)
    {
        builder.Services.AddRabbitMqConsumer<ProductCreatedIntegrationEvent, NotificationConsumer>(
            "notification-queue");
    }
    else if (messagingOptions.Provider == MessagingProvider.Kafka)
    {
        builder.Services.AddKafkaConsumer<ProductCreatedIntegrationEvent, NotificationConsumer>(
            "product-created",
            messagingOptions.Kafka.GroupId);
    }

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Notification.Consumer terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
