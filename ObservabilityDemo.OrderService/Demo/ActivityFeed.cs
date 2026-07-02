using System.Collections.Concurrent;

namespace ObservabilityDemo.OrderService.Demo;

public record ActivityEntry(
    DateTimeOffset Timestamp,
    string OrderId,
    string CustomerId,
    string Scenario,
    string Outcome,      // "success" | "failure"
    double DurationMs,
    string? ErrorMessage);

/// <summary>
/// Singleton in-memory ring buffer of recent order activity.
/// The Blazor UI subscribes to Changed to refresh the feed in real time.
/// </summary>
public sealed class ActivityFeed
{
    private const int Capacity = 30;
    private readonly ConcurrentQueue<ActivityEntry> _entries = new();

    public event Action? Changed;

    public void Add(ActivityEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity)
            _entries.TryDequeue(out _);
        Changed?.Invoke();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        Changed?.Invoke();
    }

    public IReadOnlyList<ActivityEntry> GetAll()
        => _entries.Reverse().ToList();
}
