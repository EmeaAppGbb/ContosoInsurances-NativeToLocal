using System.Text;
using System.Text.Json;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ContosoInsurance.Worker.Consumers;

/// <summary>
/// Consumes claim events from RabbitMQ and processes them.
/// </summary>
public class ClaimEventConsumer(
    IServiceScopeFactory scopeFactory,
    IConnection rabbitConnection,
    ILogger<ClaimEventConsumer> logger) : BackgroundService
{
    private const string QueueName = "claim-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

        logger.LogInformation("ClaimEventConsumer started, listening on queue: {Queue}", QueueName);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.Span);
                var claimEvent = JsonSerializer.Deserialize<ClaimSubmittedEvent>(body);

                if (claimEvent is not null)
                {
                    await ProcessClaimAsync(claimEvent);
                }

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                logger.LogInformation("Processed claim event: {ClaimNumber}", claimEvent?.ClaimNumber);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing claim event");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessClaimAsync(ClaimSubmittedEvent claimEvent)
    {
        // Simulate real claim processing (2-5 seconds)
        var delay = Random.Shared.Next(2000, 5001);
        logger.LogInformation("Processing claim {ClaimNumber} — simulated delay {Delay}ms", claimEvent.ClaimNumber, delay);
        await Task.Delay(delay);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();

        var claim = await db.Claims.FindAsync(claimEvent.ClaimId);
        if (claim is null)
        {
            logger.LogWarning("Claim {ClaimId} not found", claimEvent.ClaimId);
            return;
        }

        claim.Status = ClaimStatus.UnderReview;
        await db.SaveChangesAsync();

        logger.LogInformation("Claim {ClaimNumber} moved to UnderReview (Amount: {Amount:C})",
            claim.ClaimNumber, claim.Amount);
    }
}

/// <summary>
/// Stub consumer for notification events (future implementation).
/// </summary>
public class NotificationConsumer(
    IConnection rabbitConnection,
    ILogger<NotificationConsumer> logger) : BackgroundService
{
    private const string QueueName = "notifications";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

        logger.LogInformation("NotificationConsumer started, listening on queue: {Queue}", QueueName);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.Span);
                logger.LogInformation("Notification received: {Body}", body);

                // TODO: Implement notification delivery (email, SMS, push)
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing notification");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}

/// <summary>
/// Health check for the RabbitMQ connection.
/// </summary>
public class RabbitMqHealthCheck(IConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (connection.IsOpen)
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ connection is open."));

        return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ connection is closed."));
    }
}

/// <summary>
/// Event record matching the API's published event.
/// </summary>
public record ClaimSubmittedEvent(
    Guid ClaimId,
    string ClaimNumber,
    Guid PolicyId,
    decimal Amount,
    string Description,
    DateTime SubmittedAt);
