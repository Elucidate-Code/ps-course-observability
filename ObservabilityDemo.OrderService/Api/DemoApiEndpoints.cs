using ObservabilityDemo.OrderService.Demo;

namespace ObservabilityDemo.OrderService.Api;

public static class DemoApiEndpoints
{
    public static void MapDemoApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Demo API");

        // GET /api/settings — current DemoSettings as JSON
        api.MapGet("/settings", (DemoSettings s) => Results.Ok(ToDto(s)));

        // PATCH /api/settings — partial update; only non-null fields are applied
        api.MapPatch("/settings", (SettingsPatch patch, DemoSettings s) =>
        {
            if (patch.SlowInventory.HasValue)   s.SlowInventory   = patch.SlowInventory.Value;
            if (patch.FailInventory.HasValue)   s.FailInventory   = patch.FailInventory.Value;
            if (patch.HighLoad.HasValue)        s.HighLoad        = patch.HighLoad.Value;
            if (patch.DegradingLoad.HasValue)   s.DegradingLoad   = patch.DegradingLoad.Value;
            return Results.Ok(ToDto(s));
        });

        // GET /api/feed — current activity feed (newest first)
        api.MapGet("/feed", (ActivityFeed feed) => Results.Ok(feed.GetAll()));

        // DELETE /api/feed — clear the feed
        api.MapDelete("/feed", (ActivityFeed feed) =>
        {
            feed.Clear();
            return Results.NoContent();
        });
    }

    private static object ToDto(DemoSettings s) => new
    {
        slowInventory  = s.SlowInventory,
        failInventory  = s.FailInventory,
        highLoad       = s.HighLoad,
        degradingLoad  = s.DegradingLoad
    };
}

/// <summary>Partial-update body for PATCH /api/settings.</summary>
public record SettingsPatch(
    bool? SlowInventory  = null,
    bool? FailInventory  = null,
    bool? HighLoad       = null,
    bool? DegradingLoad  = null);
