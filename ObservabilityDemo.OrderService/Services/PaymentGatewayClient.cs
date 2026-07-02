using System.Diagnostics;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService.Services;

public sealed class PaymentGatewayClient(ILogger<PaymentGatewayClient> logger)
{
    private static readonly ActivitySource ActivitySource =
        new(TelemetryConstants.ActivitySourceName);

    public async Task AuthorizeAsync(
        Order order,
        PriceQuote quote,
        RequestSimulationOptions simulation,
        CancellationToken ct = default)
    {
        await RunRiskChecksAsync(order, quote, ct);
        await CallGatewayAsync(order, quote, ct);

        if (simulation.ForcePaymentFailure || simulation.SimulateFailure)
        {
            throw new InvalidOperationException(
                $"Payment authorization failed for order {order.OrderId} ({quote.Total:0.00} SEK)");
        }

        await WriteAuditAsync(order, quote, ct);

        logger.LogDebug(
            "Authorized payment for order {OrderId} amount {OrderTotal}",
            order.OrderId,
            quote.Total);
    }

    private static async Task RunRiskChecksAsync(Order order, PriceQuote quote, CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Payment spans intentionally disabled for the M2C5 demo flow.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanPaymentRiskCheck,
        //     ActivityKind.Internal);
        // activity?.SetTag("order.id", order.OrderId);
        // activity?.SetTag("payment.amount", (double)quote.Total);

        await Task.Delay(Random.Shared.Next(8, 20), ct);
    }

    private static async Task CallGatewayAsync(Order order, PriceQuote quote, CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Payment spans intentionally disabled for the M2C5 demo flow.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanPaymentGatewayCall,
        //     ActivityKind.Client);
        // activity?.SetTag("order.id", order.OrderId);
        // activity?.SetTag("payment.amount", (double)quote.Total);

        await Task.Delay(Random.Shared.Next(20, 45), ct);
    }

    private static async Task WriteAuditAsync(Order order, PriceQuote quote, CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Payment spans intentionally disabled for the M2C5 demo flow.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanPaymentAuditWrite,
        //     ActivityKind.Internal);
        // activity?.SetTag("order.id", order.OrderId);
        // activity?.SetTag("payment.amount", (double)quote.Total);

        await Task.Delay(Random.Shared.Next(6, 16), ct);
    }
}