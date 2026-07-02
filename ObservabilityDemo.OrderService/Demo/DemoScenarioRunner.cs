using System.Diagnostics;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Services;

namespace ObservabilityDemo.OrderService.Demo;

/// <summary>
/// Provides named demo scenarios used by the Blazor UI and the /orders/* endpoints.
/// Wraps OrderProcessingService and records every call into the ActivityFeed so the
/// Blazor workbench can display a live feed without touching OrderProcessingService.
/// </summary>
public sealed class DemoScenarioRunner(
    OrderProcessingService service,
    ActivityFeed feed,
    ILogger<DemoScenarioRunner> logger)
{
    public Task<string> RunOrderAsync(
        Order order,
        RequestSimulationOptions? simulation = null,
        CancellationToken ct = default)
        => RunWithFeedAsync(order, simulation ?? new RequestSimulationOptions(), ct);

    private async Task<string> RunWithFeedAsync(
        Order order,
        RequestSimulationOptions simulation,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await service.ProcessOrderAsync(order, simulation, ct);
            feed.Add(new ActivityEntry(
                DateTimeOffset.Now, order.OrderId, order.CustomerId,
                simulation.Scenario, "success", sw.Elapsed.TotalMilliseconds, null));
            return result;
        }
        catch (Exception ex)
        {
            feed.Add(new ActivityEntry(
                DateTimeOffset.Now, order.OrderId, order.CustomerId,
                simulation.Scenario, "failure", sw.Elapsed.TotalMilliseconds, ex.Message));
            throw;
        }
    }
}

