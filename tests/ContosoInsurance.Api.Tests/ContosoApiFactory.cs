using ContosoInsurance.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RabbitMQ.Client;

namespace ContosoInsurance.Api.Tests;

public class ContosoApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove ALL EF / SQL Server descriptors
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<InsuranceDbContext>)
                    || d.ServiceType == typeof(InsuranceDbContext)
                    || (d.ServiceType.IsGenericType &&
                        d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
                    || d.ServiceType.FullName?.Contains("EntityFramework") == true
                    || d.ServiceType.FullName?.Contains("SqlServer") == true
                    || d.ServiceType.FullName?.Contains("SqlClient") == true)
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            // SQLite in-memory
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<InsuranceDbContext>(opt => opt.UseSqlite(_connection));

            // Remove RabbitMQ and replace with NSubstitute mock
            var rabbitToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(IConnection)
                    || d.ServiceType.FullName?.Contains("RabbitMQ") == true)
                .ToList();
            foreach (var d in rabbitToRemove) services.Remove(d);

            var mockChannel = Substitute.For<IChannel>();
            mockChannel.QueueDeclareAsync(
                Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
                .Returns(new QueueDeclareOk("claim-events", 0, 0));
            mockChannel.BasicPublishAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
                .Returns(ValueTask.CompletedTask);

            var mockConnection = Substitute.For<IConnection>();
            mockConnection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
                .Returns(mockChannel);

            services.AddSingleton(mockConnection);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _connection?.Dispose();
        await base.DisposeAsync();
    }
}
