using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Core.Tests;
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
    /// <c>Task.Run</c> that then calls <c>Task.Delay(500, TimeProvider)</c>, which registers a
    /// timer via <see cref="TimeProvider.CreateTimer"/>. That registration happens asynchronously
    /// on the thread pool, so a test that calls <c>Advance()</c> immediately after mutating the
    /// repository can race ahead of it and advance the fake clock before the timer is even
    /// created — and this isn't merely "late": a <see cref="FakeTimeProvider"/> timer created
    /// with an already-past due time never fires on its own, nor on any subsequent <c>Advance()</c>
    /// call, so a lost race silently drops the save for the rest of the test rather than just
    /// delaying it. A fixed real-wall-clock sleep (the previous approach) is a probabilistic bet
    /// that the thread pool wins that race in time — one that this ticket's flakiness showed
    /// doesn't always pay off under full-suite parallel load.
    /// <para/>
    /// This subclass overrides <c>CreateTimer</c> to signal a <see cref="TaskCompletionSource"/>
    /// the instant AssetService's debounce timer actually registers, so the test can
    /// deterministically <c>await</c> "the timer just registered" instead of sleeping and hoping.
    /// Call <see cref="ArmNextRegistration"/> immediately before the mutation expected to trigger
    /// a new debounce timer, then await the returned task before calling <see cref="FakeTimeProvider.Advance"/>.
    /// </summary>
    private sealed class DebounceAwareTimeProvider : FakeTimeProvider
    {
        private TaskCompletionSource? _pendingRegistration;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _pendingRegistration?.TrySetResult();
            return timer;
        }

        /// <summary>
        /// Arms a one-shot wait for the next timer registration and returns a task that
        /// completes once it happens (bounded so a genuine regression fails fast instead of
        /// hanging). Must be called before the mutation expected to cause that registration.
        /// </summary>
        public Task ArmNextRegistration(int timeoutMs = 5000)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRegistration = tcs;
            return tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5);
    }

    [Fact]
    public async Task CollectionChanged_BeforeDebounceElapses_DoesNotSave()
    {
        var timeProvider = new DebounceAwareTimeProvider();
        var repository = new TestAssetProvider(timeProvider);
        var storage = new RecordingStorageService();
        await using var sut = new AssetService(repository, storage, timeProvider);

        var registered = timeProvider.ArmNextRegistration();
        repository.Add(new GeoFeature { Id = "a" });
        await registered;

        timeProvider.Advance(TimeSpan.FromMilliseconds(499));

        storage.SavedCollections.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectionChanged_AtDebounceElapsed_SavesOnce()
    {
        var timeProvider = new DebounceAwareTimeProvider();
        var repository = new TestAssetProvider(timeProvider);
        var storage = new RecordingStorageService();
        await using var sut = new AssetService(repository, storage, timeProvider);

        var registered = timeProvider.ArmNextRegistration();
        repository.Add(new GeoFeature { Id = "a" });
        await registered;

        timeProvider.Advance(TimeSpan.FromMilliseconds(500));

        await WaitUntilAsync(() => storage.SavedCollections.Count > 0);

        storage.SavedCollections.Should().ContainSingle();
        storage.SavedCollections[0].Features.Should().ContainSingle(f => f.Id == "a");
    }

    [Fact]
    public async Task RapidMutations_ResetDebounce_OnlySavesOnceAfterLastMutation()
    {
        var timeProvider = new DebounceAwareTimeProvider();
        var repository = new TestAssetProvider(timeProvider);
        var storage = new RecordingStorageService();
        await using var sut = new AssetService(repository, storage, timeProvider);

        var firstRegistered = timeProvider.ArmNextRegistration();
        repository.Add(new GeoFeature { Id = "a" });
        await firstRegistered;
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));

        var secondRegistered = timeProvider.ArmNextRegistration();
        repository.Add(new GeoFeature { Id = "b" }); // cancels the first debounce, starts a new 500ms window
        await secondRegistered;
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        storage.SavedCollections.Should().BeEmpty("only 250ms have elapsed since the last mutation");

        timeProvider.Advance(TimeSpan.FromMilliseconds(250)); // 500ms since the second mutation
        await WaitUntilAsync(() => storage.SavedCollections.Count > 0);

        storage.SavedCollections.Should().ContainSingle();
        storage.SavedCollections[0].Features.Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }
}
