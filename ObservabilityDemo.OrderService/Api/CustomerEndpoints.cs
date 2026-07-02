using Microsoft.AspNetCore.Mvc;
using ObservabilityDemo.OrderService.Infrastructure;
using ObservabilityDemo.OrderService.Services;

namespace ObservabilityDemo.OrderService.Api;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this WebApplication app)
    {
        var customers = app.MapGroup("/customers").WithTags("Customers");

        customers.MapGet("/", async (
            CustomerOverviewService overviewService,
            CancellationToken ct,
            bool simulateFailure = false,
            string scenario = "customers-list",
            int take = 20) =>
        {
            if (simulateFailure)
            {
                return Results.Problem(
                    title: "Customer list failed",
                    detail: "Synthetic customer list failure for load testing.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>
                    {
                        ["scenario"] = string.IsNullOrWhiteSpace(scenario) ? "customers-list" : scenario,
                    });
            }

                var items = await overviewService.GetCustomerOverviewAsync(
                    take <= 0 ? 20 : Math.Min(take, 50),
                    ct);
            return Results.Ok(items);
        })
        .WithName("GetCustomers")
        .WithSummary("List customers from SQLite");

        // GET /customers/lookup?email=... — support-tooling style lookup by email.
        // The email comes through as a query string parameter, captured into
        // every span's http.url/url.full attribute by default ASP.NET Core
        // instrumentation — no logging call anywhere put it there.
        customers.MapGet("/lookup", async (
            string email,
            CustomerRepository repository,
            CancellationToken ct) =>
        {
            var customer = await repository.GetByEmailAsync(email, ct);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        })
        .WithName("LookupCustomerByEmail")
        .WithSummary("Look up a customer by email — demonstrates query-string PII exposure");

        customers.MapPut("/{customerId}", async (
            string customerId,
            [FromBody] UpdateCustomerRequest request,
            CustomerRepository repository,
            CancellationToken ct,
            bool simulateConflict = false,
            string scenario = "customer-edit") =>
        {
            try
            {
                var updated = await repository.UpdateDisplayNameAsync(customerId, request.DisplayName, simulateConflict, ct);
                return updated is null
                    ? Results.NotFound(new { customerId, scenario = string.IsNullOrWhiteSpace(scenario) ? "customer-edit" : scenario })
                    : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { customerId, error = ex.Message, scenario = string.IsNullOrWhiteSpace(scenario) ? "customer-edit" : scenario });
            }
        })
        .WithName("UpdateCustomer")
        .WithSummary("Update a customer display name");
    }
}

public sealed record UpdateCustomerRequest(string DisplayName);