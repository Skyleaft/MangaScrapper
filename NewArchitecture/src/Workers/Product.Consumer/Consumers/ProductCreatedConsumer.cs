using Microsoft.Extensions.Logging;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.Messaging;

namespace Product.Consumer.Consumers;

/// <summary>
/// Native consumer for <see cref="ProductCreatedIntegrationEvent"/>.
/// Add your downstream business logic here (e.g., sync to search index, send email, update cache).
/// </summary>
public sealed class ProductCreatedConsumer(
    ILogger<ProductCreatedConsumer> logger)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[ProductCreatedConsumer] Received ProductCreatedIntegrationEvent. " +
            "ProductId={ProductId}, Name={Name}, Price={Price} {Currency}",
            integrationEvent.ProductId,
            integrationEvent.Name,
            integrationEvent.Price,
            integrationEvent.Currency);

        // TODO: Add downstream logic here, e.g.:
        // - Sync to Elasticsearch / search index
        // - Update a read model / projection
        // - Trigger a notification
        // - Call an external API

        await Task.CompletedTask;
    }
}
