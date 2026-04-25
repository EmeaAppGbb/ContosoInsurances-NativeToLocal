using System.Net;
using System.Net.Http.Json;
using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data.Enums;
using FluentAssertions;

namespace ContosoInsurance.Api.Tests;

public class PolicyEndpointTests : IClassFixture<ContosoApiFactory>
{
    private readonly HttpClient _client;

    public PolicyEndpointTests(ContosoApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<CustomerResponse> CreateTestCustomer()
    {
        var request = new CreateCustomerRequest("Policy", "Tester", $"pol-{Guid.NewGuid()}@test.com", null, null);
        var response = await _client.PostAsJsonAsync("/api/customers", request);
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
    }

    [Fact]
    public async Task GetPolicies_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/policies");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreatePolicy_ReturnsCreated_WithGeneratedPolicyNumber()
    {
        var customer = await CreateTestCustomer();

        var request = new CreatePolicyRequest(
            customer.Id, PolicyType.Auto, 50000m,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1));

        var response = await _client.PostAsJsonAsync("/api/policies", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<PolicyResponse>();
        created.Should().NotBeNull();
        created!.PolicyNumber.Should().StartWith("POL-");
        created.Status.Should().Be(PolicyStatus.Draft);
    }

    [Fact]
    public async Task GetPolicy_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/policies/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ActivatePolicy_ReturnsOk_ForDraftPolicy()
    {
        var customer = await CreateTestCustomer();
        var createReq = new CreatePolicyRequest(
            customer.Id, PolicyType.Home, 250000m,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1));
        var createResp = await _client.PostAsJsonAsync("/api/policies", createReq);
        var created = await createResp.Content.ReadFromJsonAsync<PolicyResponse>();

        var response = await _client.PostAsync($"/api/policies/{created!.Id}/activate", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var activated = await response.Content.ReadFromJsonAsync<PolicyResponse>();
        activated!.Status.Should().Be(PolicyStatus.Active);
    }

    [Fact]
    public async Task CancelPolicy_ReturnsOk()
    {
        var customer = await CreateTestCustomer();
        var createReq = new CreatePolicyRequest(
            customer.Id, PolicyType.Auto, 30000m,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1));
        var createResp = await _client.PostAsJsonAsync("/api/policies", createReq);
        var created = await createResp.Content.ReadFromJsonAsync<PolicyResponse>();

        var response = await _client.PostAsync($"/api/policies/{created!.Id}/cancel", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cancelled = await response.Content.ReadFromJsonAsync<PolicyResponse>();
        cancelled!.Status.Should().Be(PolicyStatus.Cancelled);
    }

    [Fact]
    public async Task CreatePolicy_InvalidCustomer_Returns404()
    {
        var request = new CreatePolicyRequest(
            Guid.NewGuid(), PolicyType.Auto, 50000m,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1));

        var response = await _client.PostAsJsonAsync("/api/policies", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatePolicy_EndDateBeforeStartDate_Returns400()
    {
        var customer = await CreateTestCustomer();
        var request = new CreatePolicyRequest(
            customer.Id, PolicyType.Auto, 50000m,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(-1));

        var response = await _client.PostAsJsonAsync("/api/policies", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
