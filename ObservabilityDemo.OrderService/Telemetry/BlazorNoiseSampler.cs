using OpenTelemetry.Trace;

namespace ObservabilityDemo.OrderService.Telemetry;

// Drops Blazor Server's ComponentHub spans (ConnectCircuit, OnRenderCompleted, etc.)
// before they reach the exporter. The HTTP request filter in TelemetryFilters only
// covers HTTP-level spans; ComponentHub activities come from a separate ActivitySource
// and must be suppressed at the sampler level.
internal sealed class BlazorNoiseSampler : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.Name.StartsWith(
                "Microsoft.AspNetCore.Components", StringComparison.Ordinal))
            return new SamplingResult(SamplingDecision.Drop);

        return new SamplingResult(SamplingDecision.RecordAndSample);
    }
}
