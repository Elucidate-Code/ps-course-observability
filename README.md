# Observability in .NET 10 — Demo App

This is the companion app for the *Observability in .NET 10* Pluralsight course. It's a single
ASP.NET Core service — an order-processing workbench — that you'll instrument with
OpenTelemetry as you work through the modules. This README explains how to run it and what
each piece does; it isn't a substitute for the course itself.

## What's here

| Path | What it is |
|---|---|
| `ObservabilityDemo.OrderService/` | The ASP.NET Core app (minimal API + a small static HTML workbench UI) |
| `ObservabilityDemo.slnx` | Solution file |
| `Start-AspireDashboard.ps1` | Launches the .NET Aspire Dashboard (a local OTLP viewer) in Docker |
| `observability-stack/` | A docker-compose stack (otel-collector, Prometheus, Tempo, Loki, Grafana) used in the later, "production backend" module |
| `Set-DemoState.ps1` | Moves the app's instrumentation between the course's module checkpoints — see below |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the Aspire Dashboard and, later, the Grafana stack)
- PowerShell 7+ (`pwsh`) — optional, only needed to run the two `.ps1` convenience scripts. Without it:
  - `Start-AspireDashboard.ps1` → run the `docker run` command from inside that file directly.
  - `Set-DemoState.ps1` → edit the `[DEMO-BLOCK]` regions by hand per the table below.

## Quickstart

1. Start the Aspire Dashboard (a local, zero-config OTLP receiver — no login, no retention):

   ```powershell
   ./Start-AspireDashboard.ps1
   ```

   - Dashboard UI: http://localhost:18888
   - OTLP ingest: http://localhost:18889 (the app is already pointed here by default)

2. Run the app:

   ```powershell
   dotnet run --project ObservabilityDemo.OrderService/ObservabilityDemo.OrderService.csproj --launch-profile http
   ```

   The workbench UI is at http://localhost:5081.

3. Click **Place Order** in the workbench, then switch to the Aspire Dashboard's Traces tab and open the newest trace.

That's the whole loop for Modules 1-4: generate traffic in the workbench, look at the result in Aspire.

## The workbench UI

The app seeds itself with in-memory SQLite data (customers, catalog items) on startup — no setup needed. The workbench (`/`) gives you buttons instead of a terminal full of `curl`:

**Toggles** (flip a setting, then place orders normally or with High Load):
- **Slow** / **Fail** — force the inventory dependency to be slow or to fail
- **High Load** — starts a background load generator sending a realistic mix of traffic (mostly `POST /orders`, plus customer/catalog/recent-orders reads) at your app's own HTTP endpoints
- **Degrading** — ramps the slow-order percentage up over ~30 seconds, useful once you're looking at percentiles rather than raw request

**One-shot actions** (each fires a single request with its own forced scenario, independent of the toggles above):
- **Place Order** — a normal `POST /orders`
- **Slow Order** / **Fail Order** — force that one request to be slow / to fail
- **DB Fail Order** — forces the final persistence step to fail
- **Inner Exception** — fails inventory with an exception that wraps an inner exception, useful once you're looking at exception chains
- **Lookup by Email** — a `GET` request with the email in the query string, useful when the course covers PII in URLs/query strings as well as bodies

Some endpoints are deliberately excluded from tracing so the trace list doesn't fill up with polling noise: `GET /api/feed` and `GET /api/settings` (the workbench's own polling calls), Blazor/static-asset requests. See `Telemetry/TelemetryFilters.cs`.

## Endpoints

| Method | URL | Purpose |
|---|---|---|
| GET | `/` | Workbench UI |
| POST | `/orders` | Process one order |
| GET | `/orders/recent` | Recently saved orders |
| GET | `/customers` | List customers |
| GET | `/customers/lookup?email=...` | Look up a customer by email |
| PUT | `/customers/{customerId}` | Update a customer |
| GET | `/catalog/products` | List catalog products |
| GET | `/demo/settings` | Current demo toggle state (plain JSON, handy to check from a browser) |

## How the instrumentation reveal works

The source has small, named `[DEMO-BLOCK BEGIN: x] ... [DEMO-BLOCK END: x]` regions in
`TelemetryConfiguration.cs` and `OrderProcessingService.cs`. Each region belongs to a group, and
each course module cumulatively turns one more group on:

| State | What's active | Corresponds to |
|---|---|---|
| **M1** (current state) | Framework auto-instrumentation only: ASP.NET Core, EF Core, HttpClient | Start of Module 1 |
| **M2** | + custom tracing (`ActivitySource`, the `order.process` span and its children) | After Module 2 |
| **M3** | + custom metrics (`Meter`, counters/histogram) | After Module 3 |
| **M4** | + structured logging (`BeginScope`, business log fields) | After Module 4 |
| **M5** | + PII redaction processor on traces and logs | After Module 5 |

Move between states with:

```powershell
./Set-DemoState.ps1 -State M2      # jump straight to the Module 2 checkpoint
./Set-DemoState.ps1 -Show          # see which blocks are currently on/off, without changing anything
```

It's safe to run repeatedly or jump around in any order — each run regenerates the toggled
regions from a canonical snapshot (`tools/demo-block-manifest.json`) rather than editing
in place. Run `dotnet build` after switching states to confirm. A few blocks are deliberately
left out of this automation (alternate/illustrative code kept for reference, or spans that are
optional bonus content beyond what the course covers) — see the comment header in
`Set-DemoState.ps1` for the exact list.

At the **M1** baseline, you'll see request spans, EF Core SQLite command spans, and framework
metrics/logs in Aspire — but no `order.process` span, no custom `orders.*` metrics, and no
business fields in the logs. That's the starting point the course builds from.

## What you'll see at M2+ (tracing on)

```text
POST /orders
  order.process
    inventory.check
    orders.persist
```

plus the framework's own EF Core SQLite command spans nested wherever the app talks to the
database. `inventory.check` and `orders.persist` carry `AddException`/`SetStatus` on the
failure path, so a `Fail Order` or `DB Fail Order` click shows up as an error span with a
`traceId` in the response body.

## Switching where telemetry goes

The app picks its OTLP destination from `Otlp:Backend` in `appsettings.json` (default: `Aspire`).
Three backends are wired up:

| Backend | Use for | Config |
|---|---|---|
| `Aspire` | Modules 1-4 — quick local viewing, no setup | `Otlp:Aspire:Endpoint` (default `http://localhost:18889`, gRPC) |
| `GrafanaStack` | Module 5 — a retained, queryable backend | `Otlp:GrafanaStack:Endpoint` (default `http://localhost:4318`, HTTP) — see below |
| `AzureMonitor` | Module 5 — a managed production backend | `Otlp:AzureMonitor:TracesEndpoint` / `MetricsEndpoint` / `LogsEndpoint` / `Headers` |

Switch at runtime without editing the file:

```powershell
$env:Otlp__Backend = "GrafanaStack"
dotnet run --project ObservabilityDemo.OrderService/ObservabilityDemo.OrderService.csproj --launch-profile http
```

For `AzureMonitor`, the endpoint URLs and auth header are per-signal (traces/metrics/logs each
have their own OTLP ingestion URL) and are credentials, so they don't belong in `appsettings.json`.
Put them in a gitignored `appsettings.Development.local.json` instead:

```json
{
  "Otlp": {
    "AzureMonitor": {
      "TracesEndpoint": "https://<your-dcr-endpoint>/.../v1/traces",
      "MetricsEndpoint": "https://<your-dcr-endpoint>/.../v1/metrics",
      "LogsEndpoint": "https://<your-dcr-endpoint>/.../v1/logs",
      "Headers": "Authorization=Bearer <token>"
    }
  }
}
```

## The Module 5 backend stack (`observability-stack/`)

This is a docker-compose stack standing in for a production observability backend: an
OpenTelemetry Collector receiving OTLP, fanning out to Prometheus (metrics), Tempo (traces), and
Loki (logs), all visualized in Grafana with pre-provisioned dashboards.

```powershell
cd observability-stack
docker compose up -d
```

| Container | Role | Port |
|---|---|---|
| `otel-collector` | Receives OTLP from the app, exports to the others | 4317 (gRPC), 4318 (HTTP) |
| `prometheus` | Metrics storage/query, with exemplar support | 9090 |
| `tempo` | Trace storage | 3200 |
| `loki` | Log storage | 3100 |
| `grafana` | Dashboards over all three, pre-provisioned from `grafana/dashboards/` and `grafana/provisioning/` | 3000 |

Set the app to `Otlp:Backend = GrafanaStack` (see above) before generating traffic, then open
Grafana at http://localhost:3000.

## Troubleshooting

- **Dashboard looks empty** — check High Load / recent traffic first; both Aspire and the
  Grafana stack only show what's actually been sent since they started.
- **Nothing arrives after switching backends** — confirm the app printed the endpoint it's
  using at startup (`[OTel] backend=... endpoint=...` on stderr) and that it matches the
  backend you meant to target.
- **`Set-DemoState.ps1` says a block wasn't found** — you're probably running it against a
  modified copy of `TelemetryConfiguration.cs` / `OrderProcessingService.cs` where a
  `[DEMO-BLOCK]` marker was edited or removed; `git diff` those two files against a fresh
  clone to compare.
