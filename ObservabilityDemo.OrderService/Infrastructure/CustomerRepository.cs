using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService.Infrastructure;

public sealed class CustomerRepository(AppDbContext db, ILogger<CustomerRepository> logger)
{
    private static readonly ActivitySource ActivitySource =
        new(TelemetryConstants.ActivitySourceName);

    public async Task<IReadOnlyList<CustomerProfile>> GetAllAsync(int take, CancellationToken ct = default)
    {
        return await db.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Id)
            .Take(take)
            .Select(customer => new CustomerProfile(
                customer.Id,
                customer.DisplayName,
                customer.Email,
                customer.Region,
                customer.LoyaltyTier,
                customer.UpdatedUtc))
            .ToListAsync(ct);
    }

    public async Task<CustomerProfile?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Email == email, ct);

        if (customer is null)
        {
            return null;
        }

        return new CustomerProfile(
            customer.Id,
            customer.DisplayName,
            customer.Email,
            customer.Region,
            customer.LoyaltyTier,
            customer.UpdatedUtc);
    }

    public async Task<CustomerProfile?> GetByIdAsync(string customerId, CancellationToken ct = default)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == customerId, ct);

        if (customer is null)
        {
            return null;
        }

        logger.LogDebug("Loaded customer profile {CustomerId}", customerId);

        // [DEMO-BLOCK: tracing-extras] Customer mapping span left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var mapActivity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanCustomerResultMap,
        //     ActivityKind.Internal);
        // mapActivity?.SetTag("customer.id", customerId);

        return new CustomerProfile(
            customer.Id,
            customer.DisplayName,
            customer.Email,
            customer.Region,
            customer.LoyaltyTier,
            customer.UpdatedUtc);
    }

    public async Task<CustomerProfile?> UpdateDisplayNameAsync(
        string customerId,
        string displayName,
        bool simulateConflict,
        CancellationToken ct = default)
    {
        if (simulateConflict)
        {
            throw new InvalidOperationException($"Concurrent customer update detected for {customerId}");
        }

        var customer = await db.Customers.FirstOrDefaultAsync(item => item.Id == customerId, ct);
        if (customer is null)
        {
            return null;
        }

        customer.DisplayName = displayName;
        customer.UpdatedUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogDebug("Updated customer profile {CustomerId}", customerId);

        return new CustomerProfile(
            customer.Id,
            customer.DisplayName,
            customer.Email,
            customer.Region,
            customer.LoyaltyTier,
            customer.UpdatedUtc);
    }
}