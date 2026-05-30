using ContosoInsurance.Data;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Messaging.Contracts;
using ContosoInsurance.Worker.Projections.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ContosoInsurance.Worker.Projections;

public sealed class ProjectionWorker(
    IServiceScopeFactory scopeFactory,
    IConnection rabbitConnection,
    ILogger<ProjectionWorker> logger) : BackgroundService
{
    private const string ClaimConsumerName = "worker-projections-claim";
    private const string QuoteConsumerName = "worker-projections-quote";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var claimChannel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var quoteChannel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);

        await claimChannel.BasicQosAsync(0, 1, false, stoppingToken);
        await quoteChannel.BasicQosAsync(0, 1, false, stoppingToken);

        var claimConsumer = new AsyncEventingBasicConsumer(claimChannel);
        claimConsumer.ReceivedAsync += async (_, eventArgs) => await ProcessClaimProjectionAsync(claimChannel, eventArgs, stoppingToken);
        await claimChannel.BasicConsumeAsync(MessagingTopology.PublicClaimProjectionQueue, false, claimConsumer, stoppingToken);

        var quoteConsumer = new AsyncEventingBasicConsumer(quoteChannel);
        quoteConsumer.ReceivedAsync += async (_, eventArgs) => await ProcessQuoteProjectionAsync(quoteChannel, eventArgs, stoppingToken);
        await quoteChannel.BasicConsumeAsync(MessagingTopology.PublicQuoteProjectionQueue, false, quoteConsumer, stoppingToken);

        logger.LogInformation("Projection worker is consuming public projection queues");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessClaimProjectionAsync(IChannel channel, BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
    {
        var envelope = MessagingSerializer.Deserialize<ClaimProjectionUpdatedEvent>(eventArgs.Body);
        if (envelope?.Payload is null)
        {
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            var alreadyProcessed = await db.ProcessedMessages.AnyAsync(item => item.MessageId == envelope.MessageId && item.ConsumerName == ClaimConsumerName, cancellationToken);
            if (alreadyProcessed)
            {
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
                return;
            }

            var payload = envelope.Payload;
            var projection = await db.ClaimProjections.FirstOrDefaultAsync(item => item.PublicClaimId == payload.PublicClaimId, cancellationToken);
            if (projection is null)
            {
                projection = new ClaimProjection { PublicClaimId = payload.PublicClaimId };
                db.ClaimProjections.Add(projection);
            }

            projection.ClaimNumber = payload.ClaimNumber;
            projection.WorkflowCorrelationId = payload.WorkflowCorrelationId;
            projection.PublicStatus = payload.PublicStatus;
            projection.StatusSummary = payload.StatusSummary;
            projection.LastUpdatedAtUtc = payload.UpdatedAtUtc;
            projection.LastMessageId = envelope.MessageId;

            db.ProcessedMessages.Add(new ProcessedMessage
            {
                MessageId = envelope.MessageId,
                ConsumerName = ClaimConsumerName,
                CorrelationId = envelope.CorrelationId,
                SubjectId = envelope.SubjectId,
                ProcessedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Projection worker failed to update claim projection for delivery {DeliveryTag}", eventArgs.DeliveryTag);
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
        }
    }

    private async Task ProcessQuoteProjectionAsync(IChannel channel, BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
    {
        var envelope = MessagingSerializer.Deserialize<QuoteProjectionUpdatedEvent>(eventArgs.Body);
        if (envelope?.Payload is null)
        {
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            var alreadyProcessed = await db.ProcessedMessages.AnyAsync(item => item.MessageId == envelope.MessageId && item.ConsumerName == QuoteConsumerName, cancellationToken);
            if (alreadyProcessed)
            {
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
                return;
            }

            var payload = envelope.Payload;
            var projection = await db.QuoteProjections.FirstOrDefaultAsync(item => item.PublicQuoteId == payload.PublicQuoteId, cancellationToken);
            if (projection is null)
            {
                projection = new QuoteProjection { PublicQuoteId = payload.PublicQuoteId };
                db.QuoteProjections.Add(projection);
            }

            projection.QuoteNumber = payload.QuoteNumber;
            projection.WorkflowCorrelationId = payload.WorkflowCorrelationId;
            projection.PublicStatus = payload.PublicStatus;
            projection.EstimatedPremium = payload.EstimatedPremium;
            projection.StatusSummary = payload.StatusSummary;
            projection.LastUpdatedAtUtc = payload.UpdatedAtUtc;
            projection.LastMessageId = envelope.MessageId;

            db.ProcessedMessages.Add(new ProcessedMessage
            {
                MessageId = envelope.MessageId,
                ConsumerName = QuoteConsumerName,
                CorrelationId = envelope.CorrelationId,
                SubjectId = envelope.SubjectId,
                ProcessedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Projection worker failed to update quote projection for delivery {DeliveryTag}", eventArgs.DeliveryTag);
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
        }
    }
}
