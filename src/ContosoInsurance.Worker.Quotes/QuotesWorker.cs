using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Messaging.Contracts;
using ContosoInsurance.Worker.Quotes.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ContosoInsurance.Worker.Quotes;

public sealed class QuotesWorker(
    IServiceScopeFactory scopeFactory,
    IConnection rabbitConnection,
    ILogger<QuotesWorker> logger) : BackgroundService
{
    private const string ConsumerName = "worker-quotes";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) => await ProcessMessageAsync(channel, eventArgs, stoppingToken);
        await channel.BasicConsumeAsync(MessagingTopology.PrivateQuoteIntakeQueue, false, consumer, stoppingToken);

        logger.LogInformation("Quotes worker is consuming queue {QueueName}", MessagingTopology.PrivateQuoteIntakeQueue);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(IChannel channel, BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
    {
        var envelope = MessagingSerializer.Deserialize<QuoteRequestedEvent>(eventArgs.Body);
        if (envelope?.Payload is null)
        {
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

            var alreadyProcessed = await db.ProcessedMessages.AnyAsync(
                message => message.MessageId == envelope.MessageId && message.ConsumerName == ConsumerName,
                cancellationToken);

            if (alreadyProcessed)
            {
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
                return;
            }

            var payload = envelope.Payload;
            var policyType = Enum.Parse<PolicyType>(payload.PolicyType, ignoreCase: true);
            var quoteCase = await db.PrivateQuoteCases
                .Include(item => item.AuditTrail)
                .Include(item => item.Notes)
                .FirstOrDefaultAsync(item => item.PublicQuoteId == payload.PublicQuoteId, cancellationToken);

            if (quoteCase is null)
            {
                quoteCase = new PrivateQuoteCase
                {
                    Id = Guid.NewGuid(),
                    PublicQuoteId = payload.PublicQuoteId,
                    WorkflowCorrelationId = payload.WorkflowCorrelationId,
                    QuoteNumber = payload.QuoteNumber,
                    Type = policyType,
                    CoverageAmount = payload.CoverageAmount,
                    CustomerId = payload.CustomerId,
                    Status = QuoteStatus.Requested,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                quoteCase.AuditTrail.Add(new QuoteCaseAuditEntry
                {
                    Id = Guid.NewGuid(),
                    Action = "quote.requested",
                    ToStatus = QuoteStatus.Requested,
                    PerformedBy = ConsumerName,
                    Details = "Quote request consumed from public API.",
                    OccurredAtUtc = DateTime.UtcNow
                });

                db.PrivateQuoteCases.Add(quoteCase);
            }
            else
            {
                quoteCase.Type = policyType;
                quoteCase.CoverageAmount = payload.CoverageAmount;
                quoteCase.CustomerId = payload.CustomerId;
                quoteCase.UpdatedAtUtc = DateTime.UtcNow;
            }

            quoteCase.Status = QuoteStatus.Underwriting;
            quoteCase.AuditTrail.Add(new QuoteCaseAuditEntry
            {
                Id = Guid.NewGuid(),
                Action = "quote.underwriting.started",
                FromStatus = QuoteStatus.Requested,
                ToStatus = QuoteStatus.Underwriting,
                PerformedBy = ConsumerName,
                Details = "Automated underwriting started.",
                OccurredAtUtc = DateTime.UtcNow
            });

            var underwriting = await EvaluateUnderwritingAsync(db, quoteCase, cancellationToken);
            quoteCase.Status = underwriting.status;
            quoteCase.EstimatedPremium = underwriting.estimatedPremium;
            quoteCase.UnderwritingSummary = underwriting.summary;
            quoteCase.CompletedAtUtc = underwriting.status is QuoteStatus.Approved or QuoteStatus.Declined ? DateTime.UtcNow : null;
            quoteCase.UpdatedAtUtc = DateTime.UtcNow;
            quoteCase.AuditTrail.Add(new QuoteCaseAuditEntry
            {
                Id = Guid.NewGuid(),
                Action = "quote.status.changed",
                FromStatus = QuoteStatus.Underwriting,
                ToStatus = underwriting.status,
                PerformedBy = ConsumerName,
                Details = underwriting.summary,
                OccurredAtUtc = DateTime.UtcNow
            });

            db.ProcessedMessages.Add(new ProcessedMessage
            {
                MessageId = envelope.MessageId,
                ConsumerName = ConsumerName,
                CorrelationId = envelope.CorrelationId,
                SubjectId = envelope.SubjectId,
                ProcessedAtUtc = DateTime.UtcNow
            });

            db.OutboxMessages.Add(OutboxMessageFactory.Create(
                MessagingTopology.EventsExchange,
                RoutingKeys.QuoteStatusChangedV1,
                MessageTypes.QuoteStatusChanged,
                ConsumerName,
                "private",
                "quote",
                payload.PublicQuoteId.ToString(),
                envelope.CorrelationId,
                envelope.MessageId,
                new QuoteStatusChangedEvent(payload.PublicQuoteId, quoteCase.Id, payload.WorkflowCorrelationId, quoteCase.QuoteNumber, quoteCase.Status.ToString(), quoteCase.EstimatedPremium, quoteCase.UnderwritingSummary, quoteCase.AssignedToDisplayName, DateTime.UtcNow)));

            db.OutboxMessages.Add(OutboxMessageFactory.Create(
                MessagingTopology.EventsExchange,
                RoutingKeys.QuoteProjectionUpdatedV1,
                MessageTypes.QuoteProjectionUpdated,
                ConsumerName,
                "public",
                "quote",
                payload.PublicQuoteId.ToString(),
                envelope.CorrelationId,
                envelope.MessageId,
                new QuoteProjectionUpdatedEvent(payload.PublicQuoteId, payload.WorkflowCorrelationId, quoteCase.QuoteNumber, quoteCase.Status.ToString(), quoteCase.EstimatedPremium, quoteCase.UnderwritingSummary ?? $"Quote is {quoteCase.Status}.", DateTime.UtcNow)));

            await db.SaveChangesAsync(cancellationToken);
            await outboxDispatcher.DispatchPendingAsync(cancellationToken);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quotes worker failed to process delivery {DeliveryTag}", eventArgs.DeliveryTag);
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
        }
    }

    private static async Task<(QuoteStatus status, decimal estimatedPremium, string summary)> EvaluateUnderwritingAsync(InsuranceDbContext db, PrivateQuoteCase quoteCase, CancellationToken cancellationToken)
    {
        var customerExists = await db.Customers.AsNoTracking().AnyAsync(item => item.Id == quoteCase.CustomerId, cancellationToken);
        if (!customerExists)
        {
            return (QuoteStatus.Declined, 0m, "Quote declined because the customer record was not found.");
        }

        var baseRate = quoteCase.Type switch
        {
            PolicyType.Auto => 0.035m,
            PolicyType.Home => 0.025m,
            PolicyType.Life => 0.015m,
            PolicyType.Health => 0.045m,
            PolicyType.Travel => 0.020m,
            PolicyType.Business => 0.040m,
            _ => 0.030m
        };

        var riskMultiplier = quoteCase.CoverageAmount switch
        {
            > 500000m => 1.30m,
            > 250000m => 1.15m,
            > 100000m => 1.05m,
            _ => 1.00m
        };

        var estimatedPremium = Math.Round(quoteCase.CoverageAmount * baseRate * riskMultiplier / 12m, 2);
        var approved = quoteCase.CoverageAmount <= 750000m && quoteCase.Type is not PolicyType.Business;
        var summary = approved
            ? $"Quote approved with automated premium calculation at {estimatedPremium:C}."
            : "Quote declined because it exceeded automated underwriting thresholds.";

        return (approved ? QuoteStatus.Approved : QuoteStatus.Declined, estimatedPremium, summary);
    }
}
