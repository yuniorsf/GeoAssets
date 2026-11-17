using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Shared.Tests.Services.OrderTargets.TargetWorkFakes;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>Saving one target's work, with optimistic concurrency (XD01-313).</summary>
public sealed class OrderTargetWorkSessionTests
{
    private readonly FakeTargetStore _store = new();

    private OrderTargetWorkSession Sut(ServiceOrderTarget target) => new(_store, _store, target, Catalog);

    private static ServiceOrderTarget Stored(int version = 3) => Target("a", activityIds: ["adjust-attachment"], note: "old", version: version);

    [Fact]
    public void Guards()
    {
        FluentActions.Invoking(() => new OrderTargetWorkSession(null!, _store, Stored(), Catalog)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OrderTargetWorkSession(_store, null!, Stored(), Catalog)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OrderTargetWorkSession(_store, _store, null!, Catalog)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OrderTargetWorkSession(_store, _store, Stored(), null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Save_SendsTheEditAtTheVersionItWasRead_ThenStartsOverAtTheNextVersion()
    {
        var sut = Sut(Stored());
        sut.Draft!.Status = OrderTargetStatus.InProgress;
        sut.Draft.SetChosen("remove-attachment", true);
        sut.Draft.Note = "new";

        var result = await sut.SaveAsync(CancellationToken.None);

        var sent = _store.Updates.Should().ContainSingle().Subject;
        sent.Version.Should().Be(3);
        sent.Status.Should().Be(OrderTargetStatus.InProgress);
        sent.ActivityIds.Should().Equal("adjust-attachment", "remove-attachment");
        sent.Note.Should().Be("new");

        result.Should().NotBeNull();
        result!.Saved.Should().BeTrue();
        result.Target.Should().Be(sent with { Version = 4 });
        sut.Draft!.Original.Should().Be(result.Target);
        sut.Draft.IsDirty.Should().BeFalse();
        sut.ErrorKey.Should().BeNull();
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Save_WithNothingToSave_SendsNothing()
    {
        var sut = Sut(Stored());

        sut.CanSave.Should().BeFalse();
        (await sut.SaveAsync(CancellationToken.None)).Should().BeNull();
        _store.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task ASecondSave_WhileTheFirstIsRunning_SendsNothing()
    {
        var gate = new TaskCompletionSource();
        _store.BeforeUpdate = (_, _) => gate.Task;
        var sut = Sut(Stored());
        sut.Draft!.Note = "new";

        var first = sut.SaveAsync(CancellationToken.None);
        sut.IsBusy.Should().BeTrue();
        sut.CanSave.Should().BeFalse();
        (await sut.SaveAsync(CancellationToken.None)).Should().BeNull();
        gate.SetResult();

        (await first).Should().NotBeNull();
        _store.Updates.Should().ContainSingle();
    }

    [Fact]
    public async Task AConflict_OverwritesNothing_ReadsTheTargetAgain_AndShowsWhatIsStoredNow()
    {
        var newer = Target("a", OrderTargetStatus.InProgress, activityIds: ["relocate-attachment"], note: "theirs", version: 4);
        _store.BeforeUpdate = (_, _) => Task.FromException(new ServiceOrderConcurrencyException("o1"));
        _store.ById = (_, _, _) => Task.FromResult<ServiceOrderTarget?>(newer);
        var sut = Sut(Stored());
        sut.Draft!.Note = "mine";

        var result = await sut.SaveAsync(CancellationToken.None);

        _store.ByIdReads.Should().Equal(("o1", "t-a"));
        result.Should().Be(new OrderTargetWorkResult(newer, Saved: false));
        sut.ErrorKey.Should().Be(OrderTargetWorkMessages.ConcurrentKey);
        sut.Draft!.Original.Should().Be(newer);
        sut.Draft.Note.Should().Be("theirs");
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task AConflict_WithTheTargetGone_DropsTheDraft()
    {
        _store.BeforeUpdate = (_, _) => Task.FromException(new ServiceOrderConcurrencyException("o1"));
        _store.ById = (_, _, _) => Task.FromResult<ServiceOrderTarget?>(null);
        var sut = Sut(Stored());
        sut.Draft!.Note = "mine";

        (await sut.SaveAsync(CancellationToken.None)).Should().BeNull();

        sut.Draft.Should().BeNull();
        sut.ErrorKey.Should().Be(OrderTargetWorkMessages.NotFoundKey);
        sut.CanSave.Should().BeFalse();
        (await sut.SaveAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task AConflict_ThatCannotBeReadBack_KeepsTheDraft_AndSaysSo()
    {
        _store.BeforeUpdate = (_, _) => Task.FromException(new ServiceOrderConcurrencyException("o1"));
        _store.ById = (_, _, _) => Task.FromException<ServiceOrderTarget?>(new HttpRequestException("down"));
        var sut = Sut(Stored());
        sut.Draft!.Note = "mine";

        (await sut.SaveAsync(CancellationToken.None)).Should().BeNull();

        sut.ErrorKey.Should().Be(OrderTargetWorkMessages.ConcurrentReloadFailedKey);
        sut.Draft!.Note.Should().Be("mine");
        sut.Draft.Original.Version.Should().Be(3, "saving again re-checks the version that was read");
    }

    [Fact]
    public async Task AConflict_WhoseReadBackTheCallerCancelled_Propagates()
    {
        using var caller = new CancellationTokenSource();
        _store.BeforeUpdate = (_, _) => Task.FromException(new ServiceOrderConcurrencyException("o1"));
        _store.ById = async (_, _, _) =>
        {
            await caller.CancelAsync();
            caller.Token.ThrowIfCancellationRequested();
            return null;
        };
        var sut = Sut(Stored());
        sut.Draft!.Note = "mine";

        await FluentActions.Awaiting(() => sut.SaveAsync(caller.Token)).Should().ThrowAsync<OperationCanceledException>();
        sut.IsBusy.Should().BeFalse();
    }

    /// <summary>The exception <paramref name="action"/> throws, as the framework builds it.</summary>
    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    public static TheoryData<Exception, string> Failures => new()
    {
        { new OrderActionDeniedException("o1", RuleEvaluationResult.Deny(OrderActionType.Annotate, "recipient")), "targetWork.error.denied" },
        { new KeyNotFoundException("gone"), OrderTargetWorkMessages.NotFoundKey },
        { Thrown(() => ArgumentException.ThrowIfNullOrWhiteSpace(" ")), "targetWork.error.invalid" },
        { new HttpRequestException("down"), "targetWork.error.failed" },
        { new TaskCanceledException("timeout"), "targetWork.error.failed" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AnyOtherFailure_BecomesAMessage_AndKeepsTheDraft(Exception failure, string expectedKey)
    {
        _store.BeforeUpdate = (_, _) => Task.FromException(failure);
        var sut = Sut(Stored());
        sut.Draft!.Note = "mine";

        (await sut.SaveAsync(CancellationToken.None)).Should().BeNull();

        sut.ErrorKey.Should().Be(expectedKey);
        sut.Draft!.Note.Should().Be("mine");
        sut.IsBusy.Should().BeFalse();
        _store.ByIdReads.Should().BeEmpty();
    }

    [Fact]
    public async Task ANewSave_ClearsThePreviousMessage()
    {
        var fail = true;
        _store.BeforeUpdate = (_, _) => fail ? Task.FromException(new HttpRequestException("down")) : Task.CompletedTask;
        var sut = Sut(Stored());
        sut.Draft!.Note = "mine";
        await sut.SaveAsync(CancellationToken.None);
        sut.ErrorKey.Should().NotBeNull();

        fail = false;
        await sut.SaveAsync(CancellationToken.None);

        sut.ErrorKey.Should().BeNull();
    }

    [Fact]
    public async Task ACancellationTheCallerAskedFor_Propagates()
    {
        using var caller = new CancellationTokenSource();
        _store.BeforeUpdate = async (_, _) => await caller.CancelAsync();
        var sut = Sut(Stored());
        sut.Draft!.Note = "mine";

        await FluentActions.Awaiting(() => sut.SaveAsync(caller.Token)).Should().ThrowAsync<OperationCanceledException>();
        sut.IsBusy.Should().BeFalse();
        sut.ErrorKey.Should().BeNull();
    }
}
