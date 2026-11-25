using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ObservabilityDemo.OrderService.Telemetry;

namespace ObservabilityDemo.OrderService;

public static class TelemetryConfiguration
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var resource = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: TelemetryConstants.ServiceName,
                serviceVersion: TelemetryConstants.ServiceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = configuration["ASPNETCORE_ENVIRONMENT"] ?? "production",
                ["host.name"] = Environment.MachineName,
            });

        services.AddOpenTelemetry()

            // -----------------------------------------------------------------
            // TRACING
            // -----------------------------------------------------------------
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation(opts =>
                {
                    opts.RecordException = true;
                    opts.Filter = TelemetryFilters.ShouldRecord;
                })
                // [DEMO-BLOCK BEGIN: tracing-dbquery-statement]
                // EntityFrameworkInstrumentationOptions has no built-in "capture the
                // statement text" switch — EnrichWithIDbCommand is the documented
                // extension point, so the developer writes the tag explicitly.
                .AddEntityFrameworkCoreInstrumentation(opts =>
                {
                    opts.EnrichWithIDbCommand = (activity, command) =>
                        activity.SetTag("db.statement.text", command.CommandText);
                })
                // [DEMO-BLOCK END: tracing-dbquery-statement]
                .AddHttpClientInstrumentation()
                // [DEMO-BLOCK BEGIN: tracing-add-source]
                 // .AddSource(TelemetryConstants.ActivitySourceName)
                // [DEMO-BLOCK END: tracing-add-source]
                // [DEMO-BLOCK BEGIN: tracing-pii-processor]
                // .AddProcessor(new PiiRedactionProcessor())
                // [DEMO-BLOCK END: tracing-pii-processor]
                .AddOtlpExporter(otlp => ConfigureOtlp(otlp, configuration, "/v1/traces")))

            // -----------------------------------------------------------------
            // METRICS
            // -----------------------------------------------------------------
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                // [DEMO-BLOCK BEGIN: metrics-add-meter]
                // .AddMeter(TelemetryConstants.MeterName)
                // [DEMO-BLOCK END: metrics-add-meter]
                // [DEMO-BLOCK BEGIN: metrics-exemplar-filter]
                 .SetExemplarFilter(ExemplarFilterType.TraceBased)
                // [DEMO-BLOCK END: metrics-exemplar-filter]
                .AddOtlpExporter((otlp, reader) =>
                {
                    ConfigureOtlp(otlp, configuration, "/v1/metrics");
                    // Reduce from the 60 s default so Aspire shows responsive metric graphs
                    // during a 6-minute demo.  5 s is fine for development; tune up for prod.
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5_000;
                }))

            // -----------------------------------------------------------------
            // LOGGING
            // -----------------------------------------------------------------
            .WithLogging(logging => logging
                .SetResourceBuilder(resource)
                // [DEMO-BLOCK BEGIN: logging-pii-processor]
                // Log-pipeline PII redaction (on from state M4).
                // .AddProcessor(new PiiRedactionLogProcessor())   // log-pipeline equivalent of PiiRedactionProcessor — covers log entries, not spans
                // [DEMO-BLOCK END: logging-pii-processor]
                .AddOtlpExporter(otlp => ConfigureOtlp(otlp, configuration, "/v1/logs")));

        // [DEMO-BLOCK BEGIN: tracing-exception-redaction]
        // Activity.AddException() tags can't be redacted by any BaseProcessor<Activity> —
        // verified by testing, not assumed. This registers the listener that redacts
        // exception.message/exception.stacktrace before that event is ever created.
        // See PiiRedactionProcessor.RegisterExceptionRedaction for why.
        // PiiRedactionProcessor.RegisterExceptionRedaction();
        // [DEMO-BLOCK END: tracing-exception-redaction]

        return services;
    }

    /// <summary>
    /// Picks the OTLP destination based on "Otlp:Backend":
    ///   "Aspire"        — local Aspire Dashboard (gRPC, default)
    ///   "GrafanaStack"  — local Grafana / Alloy collector (HttpProtobuf)
    ///   "AzureMonitor"  — Azure Monitor OTLP ingestion (HttpProtobuf, per-signal URLs)
    /// Set the flag in appsettings.json: { "Otlp": { "Backend": "Aspire" } }
    /// </summary>
    private static void ConfigureOtlp(
        OtlpExporterOptions otlp,
        IConfiguration configuration,
        string httpSignalPath)
    {
        /*
            This version more advanced for the appsettings switch - normally 
              "OpenTelemetry": {
                    "Endpoint": "https://your-collector.com",
                    "Protocol": "Grpc",
                    "Headers": "x-otlp-api-key=your-api-key"
               }
            Then :
              .AddOtlpExporter(options =>
              {
                options.Endpoint = new Uri(builder.Configuration["OpenTelemetry:Endpoint"]!);
                options.Protocol = Enum.Parse<OtlpExportProtocol>(
                    builder.Configuration["OpenTelemetry:Protocol"]!);
                options.Headers = builder.Configuration["OpenTelemetry:Headers"];
              })
        */
        var backend = configuration["Otlp:Backend"] ?? "Aspire";

        if (string.Equals(backend, "AzureMonitor", StringComparison.OrdinalIgnoreCase))
        {
            ConfigureAzureMonitorOtlp(otlp, configuration, httpSignalPath);
            return;
        }

        var sectionName = string.Equals(backend, "GrafanaStack", StringComparison.OrdinalIgnoreCase)
            ? "Otlp:GrafanaStack"
            : "Otlp:Aspire";

        var section = configuration.GetSection(sectionName);
        var baseEndpoint = section["Endpoint"] ?? "http://localhost:18889";

        // HttpProtobuf requires signal-specific paths; gRPC (Aspire) handles routing internally.
        var isHttp = string.Equals(section["Protocol"], "HttpProtobuf", StringComparison.OrdinalIgnoreCase);
        otlp.Endpoint = new Uri(isHttp
            ? baseEndpoint.TrimEnd('/') + httpSignalPath
            : baseEndpoint);

        if (Enum.TryParse<OtlpExportProtocol>(section["Protocol"], out var protocol))
            otlp.Protocol = protocol;

        if (!string.IsNullOrEmpty(section["Headers"]))
            otlp.Headers = section["Headers"];

        Console.Error.WriteLine($"[OTel] backend={backend} endpoint={otlp.Endpoint} protocol={otlp.Protocol}");
    }

    /// <summary>
    /// Azure Monitor uses per-signal OTLP ingestion URLs — traces, metrics, and logs each
    /// have a different endpoint. The section keys are TracesEndpoint, MetricsEndpoint,
    /// and LogsEndpoint. httpSignalPath ("/v1/traces" etc.) maps to the right key.
    /// </summary>
    private static void ConfigureAzureMonitorOtlp(
        OtlpExporterOptions otlp,
        IConfiguration configuration,
        string httpSignalPath)
    {
        var section = configuration.GetSection("Otlp:AzureMonitor");

        var endpointKey = httpSignalPath switch
        {
            "/v1/traces"  => "TracesEndpoint",
            "/v1/metrics" => "MetricsEndpoint",
            "/v1/logs"    => "LogsEndpoint",
            _             => throw new InvalidOperationException($"Unknown signal path: {httpSignalPath}")
        };

        var endpoint = section[endpointKey]
            ?? throw new InvalidOperationException(
                $"Otlp:AzureMonitor:{endpointKey} is not configured. " +
                "Add it to appsettings.Development.local.json (keep credentials out of source control).");

        otlp.Endpoint = new Uri(endpoint);
        otlp.Protocol = OtlpExportProtocol.HttpProtobuf;

        if (!string.IsNullOrEmpty(section["Headers"]))
            otlp.Headers = section["Headers"];

        Console.Error.WriteLine($"[OTel] backend=AzureMonitor signal={endpointKey} endpoint={otlp.Endpoint}");
    }
}
