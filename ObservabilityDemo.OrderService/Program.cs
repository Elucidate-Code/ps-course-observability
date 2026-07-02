using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ObservabilityDemo.OrderService;
using ObservabilityDemo.OrderService.Api;
using ObservabilityDemo.OrderService.Demo;
using ObservabilityDemo.OrderService.Infrastructure;
using ObservabilityDemo.OrderService.Services;

var builder = WebApplication.CreateBuilder(args);

// Per-developer OTLP backend secrets (e.g. Azure Monitor bearer tokens) live outside
// source control; see appsettings.{Environment}.local.json (gitignored).
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

// ─── Demo settings ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<DemoSettings>();

// ─── Database ─────────────────────────────────────────────────────────────────
// "Mode=Memory;Cache=Shared" keeps the in-memory database alive across connections,
// but only as long as at least one connection to it stays open — hence the singleton
// keep-alive connection below. Each scoped AppDbContext gets its OWN connection via
// the connection string, rather than sharing that singleton's connection instance:
// a single SqliteConnection isn't safe for concurrent use by multiple request scopes,
// and under load two requests opening transactions on it at once throws "cannot start
// a transaction within a transaction".
const string SqliteConnectionString = "Data Source=observability-demo;Mode=Memory;Cache=Shared";

builder.Services.AddSingleton(_ =>
{
    var keepAliveConnection = new SqliteConnection(SqliteConnectionString);
    keepAliveConnection.Open();
    return keepAliveConnection;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(SqliteConnectionString));

// ─── Application services ─────────────────────────────────────────────────────
builder.Services.AddHttpClient("load-generator", client =>
    client.BaseAddress = new Uri(
        builder.Configuration["LoadGenerator:BaseUrl"] ?? "http://localhost:5081"));
builder.Services.AddHttpClient<InventoryClient>();
builder.Services.AddScoped<CatalogRepository>();
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddScoped<CustomerOverviewService>();
builder.Services.AddScoped<OrderRepository>();
builder.Services.AddScoped<FulfillmentService>();
builder.Services.AddScoped<PaymentGatewayClient>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddSingleton<ActivityFeed>();
builder.Services.AddScoped<OrderProcessingService>();
builder.Services.AddScoped<DemoScenarioRunner>();
builder.Services.AddHostedService<LoadGeneratorService>();

// ─── OpenTelemetry pipeline ───────────────────────────────────────────────────
builder.Services.AddTelemetry(builder.Configuration);

builder.Logging.AddOpenTelemetry(logging =>
{
    // Raw template: "Order {OrderId} created"
    // Filled in:    "Order 4821 created"
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
});

var app = builder.Build();

// Force the keep-alive connection open now, before any AppDbContext connects —
// it must be the first connection to the named in-memory database so the database
// exists for every connection opened afterward.
app.Services.GetRequiredService<SqliteConnection>();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DemoDatabaseInitializer.InitializeAsync(db);
}

// ─── Static files (wwwroot/index.html served at /) ───────────────────────────
// Plain static HTML + JS frontend — no Blazor, no SignalR, no ComponentHub noise.
app.UseDefaultFiles();
app.UseStaticFiles();

// ─── API Endpoints ────────────────────────────────────────────────────────────
app.MapCatalogEndpoints();
app.MapCustomerEndpoints();
app.MapOrderEndpoints();
app.MapDemoApiEndpoints();

await app.RunAsync();


