using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Messaging.Contracts;
using ContosoInsurance.Worker.Claims.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ContosoInsurance.Worker.Claims;

public sealed class ClaimsWorker(
    IServiceScopeFactory scopeFactory,
    IConnection rabbitConnection,
    ILogger<ClaimsWorker> logger) : BackgroundService
{
    private const string ConsumerName = "worker-claims";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) => await ProcessMessageAsync(channel, eventArgs, stoppingToken);
        await channel.BasicConsumeAsync(MessagingTopology.PrivateClaimIntakeQueue, false, consumer, stoppingToken);

        logger.LogInformation("Claims worker is consuming queue {QueueName}", MessagingTopology.PrivateClaimIntakeQueue);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(IChannel channel, BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
    {
        var envelope = MessagingSerializer.Deserialize<ClaimSubmittedEvent>(eventArgs.Body);
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
            var claimCase = await db.PrivateClaimCases
                .Include(item => item.AuditTrail)
                .Include(item => item.Notes)
                .FirstOrDefaultAsync(item => item.PublicClaimId == payload.PublicClaimId, cancellationToken);

            if (claimCase is null)
            {
                claimCase = new PrivateClaimCase
                {
                    Id = Guid.NewGuid(),
                    PublicClaimId = payload.PublicClaimId,
                    WorkflowCorrelationId = payload.WorkflowCorrelationId,
                    ClaimNumber = payload.ClaimNumber,
                    Description = payload.Description,
                    Amount = payload.Amount,
                    IncidentDate = payload.IncidentDate,
                    PolicyId = payload.PolicyId,
                    Status = ClaimStatus.Submitted,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                claimCase.AuditTrail.Add(new ClaimCaseAuditEntry
                {
                    Id = Guid.NewGuid(),
                    Action = "claim.submitted",
                    ToStatus = ClaimStatus.Submitted,
                    PerformedBy = ConsumerName,
                    Details = "Claim submission consumed from public API.",
                    OccurredAtUtc = DateTime.UtcNow
                });

                db.PrivateClaimCases.Add(claimCase);
            }
            else
            {
                claimCase.Description = payload.Description;
                claimCase.Amount = payload.Amount;
                claimCase.IncidentDate = payload.IncidentDate;
                claimCase.PolicyId = payload.PolicyId;
                claimCase.UpdatedAtUtc = DateTime.UtcNow;
            }

            var nextStatus = await DetermineStatusAsync(db, claimCase, cancellationToken);
            var previousStatus = claimCase.Status;
            claimCase.Status = nextStatus.status;
            claimCase.ValidationSummary = nextStatus.summary;
            claimCase.UpdatedAtUtc = DateTime.UtcNow;
            claimCase.AuditTrail.Add(new ClaimCaseAuditEntry
            {
                Id = Guid.NewGuid(),
                Action = "claim.status.changed",
                FromStatus = previousStatus,
                ToStatus = nextStatus.status,
                PerformedBy = ConsumerName,
                Details = nextStatus.summary,
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
                RoutingKeys.ClaimStatusChangedV1,
                MessageTypes.ClaimStatusChanged,
                ConsumerName,
                "private",
                "claim",
                payload.PublicClaimId.ToString(),
                envelope.CorrelationId,
                envelope.MessageId,
                new ClaimStatusChangedEvent(payload.PublicClaimId, claimCase.Id, payload.WorkflowCorrelationId, claimCase.ClaimNumber, claimCase.Status.ToString(), claimCase.ValidationSummary, claimCase.AssignedToDisplayName, DateTime.UtcNow)));

            db.OutboxMessages.Add(OutboxMessageFactory.Create(
                MessagingTopology.EventsExchange,
                RoutingKeys.ClaimProjectionUpdatedV1,
                MessageTypes.ClaimProjectionUpdated,
                ConsumerName,
                "public",
                "claim",
                payload.PublicClaimId.ToString(),
                envelope.CorrelationId,
                envelope.MessageId,
                new ClaimProjectionUpdatedEvent(payload.PublicClaimId, payload.WorkflowCorrelationId, claimCase.ClaimNumber, claimCase.Status.ToString(), claimCase.ValidationSummary ?? $"Claim is {claimCase.Status}.", DateTime.UtcNow)));

            await db.SaveChangesAsync(cancellationToken);
            await outboxDispatcher.DispatchPendingAsync(cancellationToken);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Claims worker failed to process delivery {DeliveryTag}", eventArgs.DeliveryTag);
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
        }
    }

    private static async Task<(ClaimStatus status, string summary)> DetermineStatusAsync(InsuranceDbContext db, PrivateClaimCase claimCase, CancellationToken cancellationToken)
    {
        var policy = await db.Policies.AsNoTracking().FirstOrDefaultAsync(item => item.Id == claimCase.PolicyId, cancellationToken);
        if (policy is null)
        {
            return (ClaimStatus.Denied, "Claim denied because the referenced policy was not found.");
        }

        if (policy.Status != PolicyStatus.Active)
        {
            return (ClaimStatus.Denied, $"Claim denied because policy {policy.PolicyNumber} is {policy.Status}.");
        }

        if (claimCase.Amount > policy.CoverageAmount)
        {
            return (ClaimStatus.Denied, $"Claim exceeds available coverage of {policy.CoverageAmount:C}.");
        }

        return (ClaimStatus.UnderReview, $"Claim validated against policy {policy.PolicyNumber} and routed to review.");
    }
}
