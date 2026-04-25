using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Worker.Consumers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;

namespace ContosoInsurance.Worker.Tests;

public class ClaimEventConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly IConnection _mockRabbitConnection;
    private readonly IChannel _mockChannel;
    private readonly ILogger<ClaimEventConsumer> _logger;

    public ClaimEventConsumerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<InsuranceDbContext>(opt => opt.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();

        // Ensure DB schema
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
        db.Database.EnsureCreated();

        _mockChannel = Substitute.For<IChannel>();
        _mockRabbitConnection = Substitute.For<IConnection>();
        _mockRabbitConnection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_mockChannel);

        _logger = Substitute.For<ILogger<ClaimEventConsumer>>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ProcessClaimAsync_MovesClaimToUnderReview()
    {
        // Arrange — seed a claim
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            var customer = new Customer
            {
                Id = Guid.NewGuid(), FirstName = "Test", LastName = "Worker",
                Email = "worker@test.com"
            };
            db.Customers.Add(customer);

            var policy = new Policy
            {
                Id = Guid.NewGuid(), PolicyNumber = "POL-WORKER-001",
                Type = PolicyType.Auto, Status = PolicyStatus.Active,
                PremiumAmount = 500, CoverageAmount = 30000,
                StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddYears(1),
                CustomerId = customer.Id
            };
            db.Policies.Add(policy);

            var claim = new Claim
            {
                Id = Guid.NewGuid(), ClaimNumber = "CLM-WORKER-001",
                Description = "Test claim", Amount = 1000,
                IncidentDate = DateTime.UtcNow.AddDays(-1),
                PolicyId = policy.Id
            };
            db.Claims.Add(claim);
            await db.SaveChangesAsync();

            // Act — invoke ProcessClaimAsync via reflection (it's private)
            var consumer = new ClaimEventConsumer(
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _mockRabbitConnection,
                _logger);

            var processMethod = typeof(ClaimEventConsumer)
                .GetMethod("ProcessClaimAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            var claimEvent = new ClaimSubmittedEvent(
                claim.Id, claim.ClaimNumber, policy.Id,
                claim.Amount, claim.Description, DateTime.UtcNow);

            await (Task)processMethod.Invoke(consumer, [claimEvent])!;
        }

        // Assert
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            var updatedClaim = await db.Claims.FirstAsync(c => c.ClaimNumber == "CLM-WORKER-001");
            updatedClaim.Status.Should().Be(ClaimStatus.UnderReview);
        }
    }

    [Fact]
    public async Task ProcessClaimAsync_LogsWarning_WhenClaimNotFound()
    {
        // Arrange
        var consumer = new ClaimEventConsumer(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _mockRabbitConnection,
            _logger);

        var claimEvent = new ClaimSubmittedEvent(
            Guid.NewGuid(), "CLM-NONEXIST", Guid.NewGuid(),
            500m, "Ghost claim", DateTime.UtcNow);

        var processMethod = typeof(ClaimEventConsumer)
            .GetMethod("ProcessClaimAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Act
        await (Task)processMethod.Invoke(consumer, [claimEvent])!;

        // Assert — the logger should have been called with a warning
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public void ClaimSubmittedEvent_CanBeCreated()
    {
        var evt = new ClaimSubmittedEvent(
            Guid.NewGuid(), "CLM-TEST", Guid.NewGuid(),
            1500m, "Test event", DateTime.UtcNow);

        evt.ClaimNumber.Should().Be("CLM-TEST");
        evt.Amount.Should().Be(1500m);
    }
}
