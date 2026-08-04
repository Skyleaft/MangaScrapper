using Microsoft.Extensions.Logging;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.Messaging;

namespace Notification.Consumer.Consumers;

/// <summary>
/// Receives integration events and dispatches notifications (email, push, SMS, webhook).
/// Extend this consumer to handle multiple notification event types.
/// </summary>
public sealed class NotificationConsumer(ILogger<NotificationConsumer> logger)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[NotificationConsumer] Product created notification. ProductId={ProductId}, Name={Name}",
            integrationEvent.ProductId, integrationEvent.Name);

        // TODO: Dispatch notifications via your preferred channel:
        // - Email (SendGrid, SMTP)
        // - Push notifications (FCM, APNs)
        // - SMS (Twilio)
        // - Webhook relay
        // - Slack / Teams bot

        await Task.CompletedTask;
    }
}
