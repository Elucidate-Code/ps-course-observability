namespace ObservabilityDemo.OrderService.Telemetry;

internal static class TelemetryFilters
{
    // Drops Blazor internals, static assets, and high-frequency polling endpoints
    // so the Aspire Dashboard trace list shows only business requests.
    internal static bool ShouldRecord(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";

        if (path.StartsWith("/_blazor",    StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/_vs",        StringComparison.OrdinalIgnoreCase)) return false;

        var ext = Path.GetExtension(path);
        if (ext is ".css" or ".js" or ".ico" or ".png"
                or ".woff" or ".woff2" or ".map" or ".svg") return false;

        if (path.Equals("/api/feed",     StringComparison.OrdinalIgnoreCase)
            && ctx.Request.Method == "GET") return false;
        if (path.Equals("/api/settings", StringComparison.OrdinalIgnoreCase)
            && ctx.Request.Method == "GET") return false;

        return true;
    }
}
