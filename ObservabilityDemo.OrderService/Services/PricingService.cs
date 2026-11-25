using System.Diagnostics;
using ObservabilityDemo.OrderService.Domain;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService.Services;

public sealed class PricingService
{
    private static readonly ActivitySource ActivitySource =
        new(TelemetryConstants.ActivitySourceName);

    public async Task<PriceQuote> QuoteAsync(
        CustomerProfile customer,
        CatalogItem product,
        int quantity,
        CancellationToken ct = default)
    {
        var unitPrice = product.UnitPrice;
        var gross = unitPrice * quantity;
        var discount = await CalculateDiscountAsync(customer, gross, ct);
        var net = gross - discount;
        var shipping = await CalculateShippingAsync(customer, ct);
        var tax = await CalculateTaxAsync(net, ct);

        return new PriceQuote(
            UnitPrice: unitPrice,
            Discount: discount,
            Shipping: shipping,
            Tax: tax,
            Total: net + shipping + tax);
    }

    private static async Task<decimal> CalculateDiscountAsync(
        CustomerProfile customer,
        decimal gross,
        CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Pricing spans left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanPricingDiscount,
        //     ActivityKind.Internal);
        // activity?.SetTag("pricing.tier", customer.LoyaltyTier);

        await Task.Delay(Random.Shared.Next(6, 16), ct);

        var discountRate = customer.LoyaltyTier switch
        {
            "gold" => 0.12m,
            "silver" => 0.07m,
            _ => 0.00m,
        };

        var discount = Math.Round(gross * discountRate, 2, MidpointRounding.AwayFromZero);
        // activity?.SetTag("pricing.discount", (double)discount);
        return discount;
    }

    private static async Task<decimal> CalculateShippingAsync(
        CustomerProfile customer,
        CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Pricing spans left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanPricingShipping,
        //     ActivityKind.Internal);
        // activity?.SetTag("customer.region", customer.Region);

        await Task.Delay(Random.Shared.Next(8, 18), ct);
        return customer.Region == "SE" ? 4.90m : 9.90m;
    }

    private static async Task<decimal> CalculateTaxAsync(decimal net, CancellationToken ct)
    {
        // [DEMO-BLOCK: tracing-extras] Pricing spans left off to keep the Module 2 trace tree focused; uncomment to add them.
        // using var activity = ActivitySource.StartActivity(
        //     TelemetryConstants.SpanPricingTax,
        //     ActivityKind.Internal);
        // activity?.SetTag("pricing.taxable", (double)net);

        await Task.Delay(Random.Shared.Next(8, 18), ct);
        return Math.Round(net * 0.25m, 2, MidpointRounding.AwayFromZero);
    }
}