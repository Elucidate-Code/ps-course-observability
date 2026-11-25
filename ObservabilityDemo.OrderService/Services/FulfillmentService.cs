using System.Diagnostics;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService.Services;

public sealed class FulfillmentService
{
    private static readonly ActivitySource ActivitySource =
        new(TelemetryConstants.ActivitySourceName);

    public async Task<FulfillmentPlan> PlanAsync(
        CustomerProfile customer,
        CatalogItem product,
        int quantity,
        CancellationToken ct = default)
    {
        await CheckCapacityAsync(product, quantity, ct);
        await QuoteRouteAsync(customer, ct);

        var warehouse = await SelectWarehouseAsync(customer, ct);

        var estimatedDeliveryDays = product.Category == "apparel"
            ? 3
            : quantity > 3 ? 4 : 2;

        return new FulfillmentPlan(warehouse, estimatedDeliveryDays);
    }

    private static async Task CheckCapacityAsync(
        CatalogItem product,
        int quantity,
        CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Fulfillment spans left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanFulfillmentCapacityCheck,
        //     ActivityKind.Internal);
        // activity?.SetTag("product.category", product.Category);
        // activity?.SetTag("order.quantity", quantity);

        await Task.Delay(Random.Shared.Next(8, 20), ct);
    }

    private static async Task QuoteRouteAsync(CustomerProfile customer, CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Fulfillment spans left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanFulfillmentRouteQuote,
        //     ActivityKind.Client);
        // activity?.SetTag("customer.region", customer.Region);

        await Task.Delay(Random.Shared.Next(18, 42), ct);
    }

    private static async Task<string> SelectWarehouseAsync(CustomerProfile customer, CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Fulfillment spans left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanFulfillmentWarehouseSelect,
        //     ActivityKind.Internal);
        // activity?.SetTag("customer.region", customer.Region);

        await Task.Delay(Random.Shared.Next(6, 16), ct);

        return customer.Region switch
        {
            "SE" => "se-stockholm",
            "NO" => "no-oslo",
            _ => "eu-copenhagen",
        };
    }
}