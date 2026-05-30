using System.Text.Json;
using ContosoInsurance.Messaging.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace ContosoInsurance.Worker.Projections.Messaging;

public sealed class RabbitMqTopologyInitializer(IConnection rabbitConnection, ILogger<RabbitMqTopologyInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(MessagingTopology.EventsExchange, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(MessagingTopology.DeadLetterExchange, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);

        await DeclareQueueAsync(channel, MessagingTopology.PublicClaimProjectionQueue, RoutingKeys.ClaimProjectionUpdatedV1, cancellationToken);
        await DeclareQueueAsync(channel, MessagingTopology.PublicQuoteProjectionQueue, RoutingKeys.QuoteProjectionUpdatedV1, cancellationToken);
        logger.LogInformation("Projection worker RabbitMQ topology initialized");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task DeclareQueueAsync(IChannel channel, string queueName, string routingKey, CancellationToken cancellationToken)
    {
        var queueArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = MessagingTopology.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = $"{routingKey}.dead"
        };

        await channel.QueueDeclareAsync(queueName, true, false, false, queueArgs, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queueName, MessagingTopology.EventsExchange, routingKey, cancellationToken: cancellationToken);
        var dlqName = MessagingTopology.DlqFor(queueName);
        await channel.QueueDeclareAsync(dlqName, true, false, false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(dlqName, MessagingTopology.DeadLetterExchange, $"{routingKey}.dead", cancellationToken: cancellationToken);
    }
}

public sealed class RabbitMqConnectionHealthCheck(IConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(connection.IsOpen
            ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
            : HealthCheckResult.Unhealthy("RabbitMQ connection is closed."));
}

public static class MessagingSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static MessageEnvelope<TPayload>? Deserialize<TPayload>(ReadOnlyMemory<byte> body)
        => JsonSerializer.Deserialize<MessageEnvelope<TPayload>>(body.Span, Options);
}
