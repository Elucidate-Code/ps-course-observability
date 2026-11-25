using System.Diagnostics;
using System.Diagnostics.Metrics;
using ObservabilityDemo.OrderService.Demo;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Infrastructure;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService.Services;

/// <summary>
/// Core order processing service.
///
/// The instrumentation in this file and in TelemetryConfiguration.cs is grouped into named
/// [DEMO-BLOCK] regions. Run Set-DemoState.ps1 from the repository root to move between the
/// course checkpoints instead of editing the regions by hand:
///
///   M1  Framework auto-instrumentation only - the starting point of the course
///   M2  + custom tracing: the ActivitySource and the order.process span tree (Module 2)
///   M3  + custom metrics: the Meter, its four instruments and their call sites (Module 3)
///   M4  + structured logging and PII redaction on traces and logs (Module 4)
///   M5  Same code as M4 - Module 5 changes where telemetry is sent, not the instrumentation
///
/// For a full investigation, use M5, turn on the slow and failure scenarios in the
/// workbench, and follow metrics to a trace and then to its logs.
/// </summary>
public sealed class OrderProcessingService(
    CustomerRepository customers,
    CatalogRepository catalog,
    PricingService pricing,
    InventoryClient inventory,
    PaymentGatewayClient payments,
    FulfillmentService fulfillment,
    OrderRepository repository,
    DemoSettings settings,
    ILogger<OrderProcessingService> logger)
{
    private readonly DemoSettings _settings = settings;

    // =========================================================================
    // [MODULE 2 - TRACING] The ActivitySource (on from state M2).
    // =========================================================================
    // [DEMO-BLOCK BEGIN: tracing-activitysource]
    // private static readonly ActivitySource _activitySource =
        // new(TelemetryConstants.ActivitySourceName);
    // [DEMO-BLOCK END: tracing-activitysource]

    // =========================================================================
    // [MODULE 3 - METRICS] The Meter and its instruments (on from state M3).
    // =========================================================================
    // [DEMO-BLOCK BEGIN: metrics-meter-and-instruments]
    // private static readonly Meter _meter =
        // new(TelemetryConstants.MeterName);

    // private static readonly Counter<long> _ordersTotal =
        // _meter.CreateCounter<long>(
            // TelemetryConstants.MetricOrdersTotal,
            // description: "Total number of orders processed");

    // private static readonly Histogram<double> _orderDuration =
        // _meter.CreateHistogram<double>(
            // TelemetryConstants.MetricOrderDurationMs,
            // unit: "ms",
            // description: "Order processing duration in milliseconds",
            // Bucket boundaries (ms): 0 · 25 · 50 · 100 · 250 · 500 · 1000 · 2500 · 5000
            // advice: new InstrumentAdvice<double>
            // {
                // HistogramBucketBoundaries = [0, 25, 50, 100, 250, 500, 1000, 2500, 5000]
            // });

    // private static readonly UpDownCounter<long> _activeOrders =
        // _meter.CreateUpDownCounter<long>(
            // TelemetryConstants.MetricActiveOrders,
            // description: "Orders currently being processed");

    // private static readonly Counter<long> _dependencyFailures =
        // _meter.CreateCounter<long>(
            // TelemetryConstants.MetricDependencyFailures,
            // description: "Dependency call failures");
    // [DEMO-BLOCK END: metrics-meter-and-instruments]

    public async Task<string> ProcessOrderAsync(
        Order order,
        RequestSimulationOptions? simulation = null,
        CancellationToken ct = default)
    {
        simulation ??= new RequestSimulationOptions();

        // [DEMO-BLOCK BEGIN: logging-scope]
         // using var logScope = logger.BeginScope(new Dictionary<string, object>
         // {
             // ["CustomerId"] = order.CustomerId,
             // ["OrderId"] = order.OrderId,
             // ["Scenario"] = simulation.Scenario,
         // });
        // [DEMO-BLOCK END: logging-scope]

        // [DEMO-BLOCK BEGIN: metrics-active-increment]
        // _activeOrders.Add(1);
        // var stopwatch = Stopwatch.StartNew();
        // [DEMO-BLOCK END: metrics-active-increment]

        // [DEMO-BLOCK BEGIN: tracing-root-span]
        // using var orderActivity = _activitySource.StartActivity(
            // TelemetryConstants.SpanProcessOrder,
            // ActivityKind.Internal);
        // orderActivity?.SetTag("order.id", order.OrderId);
        // orderActivity?.SetTag("order.customer", order.CustomerId);
        // orderActivity?.SetTag("order.sku", order.ProductSku);
        // orderActivity?.SetTag("order.quantity", order.Quantity);
        // orderActivity?.SetTag("order.scenario", simulation.Scenario);
        // [DEMO-BLOCK END: tracing-root-span]

        try
        {
            var customer = await LoadCustomerAsync(order, ct);
            var product = await LoadCatalogItemAsync(order, ct);
            var quote = await QuoteOrderAsync(customer, product, order, ct);
            var plan = await PlanFulfillmentAsync(customer, product, order, ct);

            // [DEMO-BLOCK BEGIN: tracing-root-tags-late]
            // orderActivity?.SetTag("customer.region", customer.Region);
            // orderActivity?.SetTag("customer.tier", customer.LoyaltyTier);
            // PII - the redaction processor (on from state M4) masks this value before export.
            // orderActivity?.SetTag("customer.email", customer.Email);
            // orderActivity?.SetTag("product.category", product.Category);
            // orderActivity?.SetTag("order.total", (double)quote.Total);
            // orderActivity?.SetTag("fulfillment.warehouse", plan.Warehouse);
            // [DEMO-BLOCK END: tracing-root-tags-late]

            await CheckInventoryAsync(order, product, simulation, ct);
            await AuthorizePaymentAsync(order, quote, simulation, ct);
            await SaveOrderAsync(order, product, quote, simulation, ct);

            // [DEMO-BLOCK BEGIN: logging-info-structured]
            // logger.LogInformation(
                // "Order processed: total {OrderTotal} via {Warehouse}",
                // quote.Total,
                // plan.Warehouse);
            // [DEMO-BLOCK END: logging-info-structured]

            // [DEMO-BLOCK BEGIN: metrics-success-record]
            // stopwatch.Stop();
            // _ordersTotal.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
            // _orderDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            // [DEMO-BLOCK END: metrics-success-record]

            return order.OrderId;
        }
        catch (Exception ex)
        {
            // [DEMO-BLOCK BEGIN: tracing-root-catch]
            // orderActivity?.AddException(ex);
            // orderActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // orderActivity?.SetTag("error.type", ex.GetType().FullName);
            // [DEMO-BLOCK END: tracing-root-catch]

            // [DEMO-BLOCK BEGIN: metrics-failure-record]
            // stopwatch.Stop();
            // _ordersTotal.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
            // _orderDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            // [DEMO-BLOCK END: metrics-failure-record]
            // [DEMO-BLOCK BEGIN: logging-error-baseline]
            //logger.LogError($"Order failed: {ex.Message}");
            // [DEMO-BLOCK END: logging-error-baseline]
            // [DEMO-BLOCK BEGIN: logging-error-structured]
            // logger.LogError(ex,
                // "Order failed: {DependencyError}",
                 // ex.Message);
            // [DEMO-BLOCK END: logging-error-structured]




            // [DEMO-BLOCK BEGIN: logging-error-with-exception]
            // logger.LogError(
                // ex,
                // "Order failed: {DependencyError}",
                // ex.Message);
            // [DEMO-BLOCK END: logging-error-with-exception]

            _ = ex;
            throw;
        }
        finally
        {
            // [DEMO-BLOCK BEGIN: metrics-active-decrement]
            // _activeOrders.Add(-1);
            // [DEMO-BLOCK END: metrics-active-decrement]
        }
    }

    private async Task<CustomerProfile> LoadCustomerAsync(Order order, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is null)
        {
            throw new InvalidOperationException($"Customer {order.CustomerId} was not found");
        }
        return customer;
    }

    private async Task<CatalogItem> LoadCatalogItemAsync(Order order, CancellationToken ct)
    {
        var product = await catalog.GetBySkuAsync(order.ProductSku, ct);
        if (product is null)
        {
            throw new InvalidOperationException($"Product {order.ProductSku} was not found");
        }
        return product;
    }

    private async Task<PriceQuote> QuoteOrderAsync(
        CustomerProfile customer,
        CatalogItem product,
        Order order,
        CancellationToken ct)
    {
        return await pricing.QuoteAsync(customer, product, order.Quantity, ct);
    }

    private async Task<FulfillmentPlan> PlanFulfillmentAsync(
        CustomerProfile customer,
        CatalogItem product,
        Order order,
        CancellationToken ct)
    {
        return await fulfillment.PlanAsync(customer, product, order.Quantity, ct);
    }

    private async Task CheckInventoryAsync(
        Order order,
        CatalogItem product,
        RequestSimulationOptions simulation,
        CancellationToken ct)
    {
        // [DEMO-BLOCK BEGIN: tracing-inventory-span]
        // using var inventoryActivity = _activitySource.StartActivity(
            // TelemetryConstants.SpanInventoryCheck,
            // ActivityKind.Client);
        // inventoryActivity?.SetTag("inventory.sku", order.ProductSku);
        // inventoryActivity?.SetTag("inventory.qty", order.Quantity);
        // [DEMO-BLOCK END: tracing-inventory-span]

        try
        {
            await inventory.CheckStockAsync(
                order.ProductSku,
                order.Quantity,
                product.AvailableStock,
                simulation,
                ct);
        }
        catch (Exception ex)
        {
            // [DEMO-BLOCK BEGIN: tracing-inventory-catch]
            // inventoryActivity?.AddException(ex);
            // inventoryActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // inventoryActivity?.SetTag("error.type", ex.GetType().FullName);
            // [DEMO-BLOCK END: tracing-inventory-catch]

            // [DEMO-BLOCK BEGIN: metrics-dependency-failure-inventory]
            // _dependencyFailures.Add(
                // 1,
                // new KeyValuePair<string, object?>("dependency", "inventory"));
            // [DEMO-BLOCK END: metrics-dependency-failure-inventory]
            _ = ex;
            throw;
        }
    }

    private async Task AuthorizePaymentAsync(
        Order order,
        PriceQuote quote,
        RequestSimulationOptions simulation,
        CancellationToken ct)
    {
        try
        {
            await payments.AuthorizeAsync(order, quote, simulation, ct);
        }
        catch (Exception ex)
        {
            // [DEMO-BLOCK BEGIN: metrics-dependency-failure-payment]
            // _dependencyFailures.Add(
                // 1,
                // new KeyValuePair<string, object?>("dependency", "payment"));
            // [DEMO-BLOCK END: metrics-dependency-failure-payment]
            _ = ex;
            throw;
        }
    }
    
    private async Task SaveOrderAsync(
        Order order,
        CatalogItem product,
        PriceQuote quote,
        RequestSimulationOptions simulation,
        CancellationToken ct)
    {
        // [DEMO-BLOCK BEGIN: tracing-persist-span]
        // using var dbActivity = _activitySource.StartActivity(
            // TelemetryConstants.SpanOrdersPersist,
            // ActivityKind.Internal);
        // dbActivity?.SetTag("db.system", "sqlite");
        // dbActivity?.SetTag("db.operation", "INSERT");
        // dbActivity?.SetTag("order.id", order.OrderId);
        // [DEMO-BLOCK END: tracing-persist-span]

        try
        {
            await repository.SaveAsync(order, product, quote, simulation, ct);
        }
        catch (Exception ex)
        {
            // [DEMO-BLOCK BEGIN: tracing-persist-catch]
            // dbActivity?.AddException(ex);
            // dbActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // [DEMO-BLOCK END: tracing-persist-catch]
            // [DEMO-BLOCK BEGIN: metrics-dependency-failure-db]
            // _dependencyFailures.Add(
                // 1,
                // new KeyValuePair<string, object?>("dependency", "orders-db"));
            // [DEMO-BLOCK END: metrics-dependency-failure-db]
            _ = ex;
            throw;
        }
    }
}
