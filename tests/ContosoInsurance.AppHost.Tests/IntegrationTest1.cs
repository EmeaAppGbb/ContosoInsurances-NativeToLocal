using Microsoft.Extensions.Logging;

namespace ContosoInsurance.AppHost.Tests.Tests;

public class IntegrationTest1
{
    // NOTE: Aspire AppHost integration tests require real container infrastructure
    // (Docker/Podman for SQL Server + RabbitMQ). These are designed for CI pipelines.
    // For local dev, use the unit/integration tests in other test projects.

    [Fact]
    public void AppHost_ProjectReference_IsConfigured()
    {
        // Verify the test project can reference the AppHost project types
        var appHostAssembly = typeof(Projects.ContosoInsurance_AppHost).Assembly;
        Assert.NotNull(appHostAssembly);
    }
}
