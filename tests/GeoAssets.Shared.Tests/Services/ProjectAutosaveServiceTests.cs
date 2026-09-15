using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// Coverage of <see cref="ProjectAutosaveService"/> (XD01-143) — same
/// <see cref="PeriodicTimer"/>-driven-by-fake-<see cref="TimeProvider"/> testing shape as
/// <c>SessionTimeoutServiceTests</c> (GeoAssets.Web.Tests).
/// </summary>
public class ProjectAutosaveServiceTests
{
    /// <summary>See <c>SessionTimeoutServiceTests.SettleSchedulerAsync</c>'s doc comment — a
    /// short settle wait after <see cref="FakeTimeProvider.Advance"/> for the tick's
    /// continuation to actually run; simulated time itself is driven entirely by the fake clock.</summary>
    private static Task SettleSchedulerAsync() => Task.Delay(50);

    private sealed class FakeStorageService : IStorageService
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<GeoFeatureCollection> LoadAsync(string key = "default", CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAsync(GeoFeatureCollection collection, string key = "default", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GeoFeatureCollection> ImportFromStringAsync(string geoJson, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> ExportToStringAsync(GeoFeatureCollection collection, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> PickImportFileAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveExportFileAsync(string geoJson, string suggestedName = "export.geojson", CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string?> GetStringAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetStringAsync(string key, string value, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProjectSessionService : IProjectSessionService
    {
        public Project? Current { get; set; }
        public bool IsDirty { get; set; }
        public event EventHandler? DirtyChanged;
        public event EventHandler<Project>? Saved;
        public Func<Task<ProjectCloseChoice>>? CloseRequested { get; set; }

        public int SaveCallCount { get; private set; }
        public Func<Task>? OnSave { get; set; }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            SaveCallCount++;
            if (OnSave is not null) await OnSave();
            IsDirty = false;
        }

        public Task OpenAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAsAsync(string name, string description, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DiscardChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RequestCloseAsync() => throw new NotSupportedException();
        public void SetAssetTypeScope(ProjectAssetTypeScope scope) => throw new NotSupportedException();
        public void SetLayerScope(ProjectLayerScope scope) => throw new NotSupportedException();
        public void SetViewState(ProjectViewState viewState) => throw new NotSupportedException();
    }

    private static ProjectAutosaveService Sut(
        FakeProjectSessionService session, FakeTimeProvider timeProvider, FakeStorageService? storage = null) =>
        new(session, storage ?? new FakeStorageService(), timeProvider, NullLogger<ProjectAutosaveService>.Instance);

    private static Project OpenProject() => new() { Id = Guid.NewGuid(), Kind = ProjectKind.General };

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task InitAsync_NoPersistedSettings_UsesDefaults()
    {
        var sut = Sut(new FakeProjectSessionService(), new FakeTimeProvider());

        await sut.InitAsync();

        sut.Enabled.Should().BeTrue();
        sut.IntervalMinutes.Should().Be(10);
    }

    [Fact]
    public async Task InitAsync_PersistedSettings_RestoresThem()
    {
        var storage = new FakeStorageService();
        await storage.SetStringAsync("geoassets.project-autosave", """{"Enabled":false,"IntervalMinutes":5}""");
        var sut = Sut(new FakeProjectSessionService(), new FakeTimeProvider(), storage);

        await sut.InitAsync();

        sut.Enabled.Should().BeFalse();
        sut.IntervalMinutes.Should().Be(5);
    }

    // ── Acceptance criterion: enabled + dirty → autosave triggers and clears IsDirty ──

    [Fact]
    public async Task Tick_EnabledProjectOpenAndDirty_SavesSilentlyAndClearsDirty()
    {
        var session = new FakeProjectSessionService { Current = OpenProject(), IsDirty = true };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await SettleSchedulerAsync();

        session.SaveCallCount.Should().Be(1);
        session.IsDirty.Should().BeFalse();
    }

    // ── Acceptance criterion: disabled → no automatic save regardless of elapsed time ──

    [Fact]
    public async Task Tick_Disabled_NeverSavesRegardlessOfElapsedTime()
    {
        var session = new FakeProjectSessionService { Current = OpenProject(), IsDirty = true };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();
        await sut.SetEnabledAsync(false);

        timeProvider.Advance(TimeSpan.FromMinutes(30));
        await SettleSchedulerAsync();

        session.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_NoProjectOpen_DoesNotSave()
    {
        var session = new FakeProjectSessionService { Current = null, IsDirty = false };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await SettleSchedulerAsync();

        session.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_ProjectOpenButNotDirty_DoesNotSave()
    {
        var session = new FakeProjectSessionService { Current = OpenProject(), IsDirty = false };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await SettleSchedulerAsync();

        session.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_BelowInterval_DoesNotSaveYet()
    {
        var session = new FakeProjectSessionService { Current = OpenProject(), IsDirty = true };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();

        timeProvider.Advance(TimeSpan.FromMinutes(9));
        await SettleSchedulerAsync();

        session.SaveCallCount.Should().Be(0);
    }

    // ── Acceptance criterion: a SaveAsync failure surfaces a visible warning, never a silent no-op ──

    [Fact]
    public async Task Tick_SaveThrows_FiresAutosaveFailedInsteadOfSilentlyFailing()
    {
        // Fails without the fix: a naive fire-and-forget SaveAsync call would swallow the
        // exception and the safety net would fail exactly as silently as having none at all.
        var session = new FakeProjectSessionService
        {
            Current = OpenProject(),
            IsDirty = true,
            OnSave = () => throw new UnauthorizedAccessException("Not authorized (projects:manage-view) on this project."),
        };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        Exception? observed = null;
        sut.AutosaveFailed += (_, ex) => observed = ex;
        await sut.InitAsync();

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await SettleSchedulerAsync();

        observed.Should().BeOfType<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Tick_SaveThrows_LoopContinuesTickingAfterward()
    {
        var callCount = 0;
        var session = new FakeProjectSessionService
        {
            Current = OpenProject(),
            IsDirty = true,
            OnSave = () =>
            {
                callCount++;
                if (callCount == 1) throw new InvalidOperationException("transient failure");
                return Task.CompletedTask;
            },
        };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await SettleSchedulerAsync();
        session.IsDirty = true; // simulate more edits before the next tick
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await SettleSchedulerAsync();

        callCount.Should().Be(2);
    }

    // ── Settings mutation ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetEnabledAsync_PersistsThePreference()
    {
        var storage = new FakeStorageService();
        var sut = Sut(new FakeProjectSessionService(), new FakeTimeProvider(), storage);
        await sut.InitAsync();

        await sut.SetEnabledAsync(false);

        var stored = await storage.GetStringAsync("geoassets.project-autosave");
        stored.Should().Contain("\"Enabled\":false");
    }

    [Fact]
    public async Task SetIntervalMinutesAsync_PersistsThePreference()
    {
        var storage = new FakeStorageService();
        var sut = Sut(new FakeProjectSessionService(), new FakeTimeProvider(), storage);
        await sut.InitAsync();

        await sut.SetIntervalMinutesAsync(20);

        sut.IntervalMinutes.Should().Be(20);
        var stored = await storage.GetStringAsync("geoassets.project-autosave");
        stored.Should().Contain("\"IntervalMinutes\":20");
    }

    [Fact]
    public async Task SetIntervalMinutesAsync_TakesEffectOnTheRunningTimer()
    {
        var session = new FakeProjectSessionService { Current = OpenProject(), IsDirty = true };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();

        await sut.SetIntervalMinutesAsync(3);
        timeProvider.Advance(TimeSpan.FromMinutes(3));
        await SettleSchedulerAsync();

        session.SaveCallCount.Should().Be(1);
    }

    [Fact]
    public async Task SetIntervalMinutesAsync_NonPositive_Throws()
    {
        var sut = Sut(new FakeProjectSessionService(), new FakeTimeProvider());
        await sut.InitAsync();

        var act = () => sut.SetIntervalMinutesAsync(0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_StopsTheLoop_NoFurtherSavesOccur()
    {
        var session = new FakeProjectSessionService { Current = OpenProject(), IsDirty = true };
        var timeProvider = new FakeTimeProvider();
        var sut = Sut(session, timeProvider);
        await sut.InitAsync();

        await sut.DisposeAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await SettleSchedulerAsync();

        session.SaveCallCount.Should().Be(0);
    }
}
