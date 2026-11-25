using System.Diagnostics;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Infrastructure;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService.Services;

public sealed class CustomerOverviewService(CustomerRepository repository)
{
    private static readonly ActivitySource ActivitySource =
        new(TelemetryConstants.ActivitySourceName);

    public async Task<IReadOnlyList<CustomerProfile>> GetCustomerOverviewAsync(
        int take,
        CancellationToken ct = default)
    {
        // [DEMO-BLOCK: tracing-extras] Customer-overview spans left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var listActivity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanCustomersList,
        //     ActivityKind.Internal);
        // listActivity?.SetTag("customers.take", take);

        var customers = await repository.GetAllAsync(take, ct);

        // using (var enrichActivity = ActivitySource.StartActivity(
        //            TelemetryConstants.SpanCustomersCrmEnrich,
        //            ActivityKind.Client))
        // {
        //     enrichActivity?.SetTag("customers.count", customers.Count);
        //     enrichActivity?.SetTag("crm.dataset", "customer-overview");
            await Task.Delay(Random.Shared.Next(220, 360), ct);
        // }

        // using (var mapActivity = ActivitySource.StartActivity(
        //            TelemetryConstants.SpanCustomersResponseMap,
        //            ActivityKind.Internal))
        // {
        //     mapActivity?.SetTag("customers.count", customers.Count);
            await Task.Delay(Random.Shared.Next(12, 28), ct);
        // }

        // listActivity?.SetTag("customers.count", customers.Count);
        // listActivity?.SetTag("customer-list.hot_path", "customers.crm.enrich");

        return customers;
    }
}