using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Shared.Tests.Services.OrderTargets.TargetWorkFakes;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>The working copy of one target's work (XD01-313).</summary>
public sealed class OrderTargetWorkDraftTests
{
    [Fact]
    public void Guards()
    {
        FluentActions.Invoking(() => new OrderTargetWorkDraft(null!, Catalog)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OrderTargetWorkDraft(Target("a"), null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StartsFromTheTarget_ListsActivitiesInCatalogOrder_NotDirty()
    {
        var draft = new OrderTargetWorkDraft(
            Target("a", OrderTargetStatus.InProgress, activityIds: ["remove-attachment", "relocate-attachment"], note: "n"), Catalog);

        draft.Status.Should().Be(OrderTargetStatus.InProgress);
        draft.Note.Should().Be("n");
        draft.ActivityIds.Should().Equal("relocate-attachment", "remove-attachment");
        draft.IsChosen("RELOCATE-ATTACHMENT").Should().BeTrue();
        draft.IsChosen("adjust-attachment").Should().BeFalse();
        draft.DroppedActivityCount.Should().Be(0);
        draft.IsDirty.Should().BeFalse("the same activities in another order are not a change");
    }

    [Fact]
    public void AnUnchangedTarget_InCatalogOrder_IsNotDirty_AndCannotBeSaved()
    {
        var draft = new OrderTargetWorkDraft(Target("a", activityIds: ["relocate-attachment", "remove-attachment"], note: "n"), Catalog);

        draft.IsDirty.Should().BeFalse();
        draft.CanSave.Should().BeFalse();
        FluentActions.Invoking(draft.Build).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ANullNote_StartsBlank()
    {
        var draft = new OrderTargetWorkDraft(Target("a"), Catalog);

        draft.Note.Should().BeEmpty();
        draft.NoteToSave.Should().BeNull();
        draft.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ActivitiesTheCatalogNoLongerOffers_AreDroppedAndCounted_AndMakeTheDraftSaveable()
    {
        var draft = new OrderTargetWorkDraft(Target("a", activityIds: ["gone", "adjust-attachment", "also-gone"]), Catalog);

        draft.DroppedActivityCount.Should().Be(2);
        draft.ActivityIds.Should().Equal("adjust-attachment");
        draft.IsDirty.Should().BeTrue();
        draft.CanSave.Should().BeTrue();
        draft.Build().ActivityIds.Should().Equal("adjust-attachment");
    }

    [Theory]
    [InlineData(OrderTargetStatus.Pending, new[] { OrderTargetStatus.Pending, OrderTargetStatus.InProgress, OrderTargetStatus.Skipped })]
    [InlineData(OrderTargetStatus.InProgress, new[] { OrderTargetStatus.InProgress, OrderTargetStatus.Done, OrderTargetStatus.NotExecutable })]
    [InlineData(OrderTargetStatus.Done, new[] { OrderTargetStatus.Done })]
    [InlineData(OrderTargetStatus.NotExecutable, new[] { OrderTargetStatus.NotExecutable })]
    [InlineData(OrderTargetStatus.Skipped, new[] { OrderTargetStatus.Skipped })]
    [InlineData("Unknown", new string[0])]
    public void StatusOptions_AreTheCurrentStatusAndTheMovesAllowedFromIt(string status, string[] expected) =>
        new OrderTargetWorkDraft(Target("a", status), Catalog).StatusOptions.Should().Equal(expected);

    [Fact]
    public void ATargetInAStatusThisClientDoesNotKnow_CannotBeSaved()
    {
        var draft = new OrderTargetWorkDraft(Target("a", "Unknown"), Catalog) { Note = "changed" };

        draft.IsDirty.Should().BeTrue();
        draft.CanSave.Should().BeFalse();
    }

    [Fact]
    public void AStatusNotOnOffer_CannotBeSaved()
    {
        var draft = new OrderTargetWorkDraft(Target("a"), Catalog) { Status = OrderTargetStatus.Done };

        draft.IsDirty.Should().BeTrue();
        draft.CanSave.Should().BeFalse();
    }

    [Fact]
    public void SetChosen_AddsAndRemoves_AndIgnoresWhatTheCatalogDoesNotOffer()
    {
        var draft = new OrderTargetWorkDraft(Target("a"), Catalog);

        draft.SetChosen("remove-attachment", true);
        draft.SetChosen("relocate-attachment", true);
        draft.SetChosen("not-offered", true);
        draft.ActivityIds.Should().Equal("relocate-attachment", "remove-attachment");
        draft.IsDirty.Should().BeTrue();

        draft.SetChosen("REMOVE-ATTACHMENT", false);
        draft.SetChosen("relocate-attachment", false);
        draft.ActivityIds.Should().BeEmpty();
        draft.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void AStatusMove_IsDirty_AndBuildsThroughTheDomain()
    {
        var target = Target("a", activityIds: ["adjust-attachment"], note: "old", version: 7);
        var draft = new OrderTargetWorkDraft(target, Catalog) { Status = OrderTargetStatus.InProgress };

        draft.CanSave.Should().BeTrue();
        var built = draft.Build();

        built.Status.Should().Be(OrderTargetStatus.InProgress);
        built.ActivityIds.Should().Equal("adjust-attachment");
        built.Note.Should().Be("old");
        built.Version.Should().Be(7, "the save is checked against the version the target was read at");
        built.Id.Should().Be(target.Id);
        draft.Original.Should().BeSameAs(target);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" kept as typed ", " kept as typed ")]
    public void ABlankNote_IsSavedAsNoNote(string typed, string? saved)
    {
        var draft = new OrderTargetWorkDraft(Target("a", note: "before"), Catalog) { Note = typed };

        draft.NoteToSave.Should().Be(saved);
        draft.IsDirty.Should().BeTrue();
        draft.Build().Note.Should().Be(saved);
    }

    [Fact]
    public void ANoteOverTheLimit_CannotBeSaved_AtTheLimitItCan()
    {
        var draft = new OrderTargetWorkDraft(Target("a"), Catalog) { Note = new string('x', OrderTargetWorkDraft.MaxNoteLength + 1) };
        draft.NoteTooLong.Should().BeTrue();
        draft.CanSave.Should().BeFalse();

        draft.Note = new string('x', OrderTargetWorkDraft.MaxNoteLength);
        draft.NoteTooLong.Should().BeFalse();
        draft.CanSave.Should().BeTrue();
        OrderTargetWorkDraft.MaxNoteLength.Should().Be(2048);
    }

    [Fact]
    public void ACatalogWithoutActivities_StillSavesAStatusAndANote()
    {
        var draft = new OrderTargetWorkDraft(Target("a"), []) { Status = OrderTargetStatus.Skipped, Note = "no access" };

        var built = draft.Build();

        built.Status.Should().Be(OrderTargetStatus.Skipped);
        built.ActivityIds.Should().BeEmpty();
        built.Note.Should().Be("no access");
    }
}
