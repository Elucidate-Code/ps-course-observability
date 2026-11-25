namespace ObservabilityDemo.OrderService.Telemetry;

/// <summary>
/// Single source of truth for all telemetry names used in this service.
/// These names must match what is registered in the OpenTelemetry pipeline (Program.cs).
/// </summary>
public static class TelemetryConstants
{
    public const string ServiceName    = "orderservice";
    public const string ServiceVersion = "1.0.0";

    // ActivitySource name — registered in OTel tracing pipeline
    public const string ActivitySourceName = "ObservabilityDemo.OrderService";

    // Meter name — registered in OTel metrics pipeline
    public const string MeterName = "ObservabilityDemo.OrderService";

    // ---- Span names (Module 2) ----
    public const string SpanProcessOrder    = "order.process";
    public const string SpanCustomersList   = "customers.list";
    public const string SpanCustomersCrmEnrich = "customers.crm.enrich";
    public const string SpanCustomersResponseMap = "customers.response.map";
    public const string SpanCustomerLoad    = "customer.load";
    public const string SpanCustomerResultMap = "customer.result.map";
    public const string SpanCatalogLookup   = "catalog.lookup";
    public const string SpanCatalogResultMap = "catalog.result.map";
    public const string SpanPricingQuote    = "pricing.quote";
    public const string SpanPricingDiscount = "pricing.discount";
    public const string SpanPricingShipping = "pricing.shipping";
    public const string SpanPricingTax = "pricing.tax";
    public const string SpanInventoryCheck  = "inventory.check";
    public const string SpanInventoryCacheLookup = "inventory.cache.lookup";
    public const string SpanInventoryPartnerCall = "inventory.partner.call";
    public const string SpanInventoryResponseValidate = "inventory.response.validate";
    public const string SpanPaymentAuth     = "payment.authorize";
    public const string SpanPaymentRiskCheck = "payment.risk.check";
    public const string SpanPaymentGatewayCall = "payment.gateway.call";
    public const string SpanPaymentAuditWrite = "payment.audit.write";
    public const string SpanFulfillmentPlan = "fulfillment.plan";
    public const string SpanFulfillmentCapacityCheck = "fulfillment.capacity.check";
    public const string SpanFulfillmentRouteQuote = "fulfillment.route.quote";
    public const string SpanFulfillmentWarehouseSelect = "fulfillment.warehouse.select";
    public const string SpanOrdersPersist   = "orders.persist";

    // ---- Metric names (Module 3) ----
    public const string MetricOrdersTotal        = "orders.total";
    public const string MetricOrderDurationMs    = "orders.duration_ms";
    public const string MetricActiveOrders       = "orders.active";
    public const string MetricDependencyFailures = "dependency.failures";
}
