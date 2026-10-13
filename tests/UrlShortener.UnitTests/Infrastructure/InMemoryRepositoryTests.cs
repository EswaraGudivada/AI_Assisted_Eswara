using UrlShortener.Core.Domain;
using UrlShortener.UnitTests.Fakes.InMemory;

namespace UrlShortener.UnitTests.Infrastructure;

public class InMemoryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly InMemoryLinkStore _store = new();

    [Fact]
    public async Task TryAdd_returns_false_for_duplicate_codes()
    {
        var repository = new InMemoryShortLinkRepository(_store);

        Assert.True(await repository.TryAddAsync(new ShortLink { Code = "dup", OriginalUrl = "https://a.example/" }));
        Assert.False(await repository.TryAddAsync(new ShortLink { Code = "dup", OriginalUrl = "https://b.example/" }));
    }

    [Fact]
    public async Task Concurrent_adds_of_the_same_code_have_exactly_one_winner()
    {
        var repository = new InMemoryShortLinkRepository(_store);

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(i =>
            Task.Run(() => repository.TryAddAsync(new ShortLink { Code = "race", OriginalUrl = $"https://{i}.example/" }))));

        Assert.Single(results, r => r);
    }

    [Fact]
    public async Task RecordBatch_updates_counters_and_keeps_the_latest_access_time()
    {
        var links = new InMemoryShortLinkRepository(_store);
        var clicks = new InMemoryClickEventRepository(_store);
        await links.TryAddAsync(new ShortLink { Code = "abc", OriginalUrl = "https://a.example/" });

        await clicks.RecordBatchAsync([Click("abc", Now.AddMinutes(5)), Click("abc", Now)]);
        await clicks.RecordBatchAsync([Click("abc", Now.AddMinutes(1))]);

        var link = await links.GetByCodeAsync("abc");
        Assert.Equal(3, link!.ClickCount);
        Assert.Equal(Now.AddMinutes(5), link.LastAccessedAt);
        Assert.Equal(2, (await clicks.GetRangeAsync("abc", Now.AddMinutes(1), DateTimeOffset.MaxValue)).Count);
    }

    [Fact]
    public async Task Concurrent_batches_do_not_lose_counter_updates()
    {
        var links = new InMemoryShortLinkRepository(_store);
        var clicks = new InMemoryClickEventRepository(_store);
        await links.TryAddAsync(new ShortLink { Code = "hot", OriginalUrl = "https://a.example/" });

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            Task.Run(() => clicks.RecordBatchAsync([Click("hot", Now)]))));

        Assert.Equal(100, (await links.GetByCodeAsync("hot"))!.ClickCount);
    }

    private static ClickEvent Click(string code, DateTimeOffset at) => new() { Code = code, OccurredAt = at };
}
