using System.Net.Http.Json;
using ObservabilityDemo.OrderService.Demo;
using ObservabilityDemo.OrderService.Domain;

namespace ObservabilityDemo.OrderService.Demo;

/// <summary>
/// Load generator
///
/// When DemoSettings.HighLoad = true, this background service generates a small,
/// mixed HTTP workload against the app itself so the traces look like real request
/// traffic rather than direct in-process method calls. Its own slow-call fraction
/// (18%) is capped at a 150 ms delay so its baseline noise stays well below
/// Degrading Load's ramp ceiling — see the slowDelayMsOverride below.
///
/// When DemoSettings.DegradingLoad = true, the same concurrent workers run with a
/// FIXED 25 % slow-order fraction, but the slow-inventory delay ramps from 100 ms
/// → 2000 ms in 100 ms steps every 2 seconds (~38 s total).  Because the delay
/// passes through the histogram bucket boundaries [100, 250, 500, 1000, 2500 ms],
/// the Aspire P95/P99 lines step visibly upward while P50 stays flat (75 % fast).
///
/// To activate: enable High Load or Degrading Load from the workbench.
/// </summary>
public sealed class LoadGeneratorService(
    IHttpClientFactory httpClientFactory,
    DemoSettings settings,
    ILogger<LoadGeneratorService> logger) : BackgroundService
{
    private static int _counter = 0;
    private static readonly string[] CustomerIds = ["cust-001", "cust-002", "cust-003", "cust-099"];
    private static readonly string[] Skus = ["SKU-A1", "SKU-B2", "SKU-C3", "SKU-D4"];

    // Number of persistent concurrent workers when any load mode is enabled.
    private const int ConcurrentWorkers = 5;

    // Discrete delay levels for Degrading Load — each chosen so that (delay + ~250 ms
    // order-processing overhead) lands in a different OTel histogram bucket.
    // Bucket boundaries: [100, 250, 500, 1000, 2500] ms
    //   100 ms  →  total ~350 ms  →  le= 500 bucket  →  P90 ≈  400 ms
    //   500 ms  →  total ~750 ms  →  le=1000 bucket  →  P90 ≈  800 ms
    //  1100 ms  →  total ~1350 ms →  le=2500 bucket  →  P90 ≈ 1900 ms
    // Hold each 40 s so Aspire's polling window captures a clean plateau.
    private static readonly int[] DegradeStepDelays = [100, 500, 1100];
    private int _degradeStepIdx = 0;

    // Current slow-inventory delay (ms) used by Degrading Load workers.
    // Written by the ramp task, read by workers — both via Interlocked.
    private int _degradeDelayMs = 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = httpClientFactory.CreateClient("load-generator");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!settings.HighLoad && !settings.DegradingLoad)
            {
                await Task.Delay(250, stoppingToken);
                continue;
            }

            // Snapshot which modes this iteration's workers are spawned for. The wait
            // below must restart whenever EITHER toggle changes — not just when both
            // go false — otherwise flipping Degrading on while High Load is already
            // running (or turning High Load off while Degrading stays on) never takes
            // effect: the worker set below is fixed for the lifetime of one iteration.
            var runningHighLoad = settings.HighLoad;
            var runningDegradingLoad = settings.DegradingLoad;

            logger.LogInformation(
                "Load generator starting — {Workers} concurrent workers (HighLoad={H} DegradingLoad={D})",
                ConcurrentWorkers, runningHighLoad, runningDegradingLoad);

            using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var tasks = new List<Task>();

            if (runningHighLoad)
            {
                // Keep High Load's own slow-call noise in a low histogram bucket (le=250,
                // P90 ≈ 200 ms) so it stays well below Degrading Load's ramp ceiling
                // (1100 ms → le=2500, P90 ≈ 1900 ms). Without this, High Load's slow calls
                // fell back to InventoryClient's 2000 ms default and already saturated
                // p95/p99, leaving Degrading Load no headroom to visibly step above it.
                tasks.AddRange(Enumerable.Range(0, ConcurrentWorkers)
                    .Select(_ => RunWorkerAsync(client, slowPctOverride: null, slowDelayMsOverride: () => 150, workerCts.Token)));
            }

            if (runningDegradingLoad)
            {
                _degradeStepIdx = 0;
                Interlocked.Exchange(ref _degradeDelayMs, DegradeStepDelays[0]); // jump straight to first bucket
                tasks.Add(RunDegradeRampAsync(workerCts.Token));
                tasks.AddRange(Enumerable.Range(0, ConcurrentWorkers)
                    .Select(_ => RunWorkerAsync(
                        client,
                        slowPctOverride: () => 25,          // fixed 25 % — enough for P95/P99, not P50
                        slowDelayMsOverride: () => _degradeDelayMs,
                        workerCts.Token)));
            }

            // Wait until either toggle changes from what this iteration's workers were
            // spawned for, then fall through to respawn (or stop, if both are now off).
            while (settings.HighLoad == runningHighLoad
                   && settings.DegradingLoad == runningDegradingLoad
                   && !stoppingToken.IsCancellationRequested)
                await Task.Delay(100, stoppingToken);

            await workerCts.CancelAsync();
            await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            logger.LogInformation("Load generator stopped");
        }
    }

    // Steps through DegradeStepDelays every 30 s, replacing _degradeDelayMs with the
    // next value.  Each delay lands in a different OTel histogram bucket so the Aspire
    // chart shows a clean plateau at each level rather than a blurred ramp.
    // P50 stays flat (75 % fast); P90/P99 step up through ~400 → ~800 → ~1900 ms.
    private async Task RunDegradeRampAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(40_000, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (ct.IsCancellationRequested) break;
            var nextIdx = Interlocked.Increment(ref _degradeStepIdx);
            if (nextIdx < DegradeStepDelays.Length)
                Interlocked.Exchange(ref _degradeDelayMs, DegradeStepDelays[nextIdx]);
            // Past the last step: delay stays at DegradeStepDelays[^1]
        }
    }

    private async Task RunWorkerAsync(
        HttpClient client,
        Func<int>? slowPctOverride,
        Func<int>? slowDelayMsOverride,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ExecuteRandomRequestAsync(client, slowPctOverride?.Invoke(), slowDelayMsOverride?.Invoke(), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Load generator worker error — continuing");
            }
        }
    }

    private async Task ExecuteRandomRequestAsync(
        HttpClient client,
        int? slowPctOverride,
        int? slowDelayMs,
        CancellationToken ct)
    {
        var shouldFail = Random.Shared.Next(100) < 5;
        var roll = Random.Shared.Next(100);
        HttpResponseMessage response;

        if (roll < 50)
        {
            response = await PostOrderAsync(client, shouldFail, slowPctOverride, slowDelayMs, ct);
        }
        else if (roll < 70)
        {
            response = await client.GetAsync($"/customers?simulateFailure={shouldFail.ToString().ToLowerInvariant()}&scenario=load-customers", ct);
        }
        else if (roll < 82)
        {
            var customerId = CustomerIds[Random.Shared.Next(CustomerIds.Length)];
            response = await client.PutAsJsonAsync(
                $"/customers/{customerId}?simulateConflict={shouldFail.ToString().ToLowerInvariant()}&scenario=load-customer-edit",
                new { displayName = $"{customerId}-edited-{Random.Shared.Next(1000, 9999)}" },
                ct);
        }
        else if (roll < 92)
        {
            response = await client.GetAsync($"/catalog/products?simulateFailure={shouldFail.ToString().ToLowerInvariant()}&scenario=load-catalog", ct);
        }
        else
        {
            response = await client.GetAsync($"/orders/recent?simulateFailure={shouldFail.ToString().ToLowerInvariant()}&scenario=load-orders-recent", ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Load request returned HTTP {StatusCode}", (int)response.StatusCode);
        }
    }

    private static Task<HttpResponseMessage> PostOrderAsync(
        HttpClient client,
        bool shouldFail,
        int? slowPctOverride,
        int? slowDelayMs,
        CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _counter);
        var customerId = CustomerIds[id % CustomerIds.Length];
        var scenario = shouldFail ? "load-order-fail" : "load-order";

        // slowPctOverride: explicit probability (e.g. 25 % for degrading load).
        // null: fall back to the original 18%-baseline logic.
        var forceSlow = slowPctOverride.HasValue
            ? Random.Shared.Next(100) < slowPctOverride.Value
            : Random.Shared.Next(100) < 18;

        var failureMode = shouldFail && Random.Shared.Next(2) == 0
            ? "&forceInventoryFailure=true"
            : shouldFail ? "&forcePaymentFailure=true" : string.Empty;

        var order = new Order(
            OrderId: $"load-{id:D6}",
            CustomerId: customerId,
            CustomerEmail: $"{customerId}@globomantics.com",
            ProductSku: Skus[id % Skus.Length],
            Quantity: Random.Shared.Next(1, 5));

        // When slowDelayMs is set, pass it so InventoryClient uses that value instead of
        // the fixed 2000 ms default — this is what lets the delay climb through buckets.
        var slowParam = forceSlow
            ? (slowDelayMs.HasValue && slowDelayMs.Value > 0
                ? $"&forceSlowInventory=true&slowInventoryDelayMs={slowDelayMs.Value}"
                : "&forceSlowInventory=true")
            : string.Empty;

        var url = $"/orders?ignoreDemoSettings=true&scenario={scenario}" +
                  slowParam +
                  failureMode;

        return client.PostAsJsonAsync(url, order, ct);
    }
}
