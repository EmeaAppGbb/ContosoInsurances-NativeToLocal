using System.Text;
using System.Text.Json;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace ContosoInsurance.Worker.Quotes.Messaging;

public interface IOutboxDispatcher
{
    Task DispatchPendingAsync(CancellationToken cancellationToken = default);
}

public sealed class RabbitMqOutboxDispatcher(
    InsuranceDbContext db,
    IConnection rabbitConnection,
    ILogger<RabbitMqOutboxDispatcher> logger) : IOutboxDispatcher
{
    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        var pendingMessages = await db.OutboxMessages
            .Where(message => message.PublishedAtUtc == null)
            .OrderBy(message => message.OccurredAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return;
        }

        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: cancellationToken);
        foreach (var pendingMessage in pendingMessages)
        {
            try
            {
                var body = Encoding.UTF8.GetBytes(pendingMessage.PayloadJson);
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = pendingMessage.MessageId.ToString(),
                    CorrelationId = pendingMessage.CorrelationId.ToString(),
                    Type = pendingMessage.MessageType
                };

                await channel.BasicPublishAsync(pendingMessage.Exchange, pendingMessage.RoutingKey, false, properties, body, cancellationToken);
                pendingMessage.PublishedAtUtc = DateTime.UtcNow;
                pendingMessage.LastError = null;
            }
            catch (Exception ex)
            {
                pendingMessage.PublishAttempts += 1;
                pendingMessage.LastError = ex.Message;
                logger.LogError(ex, "Quotes worker failed to publish outbox message {MessageId}", pendingMessage.MessageId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class OutboxPublisherService(IServiceScopeFactory scopeFactory, ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Quotes worker outbox dispatch iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

public sealed class RabbitMqTopologyInitializer(IConnection rabbitConnection, ILogger<RabbitMqTopologyInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(MessagingTopology.CommandsExchange, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(MessagingTopology.EventsExchange, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(MessagingTopology.DeadLetterExchange, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);

        await DeclareQueueAsync(channel, MessagingTopology.PrivateQuoteIntakeQueue, RoutingKeys.QuoteRequestedV1, cancellationToken);
        logger.LogInformation("Quotes worker RabbitMQ topology initialized");
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
        await channel.QueueBindAsync(queueName, MessagingTopology.CommandsExchange, routingKey, cancellationToken: cancellationToken);
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

public static class OutboxMessageFactory
{
    public static OutboxMessage Create<TPayload>(string exchange, string routingKey, string messageType, string sourceSystem, string classification, string subjectType, string subjectId, Guid correlationId, Guid? causationId, TPayload payload)
    {
        var envelope = new MessageEnvelope<TPayload>
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CausationId = causationId,
            MessageType = messageType,
            SchemaVersion = 1,
            OccurredAtUtc = DateTime.UtcNow,
            SourceSystem = sourceSystem,
            Classification = classification,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Payload = payload
        };

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            Exchange = exchange,
            RoutingKey = routingKey,
            MessageType = envelope.MessageType,
            SchemaVersion = envelope.SchemaVersion,
            SourceSystem = envelope.SourceSystem,
            Classification = envelope.Classification,
            SubjectType = envelope.SubjectType,
            SubjectId = envelope.SubjectId,
            OccurredAtUtc = envelope.OccurredAtUtc,
            PayloadJson = JsonSerializer.Serialize(envelope, MessagingSerializer.Options)
        };
    }
}
