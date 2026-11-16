using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary><see cref="IServiceOrderTargetWriter.UpdateAsync"/> and <see cref="IServiceOrderTargetWriter.RemoveAsync"/>.</summary>
public abstract partial class ServiceOrderTargetStoreContractTests
{
    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WritesActivitiesStatusConflictLevelAndNote_AndNothingElse()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var original = Target(harness, "ord-1", "feat-1")
            .WithId("tgt-1")
            .WithAssetTypeId("pole")
            .WithAlias("P-1")
            .WithActivityIds("inspect")
            .WithNote("before")
            .WithPosition(-38.5, -12.9)
            .Build();
        await harness.Writer.AddRangeAsync("ord-1", [original], CancellationToken.None);

        var edited = original with
        {
            // The four members an update persists …
            ActivityIds = ["replace", "inspect", "clean"],
            Status = OrderTargetStatus.InProgress,
            ConflictLevel = OrderTargetConflictLevel.Blocked,
            Note = "after",
            // … and members fixed when the target was added, which it must ignore.
            FeatureId = "feat-other",
            AssetTypeId = "manhole",
            Alias = "changed",
            Longitude = 10,
            Latitude = 20,
            AddedAt = original.AddedAt.AddDays(3),
            AddedBy = "someone-else",
        };
        await harness.Writer.UpdateAsync(edited, CancellationToken.None);

        var stored = (await AllTargetsAsync(harness, "ord-1")).Should().ContainSingle().Subject;
        stored.Should().BeEquivalentTo(original with
        {
            ActivityIds = ["replace", "inspect", "clean"],
            Status = OrderTargetStatus.InProgress,
            ConflictLevel = OrderTargetConflictLevel.Blocked,
            Note = "after",
            Version = 1,
        });
        stored.ActivityIds.Should().Equal("replace", "inspect", "clean");
    }

    [Fact]
    public async Task UpdateAsync_NullNoteAndNoActivities_ClearsBoth()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var original = Target(harness, "ord-1", "feat-1").WithActivityIds("inspect").WithNote("to be cleared").Build();
        await harness.Writer.AddRangeAsync("ord-1", [original], CancellationToken.None);

        await harness.Writer.UpdateAsync(original with { ActivityIds = [], Note = null }, CancellationToken.None);

        var stored = (await AllTargetsAsync(harness, "ord-1")).Should().ContainSingle().Subject;
        stored.ActivityIds.Should().BeEmpty();
        stored.Note.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_OnSuccess_StoresTheGivenVersionPlusOne_SoTheCallerCanContinueFromIt()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var target = Target(harness, "ord-1", "feat-1").WithVersion(7).Build();
        await harness.Writer.AddRangeAsync("ord-1", [target], CancellationToken.None);

        await harness.Writer.UpdateAsync(target with { Note = "first" }, CancellationToken.None);
        await harness.Writer.UpdateAsync(target with { Note = "second", Version = 8 }, CancellationToken.None);

        var stored = (await AllTargetsAsync(harness, "ord-1")).Should().ContainSingle().Subject;
        stored.Version.Should().Be(9);
        stored.Note.Should().Be("second");
    }

    [Fact]
    public async Task UpdateAsync_StaleVersion_ThrowsServiceOrderConcurrencyExceptionAndWritesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var read = Target(harness, "ord-1", "feat-1").WithNote("as read").Build();
        await harness.Writer.AddRangeAsync("ord-1", [read], CancellationToken.None);
        await harness.Writer.UpdateAsync(read with { Note = "first writer" }, CancellationToken.None);

        var act = () => harness.Writer.UpdateAsync(
            read with { Note = "second writer", Status = OrderTargetStatus.Skipped }, CancellationToken.None);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        var stored = (await AllTargetsAsync(harness, "ord-1")).Should().ContainSingle().Subject;
        stored.Note.Should().Be("first writer");
        stored.Status.Should().Be(OrderTargetStatus.Pending);
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_VersionAheadOfTheStoredOne_IsAlsoAConcurrencyConflict()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var target = Target(harness, "ord-1", "feat-1").Build();
        await harness.Writer.AddRangeAsync("ord-1", [target], CancellationToken.None);

        var act = () => harness.Writer.UpdateAsync(target with { Version = 5, Note = "from the future" }, CancellationToken.None);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        (await AllTargetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Note.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_TargetThatDoesNotExist_ThrowsKeyNotFoundException()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");

        var act = () => harness.Writer.UpdateAsync(Target(harness, "ord-1", "feat-1").Build(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_TargetIdThatBelongsToAnotherOrder_ThrowsKeyNotFoundExceptionAndLeavesThatTargetAlone()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await SeedOrderAsync(harness, "ord-2");
        var owned = Target(harness, "ord-1", "feat-1").WithId("tgt-1").WithNote("mine").Build();
        await harness.Writer.AddRangeAsync("ord-1", [owned], CancellationToken.None);

        var act = () => harness.Writer.UpdateAsync(owned with { OrderId = "ord-2", Note = "hijacked" }, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await AllTargetsAsync(harness, "ord-1")).Should().BeEquivalentTo([owned]);
    }

    [Fact]
    public async Task UpdateAsync_NullTarget_ThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Writer.UpdateAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_DoesNotRecheckStatusMoves()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var done = Target(harness, "ord-1", "feat-1").WithStatus(OrderTargetStatus.Done).Build();
        await harness.Writer.AddRangeAsync("ord-1", [done], CancellationToken.None);

        // Done → Pending is not a legal move; enforcing that is ServiceOrderTarget.WithStatus's job.
        await harness.Writer.UpdateAsync(done with { Status = OrderTargetStatus.Pending }, CancellationToken.None);

        (await AllTargetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Status.Should().Be(OrderTargetStatus.Pending);
    }

    // ── RemoveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_RemovesTheListedTargets_IgnoresUnknownIds_AndReturnsHowManyItRemoved()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "feat-1").WithId("tgt-1").Build(),
                Target(harness, "ord-1", "feat-2").WithId("tgt-2").Build(),
                Target(harness, "ord-1", "feat-3").WithId("tgt-3").Build(),
            ],
            CancellationToken.None);

        var removed = await harness.Writer.RemoveAsync("ord-1", ["tgt-1", "tgt-3", "tgt-unknown", "tgt-1"], CancellationToken.None);
        var repeated = await harness.Writer.RemoveAsync("ord-1", ["tgt-1", "tgt-3"], CancellationToken.None);

        removed.Should().Be(2);
        repeated.Should().Be(0);
        (await AllTargetsAsync(harness, "ord-1")).Select(t => t.Id).Should().Equal("tgt-2");
    }

    [Fact]
    public async Task RemoveAsync_IdsOfAnotherOrdersTargets_AreIgnored()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await SeedOrderAsync(harness, "ord-2");
        await harness.Writer.AddRangeAsync("ord-1", [Target(harness, "ord-1", "feat-1").WithId("tgt-1").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-2", [Target(harness, "ord-2", "feat-1").WithId("tgt-2").Build()], CancellationToken.None);

        var removed = await harness.Writer.RemoveAsync("ord-1", ["tgt-1", "tgt-2"], CancellationToken.None);

        removed.Should().Be(1);
        (await harness.Reader.CountAsync("ord-1", CancellationToken.None)).Should().Be(0);
        (await AllTargetsAsync(harness, "ord-2")).Select(t => t.Id).Should().Equal("tgt-2");
    }

    [Fact]
    public async Task RemoveAsync_DoesNotCheckTheVersion()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var target = Target(harness, "ord-1", "feat-1").WithId("tgt-1").Build();
        await harness.Writer.AddRangeAsync("ord-1", [target], CancellationToken.None);
        await harness.Writer.UpdateAsync(target with { Note = "edited since the caller read it" }, CancellationToken.None);

        var removed = await harness.Writer.RemoveAsync("ord-1", ["tgt-1"], CancellationToken.None);

        removed.Should().Be(1);
    }

    [Fact]
    public async Task RemoveAsync_EmptyIds_ReturnsZero()
    {
        await using var harness = await CreateHarnessAsync();

        (await harness.Writer.RemoveAsync("ord-missing", [], CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task RemoveAsync_OrderThatDoesNotExist_ReturnsZero()
    {
        await using var harness = await CreateHarnessAsync();

        (await harness.Writer.RemoveAsync("ord-missing", ["tgt-1"], CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task RemoveAsync_NullIds_ThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Writer.RemoveAsync("ord-1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveAsync_BlankOrderId_ThrowsArgumentException(string orderId)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Writer.RemoveAsync(orderId, ["tgt-1"], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
