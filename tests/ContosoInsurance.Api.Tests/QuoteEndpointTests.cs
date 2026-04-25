using System.Net;
using System.Net.Http.Json;
using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data.Enums;
using FluentAssertions;

namespace ContosoInsurance.Api.Tests;

public class QuoteEndpointTests : IClassFixture<ContosoApiFactory>
{
    private readonly HttpClient _client;

    public QuoteEndpointTests(ContosoApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<CustomerResponse> CreateTestCustomer()
    {
        var request = new CreateCustomerRequest("Quote", "Tester", $"quote-{Guid.NewGuid()}@test.com", null, null);
        var response = await _client.PostAsJsonAsync("/api/customers", request);
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
    }

    [Fact]
    public async Task GetQuotes_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/quotes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateQuote_ReturnsCreated_WithGeneratedQuoteNumber()
    {
        var customer = await CreateTestCustomer();

        var request = new CreateQuoteRequest(customer.Id, PolicyType.Life, 500000m);

        var response = await _client.PostAsJsonAsync("/api/quotes", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<QuoteResponse>();
        created.Should().NotBeNull();
        created!.QuoteNumber.Should().StartWith("QTE-");
        created.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetQuote_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/quotes/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateQuote_SetsExpiresAt_30DaysFromNow()
    {
        var customer = await CreateTestCustomer();
        var request = new CreateQuoteRequest(customer.Id, PolicyType.Health, 100000m);

        var response = await _client.PostAsJsonAsync("/api/quotes", request);
        var created = await response.Content.ReadFromJsonAsync<QuoteResponse>();

        created!.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), precision: TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetQuote_ReturnsQuote_AfterCreation()
    {
        var customer = await CreateTestCustomer();
        var request = new CreateQuoteRequest(customer.Id, PolicyType.Travel, 25000m);

        var createResp = await _client.PostAsJsonAsync("/api/quotes", request);
        var created = (await createResp.Content.ReadFromJsonAsync<QuoteResponse>())!;

        var response = await _client.GetAsync($"/api/quotes/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loaded = await response.Content.ReadFromJsonAsync<QuoteResponse>();
        loaded!.CoverageAmount.Should().Be(25000m);
    }

    [Fact]
    public async Task CreateQuote_InvalidCustomer_Returns404()
    {
        var request = new CreateQuoteRequest(Guid.NewGuid(), PolicyType.Auto, 50000m);
        var response = await _client.PostAsJsonAsync("/api/quotes", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateQuote_CalculatesPremium()
    {
        var customer = await CreateTestCustomer();
        // Auto: 0.035 * 50000 / 12 = 145.83
        var request = new CreateQuoteRequest(customer.Id, PolicyType.Auto, 50000m);

        var response = await _client.PostAsJsonAsync("/api/quotes", request);
        var created = await response.Content.ReadFromJsonAsync<QuoteResponse>();

        created!.EstimatedPremium.Should().Be(145.83m);
    }
}
