using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Api.Services;

namespace ContosoInsurance.Api.Endpoints;

public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/customers").WithTags("Customers");

        group.MapGet("/", async (ICustomerService svc, string? search, int page = 1, int pageSize = 20) =>
            Results.Ok(await svc.GetCustomersAsync(search, page, pageSize)))
            .WithName("GetCustomers")
            .WithSummary("List customers with optional search and pagination");

        group.MapGet("/{id:guid}", async (Guid id, ICustomerService svc) =>
            await svc.GetCustomerByIdAsync(id) is { } customer
                ? Results.Ok(customer)
                : Results.NotFound())
            .WithName("GetCustomer")
            .WithSummary("Get a customer by ID");

        group.MapPost("/", async (CreateCustomerRequest request, ICustomerService svc) =>
        {
            var customer = await svc.CreateCustomerAsync(request);
            return Results.Created($"/api/customers/{customer.Id}", customer);
        })
        .WithName("CreateCustomer")
        .WithSummary("Create a new customer");

        group.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICustomerService svc) =>
            await svc.UpdateCustomerAsync(id, request) is { } customer
                ? Results.Ok(customer)
                : Results.NotFound())
            .WithName("UpdateCustomer")
            .WithSummary("Update an existing customer");

        group.MapDelete("/{id:guid}", async (Guid id, ICustomerService svc) =>
            await svc.DeleteCustomerAsync(id)
                ? Results.NoContent()
                : Results.NotFound())
            .WithName("DeleteCustomer")
            .WithSummary("Delete a customer (no active policies)");

        return group;
    }
}
