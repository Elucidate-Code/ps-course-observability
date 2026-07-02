using ObservabilityDemo.OrderService.Infrastructure;

namespace ObservabilityDemo.OrderService.Api;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var catalog = app.MapGroup("/catalog").WithTags("Catalog");

        catalog.MapGet("/products", async (
            CatalogRepository repository,
            CancellationToken ct,
            bool simulateFailure = false,
            string scenario = "catalog",
            int take = 20) =>
        {
            if (simulateFailure)
            {
                return Results.Problem(
                    title: "Catalog query failed",
                    detail: "Synthetic catalog failure for load testing.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>
                    {
                        ["scenario"] = string.IsNullOrWhiteSpace(scenario) ? "catalog" : scenario,
                    });
            }

            var items = await repository.GetProductsAsync(take <= 0 ? 20 : Math.Min(take, 50), ct);
            return Results.Ok(items);
        })
        .WithName("GetCatalogProducts")
        .WithSummary("List catalog products from SQLite");
    }
}