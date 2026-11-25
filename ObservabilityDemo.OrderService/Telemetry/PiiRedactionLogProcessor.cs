using OpenTelemetry;
using OpenTelemetry.Logs;

namespace ObservabilityDemo.OrderService.Telemetry;

/// <summary>
/// PII masking (Module 4), logging pipeline.
///
/// Logging-pipeline counterpart to PiiRedactionProcessor. Same rules (the
/// _piiKeys/_valuePatterns/RedactPairsList/RedactFreeText defined on
/// PiiRedactionProcessor), a different BaseProcessor&lt;T&gt; — registered separately,
/// in WithLogging(...) instead of WithTracing(...), because the two pipelines are
/// unrelated SDK objects with no shared base class.
///
/// LogRecord has no Events and no Status. Its two surfaces are:
///
///   1. Attributes — the equivalent of Activity.TagObjects. The difference is the
///      API shape: there's no SetTag, so the whole list is rebuilt and reassigned.
///
///   2. FormattedMessage — the equivalent of Activity.StatusDescription: free text,
///      no key of its own, populated by whatever the message template rendered.
///      _logger.LogInformation("User created {@User}", user) puts every destructured
///      property into Attributes (tier 1/tier 2 both apply there); the rendered
///      sentence itself lands in FormattedMessage (tier 2 only — no key to match).
/// </summary>
public sealed class PiiRedactionLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        if (record.Attributes is { } attributes)
            record.Attributes = PiiRedactionProcessor.RedactPairsList(attributes);

        if (record.FormattedMessage is string message)
            record.FormattedMessage = PiiRedactionProcessor.RedactFreeText(message);
    }
}
