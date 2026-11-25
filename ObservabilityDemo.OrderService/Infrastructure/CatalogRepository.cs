using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService.Infrastructure;

public sealed class CatalogRepository(AppDbContext db, ILogger<CatalogRepository> logger)
{
    private static readonly ActivitySource ActivitySource =
        new(TelemetryConstants.ActivitySourceName);

    public async Task<IReadOnlyList<CatalogItem>> GetProductsAsync(int take, CancellationToken ct = default)
    {
        return await db.Products
            .AsNoTracking()
            .OrderBy(product => product.Sku)
            .Take(take)
            .Select(product => new CatalogItem(
                product.Sku,
                product.Name,
                product.Category,
                product.UnitPrice,
                product.AvailableStock))
            .ToListAsync(ct);
    }

    public async Task<CatalogItem?> GetBySkuAsync(string sku, CancellationToken ct = default)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sku == sku, ct);

        if (product is null)
        {
            return null;
        }

        logger.LogDebug("Loaded catalog item {Sku}", sku);

        // [DEMO-BLOCK: tracing-extras] Catalog mapping span left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var mapActivity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanCatalogResultMap,
        //     ActivityKind.Internal);
        // mapActivity?.SetTag("product.sku", sku);

        return new CatalogItem(
            product.Sku,
            product.Name,
            product.Category,
            product.UnitPrice,
            product.AvailableStock);
    }
}