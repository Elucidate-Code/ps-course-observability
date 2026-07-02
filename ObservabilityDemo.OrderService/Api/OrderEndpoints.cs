using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Infrastructure;
using ObservabilityDemo.OrderService.Services;

namespace ObservabilityDemo.OrderService.Api;

/// <summary>
/// Exposes the order processing endpoints used during all demo clips.
///
/// Endpoints:
///   POST /orders             — Process a single order (used in all demos)
///   GET  /orders/recent      — Recent saved orders from SQLite
///   GET  /demo/settings      — Shows current DemoSettings (useful on-screen during demo)
/// </summary>
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        // POST /orders — normal path
        orders.MapPost("/", async (
            HttpContext httpContext,
            [FromBody] Order order,
            [AsParameters] OrderSimulationQuery simulation,
            Demo.DemoScenarioRunner runner,
            CancellationToken ct) =>
        {
            try
            {
                var orderId = await runner.RunOrderAsync(order, simulation.ToOptions(), ct);
                return Results.Ok(new { orderId, status = "accepted", scenario = simulation.Scenario });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Order failed (expected for demo)",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError,
                    instance: httpContext.Request.Path,
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier,
                    });
            }
        })
        .WithName("ProcessOrder")
        .WithSummary("Process an order through inventory, payment, and persistence");

        orders.MapGet("/recent", async (
            OrderRepository repository,
            CancellationToken ct,
            int take = 20,
            bool simulateFailure = false,
            string scenario = "orders-recent") =>
        {
            if (simulateFailure)
            {
                return Results.Problem(
                    title: "Recent orders query failed",
                    detail: "Synthetic load failure while reading recent orders.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>
                    {
                        ["scenario"] = string.IsNullOrWhiteSpace(scenario) ? "orders-recent" : scenario,
                    });
            }

            var recentOrders = await repository.GetRecentAsync(take <= 0 ? 20 : Math.Min(take, 50), ct);
            return Results.Ok(recentOrders);
        })
        .WithName("RecentOrders")
        .WithSummary("Read recent orders from SQLite");

        // GET /demo/settings — shows what is currently active
        app.MapGet("/demo/settings", (Demo.DemoSettings settings) => Results.Ok(settings))
           .WithTags("Demo")
           .WithSummary("Show current demo settings");
    }
}

public sealed record OrderSimulationQuery(
    bool IgnoreDemoSettings = false,
    bool ForceSlowInventory = false,
    bool ForceInventoryFailure = false,
    bool ForceInventoryTransportFailure = false,
    bool ForcePaymentFailure = false,
    bool ForceDbFailure = false,
    string Scenario = "normal",
    int SlowInventoryDelayMs = 0)   // 0 = default 2000 ms slow path
{
    public RequestSimulationOptions ToOptions()
        => new(
            IgnoreDemoSettings,
            ForceSlowInventory,
            ForceInventoryFailure,
            ForceInventoryTransportFailure,
            ForcePaymentFailure,
            ForceDbFailure,
            false,
            ForceInventoryFailure || ForcePaymentFailure,
            string.IsNullOrWhiteSpace(Scenario) ? "normal" : Scenario,
            SlowInventoryDelayMs);
}
