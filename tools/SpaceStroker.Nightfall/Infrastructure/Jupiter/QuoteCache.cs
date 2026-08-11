using System.Collections.Concurrent;

namespace SpaceStroker.Nightfall.Infrastructure.Jupiter;

/// <summary>
/// A TTL cache with single-flight coalescing in front of Jupiter's quote endpoint.
///
/// ⚠️ Not an optimisation — it is what makes hover-quoting viable at all. Hover fires on every mouse
/// move, and Jupiter's free tier rate-limits hard; without a cache and in-flight coalescing, idle
/// mouse drift across a 20-wallet pool exhausts the quota in under a minute and the dashboard starts
/// showing errors instead of prices.
/// </summary>
public sealed class QuoteCache(TimeSpan ttl)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed record Entry(Task<JupiterQuote> Task, DateTimeOffset CreatedUtc);

    public async Task<JupiterQuote> GetOrAddAsync(string key, Func<Task<JupiterQuote>> factory)
    {
        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                // A faulted task must not be cached: the next hover should retry, not replay the error.
                var stale = now - existing.CreatedUtc > ttl;
                var broken = existing.Task.IsFaulted || existing.Task.IsCanceled;

                if (!stale && !broken)
                    return await existing.Task;

                _entries.TryRemove(new KeyValuePair<string, Entry>(key, existing));
            }

            // The task goes into the dictionary BEFORE it is awaited, so concurrent callers for the
            // same key attach to this one request instead of each firing their own.
            var lazy = new TaskCompletionSource<JupiterQuote>(TaskCreationOptions.RunContinuationsAsynchronously);
            var candidate = new Entry(lazy.Task, now);

            if (!_entries.TryAdd(key, candidate))
                continue;   // somebody else won the race — go round and attach to theirs

            try
            {
                var quote = await factory();
                lazy.SetResult(quote);
                return quote;
            }
            catch (Exception ex)
            {
                lazy.SetException(ex);
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, candidate));
                throw;
            }
        }
    }

    public void Clear() => _entries.Clear();
}
