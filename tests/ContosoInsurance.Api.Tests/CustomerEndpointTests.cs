using System.Net;
using System.Net.Http.Json;
using ContosoInsurance.Api.DTOs;
using FluentAssertions;

namespace ContosoInsurance.Api.Tests;

public class CustomerEndpointTests : IClassFixture<ContosoApiFactory>
{
    private readonly HttpClient _client;

    public CustomerEndpointTests(ContosoApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCustomers_ReturnsOk_WithPaginatedResponse()
    {
        var response = await _client.GetAsync("/api/customers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PaginatedResponse<CustomerResponse>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateCustomer_ReturnsCreated()
    {
        var request = new CreateCustomerRequest("Alice", "Smith", $"alice-{Guid.NewGuid()}@test.com", "555-1234", null);

        var response = await _client.PostAsJsonAsync("/api/customers", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CustomerResponse>();
        created.Should().NotBeNull();
        created!.FirstName.Should().Be("Alice");
        created.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetCustomer_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/customers/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCustomer_ReturnsCustomer_AfterCreation()
    {
        var request = new CreateCustomerRequest("Bob", "Jones", $"bob-{Guid.NewGuid()}@test.com", null, null);
        var createResponse = await _client.PostAsJsonAsync("/api/customers", request);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerResponse>();

        var response = await _client.GetAsync($"/api/customers/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loaded = await response.Content.ReadFromJsonAsync<CustomerResponse>();
        loaded!.LastName.Should().Be("Jones");
    }

    [Fact]
    public async Task CreateCustomer_DuplicateEmail_Returns409()
    {
        var email = $"dup-{Guid.NewGuid()}@test.com";
        var request = new CreateCustomerRequest("First", "User", email, null, null);
        await _client.PostAsJsonAsync("/api/customers", request);

        var duplicate = new CreateCustomerRequest("Second", "User", email, null, null);
        var response = await _client.PostAsJsonAsync("/api/customers", duplicate);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteCustomer_ReturnsNoContent()
    {
        var request = new CreateCustomerRequest("Delete", "Me", $"del-{Guid.NewGuid()}@test.com", null, null);
        var createResponse = await _client.PostAsJsonAsync("/api/customers", request);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerResponse>();

        var response = await _client.DeleteAsync($"/api/customers/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCustomer_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var response = await _client.DeleteAsync($"/api/customers/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
