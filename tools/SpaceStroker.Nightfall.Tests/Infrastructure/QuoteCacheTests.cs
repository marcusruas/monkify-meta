using System.Text.Json;
using Shouldly;
using SpaceStroker.Nightfall.Infrastructure.Jupiter;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Infrastructure;

public class QuoteCacheTests
{
    private static JupiterQuote Quote(ulong outAmount = 1_000_000_000UL)
        => new("mint", "sol", 100UL, outAmount, 0.5m, JsonDocument.Parse("{}").RootElement);

    [Fact]
    public async Task Serves_a_second_request_inside_the_ttl_from_cache()
    {
        // Hover fires on every mouse move; without this, idle drift exhausts Jupiter's free tier.
        var calls = 0;
        var cache = new QuoteCache(TimeSpan.FromMinutes(1));

        await cache.GetOrAddAsync("k", () => { calls++; return Task.FromResult(Quote()); });
        await cache.GetOrAddAsync("k", () => { calls++; return Task.FromResult(Quote()); });

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Refetches_once_the_ttl_has_passed()
    {
        var calls = 0;
        var cache = new QuoteCache(TimeSpan.FromMilliseconds(30));

        await cache.GetOrAddAsync("k", () => { calls++; return Task.FromResult(Quote()); });
        await Task.Delay(80);
        await cache.GetOrAddAsync("k", () => { calls++; return Task.FromResult(Quote()); });

        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Coalesces_concurrent_requests_for_the_same_key_into_one_call()
    {
        // A sweep across cards holding the same position must not fire N identical quotes.
        var calls = 0;
        var release = new TaskCompletionSource();
        var cache = new QuoteCache(TimeSpan.FromMinutes(1));

        async Task<JupiterQuote> Slow()
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return Quote();
        }

        var first = cache.GetOrAddAsync("k", Slow);
        var second = cache.GetOrAddAsync("k", Slow);
        var third = cache.GetOrAddAsync("k", Slow);

        release.SetResult();
        await Task.WhenAll(first, second, third);

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Keeps_different_keys_apart()
    {
        var calls = 0;
        var cache = new QuoteCache(TimeSpan.FromMinutes(1));

        await cache.GetOrAddAsync("a", () => { calls++; return Task.FromResult(Quote()); });
        await cache.GetOrAddAsync("b", () => { calls++; return Task.FromResult(Quote()); });

        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Does_not_cache_a_failure()
    {
        // A transient Jupiter error must not poison the cache for the whole TTL — the next hover
        // should retry rather than replay the error.
        var calls = 0;
        var cache = new QuoteCache(TimeSpan.FromMinutes(1));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            cache.GetOrAddAsync("k", () => { calls++; return Task.FromException<JupiterQuote>(new InvalidOperationException()); }));

        await cache.GetOrAddAsync("k", () => { calls++; return Task.FromResult(Quote()); });

        calls.ShouldBe(2);
    }
}
