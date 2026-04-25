using System.Net;
using System.Net.Http.Json;
using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data.Enums;
using FluentAssertions;

namespace ContosoInsurance.Api.Tests;

public class ClaimEndpointTests : IClassFixture<ContosoApiFactory>
{
    private readonly HttpClient _client;

    public ClaimEndpointTests(ContosoApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>Creates a customer + active policy suitable for claims.</summary>
    private async Task<PolicyResponse> CreateActivePolicy()
    {
        var custReq = new CreateCustomerRequest("Claim", "Tester", $"claim-{Guid.NewGuid()}@test.com", null, null);
        var custResp = await _client.PostAsJsonAsync("/api/customers", custReq);
        var customer = (await custResp.Content.ReadFromJsonAsync<CustomerResponse>())!;

        var polReq = new CreatePolicyRequest(
            customer.Id, PolicyType.Auto, 30000m,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1));
        var polResp = await _client.PostAsJsonAsync("/api/policies", polReq);
        var policy = (await polResp.Content.ReadFromJsonAsync<PolicyResponse>())!;

        // Activate the policy
        var activateResp = await _client.PostAsync($"/api/policies/{policy.Id}/activate", null);
        return (await activateResp.Content.ReadFromJsonAsync<PolicyResponse>())!;
    }

    [Fact]
    public async Task GetClaims_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/claims");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SubmitClaim_ReturnsCreated_WithGeneratedClaimNumber()
    {
        var policy = await CreateActivePolicy();

        var request = new SubmitClaimRequest(
            policy.Id, "Minor fender bender", 2500m, DateTime.UtcNow.AddDays(-2));

        var response = await _client.PostAsJsonAsync("/api/claims", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ClaimResponse>();
        created.Should().NotBeNull();
        created!.ClaimNumber.Should().StartWith("CLM-");
        created.Status.Should().Be(ClaimStatus.Submitted);
    }

    [Fact]
    public async Task GetClaim_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/claims/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetClaim_ReturnsClaim_AfterSubmission()
    {
        var policy = await CreateActivePolicy();
        var request = new SubmitClaimRequest(
            policy.Id, "Tree fell on car", 8000m, DateTime.UtcNow.AddDays(-1));

        var createResp = await _client.PostAsJsonAsync("/api/claims", request);
        var created = (await createResp.Content.ReadFromJsonAsync<ClaimResponse>())!;

        var response = await _client.GetAsync($"/api/claims/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loaded = await response.Content.ReadFromJsonAsync<ClaimResponse>();
        loaded!.Description.Should().Be("Tree fell on car");
    }

    [Fact]
    public async Task SubmitClaim_AgainstInactivePolicy_Returns409()
    {
        // Create a customer and draft policy (not activated)
        var custReq = new CreateCustomerRequest("Inactive", "Tester", $"inactive-{Guid.NewGuid()}@test.com", null, null);
        var custResp = await _client.PostAsJsonAsync("/api/customers", custReq);
        var customer = (await custResp.Content.ReadFromJsonAsync<CustomerResponse>())!;

        var polReq = new CreatePolicyRequest(
            customer.Id, PolicyType.Auto, 30000m,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1));
        var polResp = await _client.PostAsJsonAsync("/api/policies", polReq);
        var policy = (await polResp.Content.ReadFromJsonAsync<PolicyResponse>())!;

        var request = new SubmitClaimRequest(policy.Id, "Test", 1000m, DateTime.UtcNow);
        var response = await _client.PostAsJsonAsync("/api/claims", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SubmitClaim_ExceedsCoverage_Returns400()
    {
        var policy = await CreateActivePolicy();
        var request = new SubmitClaimRequest(
            policy.Id, "Exceeds coverage", 999999m, DateTime.UtcNow);

        var response = await _client.PostAsJsonAsync("/api/claims", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
