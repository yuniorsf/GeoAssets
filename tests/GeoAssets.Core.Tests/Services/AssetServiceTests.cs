using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Provider.InMemory;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class AssetServiceTests
{
    private sealed class RecordingStorageService : IStorageService
    {
        public List<GeoFeatureCollection> SavedCollections { get; } = [];

        public Task<GeoFeatureCollection> LoadAsync(string key = "default", CancellationToken ct = default)
            => Task.FromResult(new GeoFeatureCollection());

        public Task SaveAsync(GeoFeatureCollection collection, string key = "default", CancellationToken ct = default)
        {
            SavedCollections.Add(collection);
            return Task.CompletedTask;
        }

        public Task<GeoFeatureCollection> ImportFromStringAsync(string geoJson, CancellationToken ct = default)
            => Task.FromResult(new GeoFeatureCollection());

        public Task<string> ExportToStringAsync(GeoFeatureCollection collection, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<string?> PickImportFileAsync(CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SaveExportFileAsync(string geoJson, string suggestedName = "export.geojson", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string?> GetStringAsync(string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetStringAsync(string key, string value, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// AssetService's auto-save debounce fires <c>CollectionChanged</c> into a background
    /// <c>Task.Run</c> that then calls <c>Task.Delay(500, TimeProvider)</c>. That registration
    /// happens asynchronously on the thread pool, so a test that calls <c>Advance()</c>
    /// immediately after mutating the repository can race ahead of it and advance the fake
    /// clock before the delay's timer even exists. <see cref="SettleSchedulerAsync"/> yields
    /// briefly (real wall-clock, but only enough for thread-pool scheduling — not simulating
    /// any part of the 500ms business delay itself, which is entirely driven by the fake clock)
    /// so the timer is guaranteed to be registered before the next <c>Advance()</c> call.
    /// </summary>
    private static Task SettleSchedulerAsync() => Task.Delay(50);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5);
    }

    [Fact]
    public async Task CollectionChanged_BeforeDebounceElapses_DoesNotSave()
    {
        var timeProvider = new FakeTimeProvider();
        var repository = new InMemoryAssetProvider(timeProvider);
        var storage = new RecordingStorageService();
        await using var sut = new AssetService(repository, storage, timeProvider);

        repository.Add(new GeoFeature { Id = "a" });
        await SettleSchedulerAsync();
        timeProvider.Advance(TimeSpan.FromMilliseconds(499));
        await SettleSchedulerAsync();

        storage.SavedCollections.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectionChanged_AtDebounceElapsed_SavesOnce()
    {
        var timeProvider = new FakeTimeProvider();
        var repository = new InMemoryAssetProvider(timeProvider);
        var storage = new RecordingStorageService();
        await using var sut = new AssetService(repository, storage, timeProvider);

        repository.Add(new GeoFeature { Id = "a" });
        await SettleSchedulerAsync();
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));

        await WaitUntilAsync(() => storage.SavedCollections.Count > 0);

        storage.SavedCollections.Should().ContainSingle();
        storage.SavedCollections[0].Features.Should().ContainSingle(f => f.Id == "a");
    }

    [Fact]
    public async Task RapidMutations_ResetDebounce_OnlySavesOnceAfterLastMutation()
    {
        var timeProvider = new FakeTimeProvider();
        var repository = new InMemoryAssetProvider(timeProvider);
        var storage = new RecordingStorageService();
        await using var sut = new AssetService(repository, storage, timeProvider);

        repository.Add(new GeoFeature { Id = "a" });
        await SettleSchedulerAsync();
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));

        repository.Add(new GeoFeature { Id = "b" }); // cancels the first debounce, starts a new 500ms window
        await SettleSchedulerAsync();
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await SettleSchedulerAsync();
        storage.SavedCollections.Should().BeEmpty("only 250ms have elapsed since the last mutation");

        timeProvider.Advance(TimeSpan.FromMilliseconds(250)); // 500ms since the second mutation
        await WaitUntilAsync(() => storage.SavedCollections.Count > 0);

        storage.SavedCollections.Should().ContainSingle();
        storage.SavedCollections[0].Features.Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }
}
