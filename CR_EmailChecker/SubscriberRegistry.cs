using System.Collections.Concurrent;

namespace EmailChecker;

record Subscriber(string Url, string? Token);

/// <summary>
/// Thread-safe registry of webhook subscribers.
/// Re-registering the same URL updates its token and clears its failure count.
/// A subscriber is auto-removed after 3 consecutive delivery failures.
/// </summary>
class SubscriberRegistry
{
    private const int MaxConsecutiveFailures = 3;

    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = new();
    private readonly ConcurrentDictionary<string, int>        _failures    = new();

    public void Add(Subscriber subscriber)
    {
        _subscribers[subscriber.Url] = subscriber;
        _failures.TryRemove(subscriber.Url, out _);
        Console.WriteLine($"Subscriber registered: {subscriber.Url} (total: {_subscribers.Count})");
    }

    public void Remove(string url)
    {
        _subscribers.TryRemove(url, out _);
        _failures.TryRemove(url, out _);
        Console.WriteLine($"Subscriber removed: {url} (total: {_subscribers.Count})");
    }

    public void RecordSuccess(string url) => _failures.TryRemove(url, out _);

    public void RecordFailure(string url)
    {
        int count = _failures.AddOrUpdate(url, 1, (_, n) => n + 1);
        if (count >= MaxConsecutiveFailures)
        {
            Remove(url);
            Console.WriteLine($"Subscriber auto-removed after {count} consecutive failures: {url}");
        }
    }

    public IReadOnlyList<Subscriber> GetAll() => _subscribers.Values.ToList();
}
