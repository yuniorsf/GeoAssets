using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Shared.Tests.Services.OrderTargets.TargetWorkFakes;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>The shown order's targets as map feature states (XD01-313).</summary>
public sealed class OrderTargetMapStatesTests
{
    private const string Div = "map-div";

    private readonly FakeTargetStore _store = new();
    private readonly RecordingStateInterop _map = new();
    private readonly MapSelectionSet _selection = new();
    private readonly CountingLogger<OrderTargetMapStates> _logger = new();
    private readonly object _owner = new();

    private OrderTargetMapStates Sut() => new(_store, _map, _selection, new FixedMapContext(Div), _logger);

    private static ServiceOrderTarget[] Many(int count, string orderId = "o1") =>
        [.. Enumerable.Range(1, count).Select(i => Target($"f{i:D4}", orderId: orderId))];

    [Fact]
    public async Task Guards()
    {
        var sut = Sut();

        await FluentActions.Awaiting(() => sut.ShowAsync(null!, "o1", CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => sut.ShowAsync(_owner, " ", CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.ShowAsync(_owner, null!, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.RefreshAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => sut.ClearAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        _store.PageQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task Show_ReadsEveryPageOfTheMaximumSize_SortedByFeatureId_AndPushesEachPageAsItArrives()
    {
        _store.Add(Many(450));
        var sut = Sut();

        (await sut.ShowAsync(_owner, "o1", CancellationToken.None)).Should().BeTrue();

        _store.PageQueries.Select(q => (q.OrderId, q.Page, q.PageSize, q.SortBy, q.Descending, q.Status, q.ConflictLevel))
            .Should().Equal(
                ("o1", 1, 200, OrderTargetSortKeys.FeatureId, false, (string?)null, (OrderTargetConflictLevel?)null),
                ("o1", 2, 200, OrderTargetSortKeys.FeatureId, false, null, null),
                ("o1", 3, 200, OrderTargetSortKeys.FeatureId, false, null, null));
        _map.Calls.Select(c => (c.DivId, c.Ids.Length, c.State)).Should().Equal(
            (Div, 200, MapFeatureState.Pending), (Div, 200, MapFeatureState.Pending), (Div, 50, MapFeatureState.Pending));
        sut.OrderId.Should().Be("o1");
        sut.Shown.Should().HaveCount(450);
        OrderTargetMapStates.PageSize.Should().Be(OrderTargetQuery.MaxPageSize);
    }

    [Fact]
    public async Task Show_GroupsAPageByState_OneCallPerState()
    {
        _store.Add(
            Target("a"),
            Target("b", OrderTargetStatus.Done),
            Target("c", conflict: OrderTargetConflictLevel.Warning),
            Target("d", conflict: OrderTargetConflictLevel.Blocked),
            Target("e", OrderTargetStatus.Skipped),
            Target("f", OrderTargetStatus.InProgress));

        await Sut().ShowAsync(_owner, "o1", CancellationToken.None);

        _map.Calls.Select(c => (string.Join(",", c.Ids), c.State)).Should().BeEquivalentTo(new[]
        {
            ("a,f", MapFeatureState.Pending),
            ("b", MapFeatureState.Done),
            ("c", MapFeatureState.Conflict),
            ("d,e", MapFeatureState.Disabled),
        });
    }

    [Fact]
    public async Task Show_AnOrderWithoutTargets_ReadsOnce_AndPushesNothing()
    {
        var sut = Sut();

        (await sut.ShowAsync(_owner, "empty", CancellationToken.None)).Should().BeTrue();

        _store.PageQueries.Should().ContainSingle();
        _map.Calls.Should().BeEmpty();
        sut.OrderId.Should().Be("empty");
    }

    [Fact]
    public async Task Show_StopsAtAnEmptyPage_EvenIfTheTotalStillPromisesMore()
    {
        _store.Add(Many(3));
        _store.ReportedTotal = 5_000;

        (await Sut().ShowAsync(_owner, "o1", CancellationToken.None)).Should().BeTrue();

        _store.PageQueries.Select(q => q.Page).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Show_ForwardsALinkedToken_NotTheCallersOwn()
    {
        using var caller = new CancellationTokenSource();
        await Sut().ShowAsync(_owner, "o1", caller.Token);

        _store.PageTokens.Should().ContainSingle().Which.Should().NotBe(caller.Token);
    }

    [Fact]
    public async Task ShowingAnotherOrder_FirstGivesThePreviousFeaturesTheirSelectionStateBack()
    {
        _store.Add(Target("a"), Target("b"), Target("c"));
        _store.Add(Target("x", orderId: "o2"));
        _selection.Replace(["b", "zz"]);
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();

        await sut.ShowAsync(new object(), "o2", CancellationToken.None);

        _map.Calls.Should().HaveCount(3);
        _map.Calls[0].Should().BeEquivalentTo((Div, new[] { "a", "c" }, MapFeatureState.Default));
        _map.Calls[1].Should().BeEquivalentTo((Div, new[] { "b" }, MapFeatureState.Selected));
        _map.Calls[2].Should().BeEquivalentTo((Div, new[] { "x" }, MapFeatureState.Pending));
        sut.Shown.Keys.Should().Equal("x");
    }

    [Fact]
    public async Task Clear_ByTheOwner_RestoresOnlyTheFeaturesItStyled()
    {
        _store.Add(Target("a"), Target("b"));
        _selection.Replace(["a"]);
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();

        await sut.ClearAsync(_owner);

        _map.Calls.Select(c => (string.Join(",", c.Ids), c.State)).Should().Equal(("b", MapFeatureState.Default), ("a", MapFeatureState.Selected));
        sut.OrderId.Should().BeNull();
        sut.Shown.Should().BeEmpty();
    }

    [Fact]
    public async Task Clear_WithEverythingSelected_OrNothingSelected_MakesOnlyTheCallItNeeds()
    {
        _store.Add(Target("a"));
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();
        await sut.ClearAsync(_owner);
        _map.Calls.Should().ContainSingle().Which.State.Should().Be(MapFeatureState.Default);

        _selection.Replace(["a"]);
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();
        await sut.ClearAsync(_owner);
        _map.Calls.Should().ContainSingle().Which.State.Should().Be(MapFeatureState.Selected);
    }

    [Fact]
    public async Task Clear_ByAnotherOwner_DoesNothing()
    {
        _store.Add(Target("a"));
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();

        await sut.ClearAsync(new object());

        _map.Calls.Should().BeEmpty();
        sut.OrderId.Should().Be("o1");
    }

    [Fact]
    public async Task Clear_AfterAnotherOwnerShowedTheSameOrder_LeavesTheNewerViewAlone()
    {
        _store.Add(Target("a"));
        var older = new object();
        var sut = Sut();
        await sut.ShowAsync(older, "o1", CancellationToken.None);
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();

        await sut.ClearAsync(older);

        _map.Calls.Should().BeEmpty();
        sut.Shown.Should().ContainKey("a");
    }

    [Fact]
    public async Task Clear_WithNothingShown_MakesNoCall()
    {
        var sut = Sut();
        await sut.ShowAsync(_owner, "empty", CancellationToken.None);

        await sut.ClearAsync(_owner);

        _map.Calls.Should().BeEmpty();
        sut.OrderId.Should().BeNull();
    }

    [Fact]
    public async Task CancelledByTheCaller_WhileReading_ReturnsFalseQuietly_AndPushesNoMore()
    {
        _store.Add(Many(450));
        using var caller = new CancellationTokenSource();
        _store.BeforePage = async (q, _) =>
        {
            if (q.Page == 2) await caller.CancelAsync();
        };

        (await Sut().ShowAsync(_owner, "o1", caller.Token)).Should().BeFalse();

        _map.Calls.Should().ContainSingle();
        _logger.Warnings.Should().Be(0);
    }

    [Fact]
    public async Task CancelledBetweenTwoStatesOfOnePage_PushesNothingMore()
    {
        _store.Add(Target("a"), Target("b", OrderTargetStatus.Done));
        using var caller = new CancellationTokenSource();
        _map.OnPush = async _ => await caller.CancelAsync();
        var sut = Sut();

        (await sut.ShowAsync(_owner, "o1", caller.Token)).Should().BeFalse();

        _map.Calls.Should().ContainSingle();
        sut.Shown.Should().ContainSingle();
    }

    [Fact]
    public async Task AFailedRead_IsLogged_ReturnsFalse_AndKeepsWhatWasPushed()
    {
        _store.Add(Many(450));
        _store.BeforePage = (q, _) => q.Page == 2 ? Task.FromException(new HttpRequestException("down")) : Task.CompletedTask;
        var sut = Sut();

        (await sut.ShowAsync(_owner, "o1", CancellationToken.None)).Should().BeFalse();

        _logger.Warnings.Should().Be(1);
        sut.Shown.Should().HaveCount(200);
        sut.OrderId.Should().Be("o1");
    }

    [Fact]
    public async Task ACancellationTheCallerDidNotAskFor_IsAFailure_NotSilence()
    {
        _store.BeforePage = (_, _) => Task.FromException(new TaskCanceledException("timeout"));

        (await Sut().ShowAsync(_owner, "o1", CancellationToken.None)).Should().BeFalse();

        _logger.Warnings.Should().Be(1);
    }

    [Fact]
    public async Task AFailedPush_IsLogged_AndReturnsFalse()
    {
        _store.Add(Target("a"));
        _map.OnPush = _ => Task.FromException(new InvalidOperationException("js gone"));

        (await Sut().ShowAsync(_owner, "o1", CancellationToken.None)).Should().BeFalse();

        _logger.Warnings.Should().Be(1);
    }

    [Fact]
    public async Task AFailedRestore_IsLogged_NotThrown()
    {
        _store.Add(Target("a"));
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.OnPush = _ => Task.FromException(new InvalidOperationException("js gone"));

        await sut.ClearAsync(_owner);

        _logger.Warnings.Should().Be(1);
        sut.OrderId.Should().BeNull();
    }

    [Fact]
    public async Task ShowingAnotherOrder_WhileTheFirstIsStillReading_StopsTheFirst()
    {
        _store.Add(Many(450));
        _store.Add(Target("x", orderId: "o2"));
        var gate = new TaskCompletionSource();
        _store.BeforePage = (q, ct) => q is { OrderId: "o1", Page: 2 } ? gate.Task.WaitAsync(ct) : Task.CompletedTask;
        var sut = Sut();

        var first = sut.ShowAsync(_owner, "o1", CancellationToken.None);
        var second = await sut.ShowAsync(new object(), "o2", CancellationToken.None);
        gate.SetResult();

        (await first).Should().BeFalse();
        second.Should().BeTrue();
        sut.OrderId.Should().Be("o2");
        sut.Shown.Keys.Should().Equal("x");
        _map.Calls.Last().Should().BeEquivalentTo((Div, new[] { "x" }, MapFeatureState.Pending));
        _logger.Warnings.Should().Be(0);
    }

    [Fact]
    public async Task ShowingAnotherOrder_WhileTheFirstIsStillTakingTheOldOneOff_LetsTheNewestWin()
    {
        _store.Add(Target("a"));
        _store.Add(Target("b", orderId: "o2"));
        _store.Add(Target("c", orderId: "o3"));
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        var gate = new TaskCompletionSource();
        _map.OnPush = state => state == MapFeatureState.Default ? gate.Task : Task.CompletedTask;

        var older = sut.ShowAsync(_owner, "o2", CancellationToken.None);
        _map.OnPush = null;
        var newer = await sut.ShowAsync(_owner, "o3", CancellationToken.None);
        gate.SetResult();

        (await older).Should().BeFalse();
        newer.Should().BeTrue();
        sut.OrderId.Should().Be("o3");
        _store.PageQueries.Select(q => q.OrderId).Should().NotContain("o2");
    }

    [Fact]
    public async Task Refresh_RestylesATargetOfTheShownOrder()
    {
        _store.Add(Target("a"));
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();

        await sut.RefreshAsync(Target("a", OrderTargetStatus.Done));

        _map.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo((Div, new[] { "a" }, MapFeatureState.Done));
        sut.Shown["a"].Should().Be(MapFeatureState.Done);
    }

    [Fact]
    public async Task Refresh_OfAnotherOrder_OrWithNothingShown_DoesNothing()
    {
        var sut = Sut();
        await sut.RefreshAsync(Target("a"));
        _map.Calls.Should().BeEmpty();

        _store.Add(Target("a"));
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.Calls.Clear();
        await sut.RefreshAsync(Target("a", orderId: "o2"));

        _map.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_WhileTheOrderIsStillLoading_IsNotOverwrittenByAPageReadBefore()
    {
        _store.Add(Many(201));
        var sut = Sut();
        var gate = new TaskCompletionSource();
        _store.BeforePage = (q, ct) => q.Page == 2 ? gate.Task.WaitAsync(ct) : Task.CompletedTask;

        var showing = sut.ShowAsync(_owner, "o1", CancellationToken.None);
        await sut.RefreshAsync(Target("f0201", OrderTargetStatus.Skipped));
        gate.SetResult();
        await showing;

        sut.Shown["f0201"].Should().Be(MapFeatureState.Disabled);
        _map.Calls.Last().State.Should().Be(MapFeatureState.Disabled);
        _map.Calls.Count(c => c.Ids.Contains("f0201")).Should().Be(1);
    }

    [Fact]
    public async Task Refresh_AFailedPush_IsLogged_NotThrown()
    {
        _store.Add(Target("a"));
        var sut = Sut();
        await sut.ShowAsync(_owner, "o1", CancellationToken.None);
        _map.OnPush = _ => Task.FromException(new InvalidOperationException("js gone"));

        await sut.RefreshAsync(Target("a", OrderTargetStatus.InProgress));

        _logger.Warnings.Should().Be(1);
    }

    [Fact]
    public void AddGeoAssetsPanelState_RegistersItScoped()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(OrderTargetMapStates)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }
}
