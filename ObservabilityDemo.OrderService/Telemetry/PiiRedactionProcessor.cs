using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace ObservabilityDemo.OrderService.Telemetry;

/// <summary>
/// Demo 4 / M4C4 — PII Masking
///
/// A BaseProcessor&lt;Activity&gt; runs in the OTel pipeline for every span that is exported.
/// Masking here is done ONCE in the pipeline rather than at every individual call site.
///
/// Two-tier approach:
///
///   1. Field-name matching (_piiKeys): the entire value is replaced when the tag key is
///      on the list. Use this for fields that exist solely to carry sensitive data — knowing
///      the key name already tells you what was there ("customer.email" → "[REDACTED:field]").
///
///   2. Value-pattern matching (_valuePatterns): only the matched portion of the value is
///      replaced, leaving the rest intact. Use this for values where some parts are diagnostic
///      and some parts are sensitive. A connection string failure message contains the server
///      name and database (useful for diagnosis) alongside a password (must not be logged) —
///      a surgical replace keeps the useful parts and strips only the secret.
///
/// Both lists must be adapted to the data your application actually handles.
/// The patterns below are illustrative — SSNs are only relevant if your domain holds them,
/// and the connection-string pattern covers the most common key=value format but not every
/// driver's serialisation. Audit your spans in a test environment before relying on this
/// in production.
///
/// HOW TO ACTIVATE:
///   In TelemetryConfiguration.cs, inside .WithTracing(...):
///       .AddProcessor(new PiiRedactionProcessor())
///   This line is present but commented out — uncomment for Demo 4.
///
/// Key teaching point: masking belongs in the pipeline, not in business code.
/// A new sensitive field only requires updating this one file.
///
/// FIVE THINGS THIS PROCESSOR DOES NOT COVER:
///
///   1. Event tags — ALL of them, not just Activity.AddException()'s. Verified by
///      testing: Activity.AddEvent always copies tags into its own private,
///      read-only TagsLinkedList on attach, no matter what type you pass in —
///      even a hand-built ActivityTagsCollection gets copied away. There is no
///      downstream API that can redact an event's tags, ever. ExceptionRecorder
///      (registered in RegisterExceptionRedaction) is the one BCL-provided rescue
///      for the one case it was built for — exceptions. Any other AddEvent call
///      with sensitive data has no centralized fix; redact before constructing
///      the event, at the call site, because nothing downstream can reach it.
///
///   2. Baggage and names. Activity.AddBaggage propagates to downstream services over
///      the wire (as a "baggage" HTTP header) the moment it's set — before OnEnd ever
///      runs. A redaction processor only scrubs what THIS span exports; it cannot reach
///      data that already left the process via baggage propagation. Activity.DisplayName
///      (and LogRecord's category/event name) are two more fields OnEnd never inspects —
///      they're enumerated surfaces this processor happens not to need today, not proof
///      no surface like them is ever worth checking.
///
///   3. _piiKeys is a blocklist, not an allowlist. It only protects fields someone
///      remembered to add. A stricter (and more defensible) variant inverts this:
///      allow a known-safe set of keys and redact everything else by default.
///
///   4. Object-destructuring in structured logs (_logger.LogInformation("{@User}", user))
///      serializes every property of the object, PII included, in one ordinary-looking
///      line. This processor only runs against Activity (trace) data — it does nothing
///      for that log call by itself. See PiiRedactionLogProcessor for the logging-pipeline
///      equivalent — it reuses RedactPairsList/RedactFreeText from this class rather than
///      redefining the rules. Even with both registered, the call site still has to avoid
///      dumping whole domain objects in the first place.
///
///   5. Other instrumentation packages. This app's ASP.NET Core/EF Core instrumentation
///      doesn't capture headers or SQL parameter values by default — but defaults vary
///      across libraries and versions. A different DB driver, a gRPC client, or a
///      messaging library you add later may make different choices. Audit each
///      instrumentation source in your own stack rather than assuming this app's defaults.
/// </summary>
public sealed class PiiRedactionProcessor : BaseProcessor<Activity>
{
    // ---------------------------------------------------------------------------
    // Tier 1 — Field names that always carry PII.
    // The entire value is replaced: the key already tells you what was there.
    // Adapt this list to the attribute names your application and its libraries use.
    // ---------------------------------------------------------------------------
    // internal: PiiRedactionLogProcessor (the logging-pipeline counterpart) reuses
    // this exact list and these exact patterns. One set of rules, two thin
    // BaseProcessor<T> adapters, not two parallel definitions to keep in sync.
    internal static readonly HashSet<string> _piiKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "customer.email",
        "user.email",
        "email",
        "http.request.header.authorization",
        "enduser.id",
        // "http.url" deliberately NOT here as a TAG key — that would redact the entire
        // URL, including the path. But RedactQueryStringKeys (below) parses the query
        // string out of any string value and checks each PARAMETER name against this
        // same list — so "email" above also protects ?email=... inside http.url,
        // without ever listing "http.url" itself.
    };

    // ---------------------------------------------------------------------------
    // Tier 2 — Value patterns for PII that can appear in any field.
    //
    // Only the MATCHED portion is replaced — the rest of the value is preserved.
    // This is important for connection strings: the server name and database are
    // diagnostic; only the password segment needs to go.
    //
    // Input:  "Server=prod-db;Database=orders;Password=s3cr3t;Port=5432"
    // Output: "Server=prod-db;Database=orders;password=[REDACTED];Port=5432"
    //
    // Replace or remove entries based on what your application handles:
    //
    //  - connection-string: database drivers often include the full connection string in
    //    exception messages when a connection fails. Field name is unpredictable — it
    //    can appear in exception.message, db.statement, or any custom diagnostic tag.
    //
    //  - ssn: only relevant if your domain handles national identity numbers. The field
    //    might be called "ssn", "socialSecurityNumber", "personIdentifier", "nin", etc.
    //    Pattern matching catches all of them without enumerating every possible name.
    //    Here the entire match is replaced — there is no surrounding context to preserve.
    //
    // Add patterns for credit card numbers, IBANs, bearer tokens, or any other structured
    // sensitive value your system is likely to encounter.
    // ---------------------------------------------------------------------------
    internal static readonly (Regex Pattern, string Replacement)[] _valuePatterns =
    [
        // Replace only the password segment — server, database, port remain readable
        (new Regex(@"(?:password|pwd)\s*=\s*[^;""'\s]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
         "password=[REDACTED]"),

        // Replace the SSN digits entirely — no useful context to preserve around them
        // Remove if SSNs are not part of your data model
        (new Regex(@"\b\d{3}-\d{2}-\d{4}\b",
            RegexOptions.Compiled),
         "[REDACTED:ssn]"),

        // Replace an email address by shape, regardless of which field/parameter carries
        // it. RedactQueryStringKeys (name-based) already catches "?email=..." before this
        // runs, so there's no double-match there — this exists for free text with no key
        // at all (FormattedMessage, StatusDescription) and for parameters this app never
        // anticipated (e.g. "?contact=..."), where a name-based list can't help.
        (new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled),
         "[REDACTED:email]"),
    ];

    private static ActivityListener? _exceptionRedactionListener;

    /// <summary>
    /// Redacts exception.message/exception.stacktrace BEFORE Activity.AddException()
    /// creates the exception event — not after, in OnEnd below. Verified by testing:
    /// an ActivityEvent's Tags has no setter, isn't IDictionary or IList, and Activity
    /// exposes no RemoveEvent/ClearEvents — once AddException adds that event, nothing
    /// downstream, including this class's own OnEnd, can rewrite or remove it. The BCL's
    /// actual extensibility point for this is ActivityListener.ExceptionRecorder, which
    /// runs before the (otherwise immutable) event exists and can supply override tags.
    /// Call once at startup; ActivitySource keeps its own strong reference to the listener.
    /// </summary>
    public static void RegisterExceptionRedaction()
    {
        if (_exceptionRedactionListener is not null)
            return;

        _exceptionRedactionListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
            ExceptionRecorder = (Activity _, Exception exception, ref TagList tags) =>
            {
                tags.Add("exception.message", RedactFreeText(exception.Message));
                tags.Add("exception.stacktrace", RedactFreeText(exception.ToString()));
            },
        };

        ActivitySource.AddActivityListener(_exceptionRedactionListener);
    }

    // No events loop here. Verified by testing, not assumed: Activity.AddEvent always
    // copies tags into its own private TagsLinkedList, regardless of what type you pass
    // in — even a hand-built, otherwise-mutable ActivityTagsCollection gets copied away
    // on attach. `activityEvent.Tags is ActivityTagsCollection` is never true for any
    // event read back from Activity.Events. There is no downstream fix for event tags,
    // for any event, full stop — the only supported interception point is BEFORE the
    // event is created. RegisterExceptionRedaction above covers the one case the BCL
    // gives a hook for (exceptions). Any other custom AddEvent call carries its own risk
    // and must be redacted at the call site — there's no centralized backstop possible.
    public override void OnEnd(Activity activity)
    {
        RedactTags(activity.TagObjects, (key, value) => activity.SetTag(key, value));

        // [DEMO-BLOCK BEGIN: pii-status-scan]
        if (activity.StatusDescription is string description)
        {
             var sanitized = RedactFreeText(description);
             if (sanitized != description)
                 activity.SetStatus(activity.Status, sanitized);
        }
        // [DEMO-BLOCK END: pii-status-scan]
    }

    internal static void RedactTags(
        IEnumerable<KeyValuePair<string, object?>> tags,
        Action<string, object?> setTag)
    {
        foreach (var pair in RedactPairsList(tags))
            setTag(pair.Key, pair.Value);
    }

    // List-rebuild shape of the same rules, for callers that hold an immutable
    // collection (e.g. LogRecord.Attributes) rather than a mutable SetTag-style API.
    internal static List<KeyValuePair<string, object?>> RedactPairsList(
        IEnumerable<KeyValuePair<string, object?>> tags)
    {
        var result = new List<KeyValuePair<string, object?>>();

        foreach (var tag in tags)
        {
            // Tier 1: key-based — replace the entire value.
            if (_piiKeys.Contains(tag.Key))
            {
                result.Add(new(tag.Key, "[REDACTED:field]"));
                continue;
            }

            // Tier 2: value-based — replace only the sensitive portion of the value.
            // This preserves diagnostic context (e.g. server name in a connection string)
            // while stripping the secret (e.g. the password segment).
            if (tag.Value is string value)
            {
                var sanitized = RedactFreeText(value);
                result.Add(new(tag.Key, sanitized));
                continue;
            }

            result.Add(tag);
        }

        return result;
    }

    // The value-only half of the rules: query-string parameter names checked against
    // _piiKeys, then every _valuePatterns regex. Used for anything that's just a bare
    // string with no key of its own — Activity.StatusDescription, LogRecord.FormattedMessage.
    internal static string RedactFreeText(string value)
    {
        // Tier 1, reapplied to a URL's query parameters: a query string has no
        // recognizable "shape" the way an email or SSN does, so a value-pattern
        // regex can't catch an api_key or token. But the parameter NAME is just
        // as much a name you control as a tag key — so the same _piiKeys list
        // applies, just against the parsed query string instead of tag.Key.
        var sanitized = RedactQueryStringKeys(value);

        foreach (var (pattern, replacement) in _valuePatterns)
            sanitized = pattern.Replace(sanitized, replacement);

        return sanitized;
    }

    internal static string RedactQueryStringKeys(string value)
    {
        var queryStart = value.IndexOf('?');
        if (queryStart < 0)
            return value;

        var pairs = value[(queryStart + 1)..].Split('&');
        for (var i = 0; i < pairs.Length; i++)
        {
            var eq = pairs[i].IndexOf('=');
            if (eq < 0)
                continue;

            var key = pairs[i][..eq];
            if (_piiKeys.Contains(key))
                pairs[i] = $"{key}=[REDACTED:field]";
        }

        return value[..(queryStart + 1)] + string.Join('&', pairs);
    }
}
