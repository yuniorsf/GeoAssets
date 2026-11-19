using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Shared.Services.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Occupancy;

/// <summary>
/// <see cref="OccupancyChangeDraft"/>: the working copy behind <c>OccupancyChangeEditor</c>
/// (XD01-315) — the rows it shows, the one-change-per-row editing, and the live preview.
/// </summary>
public sealed class OccupancyChangeDraftTests
{
    private const string Pole = "pole-1";

    private static readonly Guid Owner = new("0a0a0a0a-0000-4000-8000-000000000001");
    private static readonly Guid OccupantA = new("0c0c0c0c-0000-4000-8000-00000000000a");

    private static Guid Id(int n) => new($"0e0e0e0e-0000-4000-8000-{n:D12}");

    private static PoleOccupancyEntry Entry(int id, int? slotIndex, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) => new()
    {
        Id = Id(id),
        PoleFeatureId = Pole,
        PoleOwnerOrganizationId = Owner,
        SlotIndex = slotIndex,
        Status = status,
        OccupantOrganizationId = OccupantA,
        OperatorCnpj = "12.345.678/0001-90",
        CableDiameterMm = 12.5m,
        CableLoadDaN = 100m,
    };

    /// <summary>A view of a pole with <paramref name="regulated"/> slots and <paramref name="beyond"/> entries beyond them (ids 101..), optionally with staged changes.</summary>
    private static TargetOccupancyView View(int regulated, int beyond = 0, int version = 3, params OccupancyChange[] staged)
    {
        var occupancy = PoleOccupancy.Create(
            Pole, Owner,
            [.. Enumerable.Range(1, regulated).Select(slot => Entry(slot, slot)), .. Enumerable.Range(1, beyond).Select(n => Entry(100 + n, null))],
            version, default);
        var current = OccupancyProjection.From(occupancy);

        return new TargetOccupancyView
        {
            OrderId = "ord-1",
            TargetId = "tgt-1",
            PoleFeatureId = Pole,
            PoleOwnerOrganizationId = Owner,
            CurrentVersion = version,
            Current = current,
            Preview = current,
            Staged = staged.Length == 0
                ? null
                : new OccupancyChangeSetView
                {
                    Id = Guid.NewGuid(), OrderId = "ord-1", TargetId = "tgt-1", PoleFeatureId = Pole, Status = OccupancyChangeSetStatus.Staged,
                    BaselineVersion = version, StagedBy = "crew-1", StagedAt = default, Changes = staged,
                },
        };
    }

    private static OccupancyEntryForm Form(PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted, string cnpj = "98.765.432/0001-10") =>
        new() { Status = status, OperatorCnpj = cnpj, OccupantOrganizationId = OccupantA.ToString(), CableDiameterMm = "9.5" };

    // ── Rows ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Rows_AlwaysShowTheSixRegulatedSlots_ThenTheEntriesBeyondThem()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 2, beyond: 2));

        var rows = draft.Rows();

        rows.Select(row => row.Key).Should().Equal(
            "slot:1", "slot:2", "slot:3", "slot:4", "slot:5", "slot:6", $"entry:{Id(101):D}", $"entry:{Id(102):D}");
        rows.Take(6).Select(row => row.SlotIndex).Should().Equal(1, 2, 3, 4, 5, 6);
        rows.Take(2).Should().OnlyContain(row => row.Current != null && row.Projected == row.Current && !row.IsFree);
        rows.Skip(2).Take(4).Should().OnlyContain(row => row.IsFree && row.Projected == null);
        rows.Skip(6).Should().OnlyContain(row => row.IsBeyondRegulatedSlots && row.Current != null && row.Change == null);
        draft.IsEmpty.Should().BeTrue();
        draft.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Rows_OfAPoleWithNothingOnFile_AreSixFreeSlots_AndThePreviewIsEmpty()
    {
        var view = View(regulated: 0) with { CurrentVersion = 0 };
        var draft = new OccupancyChangeDraft(view);

        draft.Rows().Should().HaveCount(6).And.OnlyContain(row => row.IsFree);
        draft.TryPreview(out var preview, out var errorKey).Should().BeTrue();
        preview.Should().BeEquivalentTo(OccupancyProjection.Empty);
        errorKey.Should().BeNull();
    }

    // ── Editing ───────────────────────────────────────────────────────────────

    [Fact]
    public void AddToSlot_StagesAnAddOnAFreeSlot_WithTheGivenId_AndThePreviewCountsIt()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 2));

        draft.AddToSlot(4, Form(), Id(50));

        draft.Changes.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = OccupancyChangeKind.Add, SlotIndex = (int?)4, EntryId = (Guid?)Id(50),
            Status = (PoleAttachmentOccupantStatus?)PoleAttachmentOccupantStatus.AuthorizedContracted, OperatorCnpj = "98.765.432/0001-10", CableDiameterMm = 9.5m,
        });
        var row = draft.Rows().Single(r => r.SlotIndex == 4);
        row.Change!.Kind.Should().Be(OccupancyChangeKind.Add);
        row.Current.Should().BeNull();
        row.Projected!.Id.Should().Be(Id(50));
        draft.TryPreview(out var preview, out _).Should().BeTrue();
        preview!.OccupiedCount.Should().Be(3);
        draft.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void AddToSlot_OnAnOccupiedSlot_OrOneWithAPendingChange_OrOutsideOneToSix_IsRefused()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 2));
        draft.AddToSlot(4, Form(), Id(50));

        ((Action)(() => draft.AddToSlot(1, Form(), Id(51)))).Should().Throw<InvalidOperationException>();
        ((Action)(() => draft.AddToSlot(4, Form(), Id(51)))).Should().Throw<InvalidOperationException>();
        ((Action)(() => draft.AddToSlot(0, Form(), Id(51)))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => draft.AddToSlot(7, Form(), Id(51)))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => draft.AddToSlot(5, null!, Id(51)))).Should().Throw<ArgumentNullException>();
        draft.Changes.Should().ContainSingle();
    }

    [Fact]
    public void AddBeyondRegulatedSlots_IsAlwaysPossible_AndTakesAFullPoleOverCapacity()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 6));

        var key = draft.AddBeyondRegulatedSlots(Form(), Id(50));
        var second = draft.AddBeyondRegulatedSlots(Form(), Id(51));

        key.Should().Be($"entry:{Id(50):D}");
        draft.Changes.Should().HaveCount(2).And.OnlyContain(change => change.AddsBeyondRegulatedSlots);
        draft.Rows().Select(row => row.Key).Should().EndWith([key, second]);
        draft.Rows()[^1].Should().BeEquivalentTo(new { SlotIndex = (int?)null, IsBeyondRegulatedSlots = true, IsOrphan = false });
        draft.TryPreview(out var preview, out _).Should().BeTrue();
        preview.Should().BeEquivalentTo(new { OccupiedCount = 8, IsOverCapacity = true, HasUnidentifiedOccupant = false, Class = OccupancyClass.OverCapacity });
    }

    [Fact]
    public void SetEntry_OnAnEntryOnFile_StagesAModify_AddressedBySlotOrByIdBeyondTheSlots()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 2, beyond: 1));
        var beyondKey = $"entry:{Id(101):D}";

        draft.SetEntry("slot:2", Form(PoleAttachmentOccupantStatus.AbandonedDeadDrop));
        draft.SetEntry(beyondKey, Form(PoleAttachmentOccupantStatus.ClandestineUnauthorized));

        draft.Changes.Select(change => (change.Kind, change.SlotIndex, change.EntryId, change.Status)).Should().Equal(
            (OccupancyChangeKind.Modify, 2, null, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
            (OccupancyChangeKind.Modify, null, Id(101), PoleAttachmentOccupantStatus.ClandestineUnauthorized));
        draft.Rows().Single(row => row.SlotIndex == 2).Projected!.Status.Should().Be(PoleAttachmentOccupantStatus.AbandonedDeadDrop);
        draft.Rows().Single(row => string.Equals(row.Key, beyondKey, StringComparison.Ordinal)).Projected!.Status.Should().Be(PoleAttachmentOccupantStatus.ClandestineUnauthorized);
    }

    [Fact]
    public void SetEntry_OnAnEntryBeingAdded_KeepsItAnAddWithTheSameId_AndOnAFreeSlotIsRefused()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 1));
        draft.AddToSlot(3, Form(), Id(50));
        var beyond = draft.AddBeyondRegulatedSlots(Form(), Id(51));

        draft.SetEntry("slot:3", Form(cnpj: "11.111.111/0001-11"));
        draft.SetEntry(beyond, Form(PoleAttachmentOccupantStatus.ClandestineUnauthorized));

        draft.Changes.Select(change => (change.Kind, change.SlotIndex, change.EntryId, change.OperatorCnpj, change.Status)).Should().Equal(
            (OccupancyChangeKind.Add, 3, Id(50), "11.111.111/0001-11", PoleAttachmentOccupantStatus.AuthorizedContracted),
            (OccupancyChangeKind.Add, null, Id(51), "98.765.432/0001-10", PoleAttachmentOccupantStatus.ClandestineUnauthorized));
        ((Action)(() => draft.SetEntry("slot:5", Form()))).Should().Throw<InvalidOperationException>("a free slot needs AddToSlot, which is given the new entry's id");
        ((Action)(() => draft.SetEntry("slot:9", Form()))).Should().Throw<KeyNotFoundException>();
        ((Action)(() => draft.SetEntry("slot:1", null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_And_MarkUnidentified_ReplaceWhatWasPendingOnTheRow_OneChangePerRow()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 3, beyond: 1));
        var beyondKey = $"entry:{Id(101):D}";

        draft.SetEntry("slot:1", Form(PoleAttachmentOccupantStatus.AbandonedDeadDrop));
        draft.Remove("slot:1");
        draft.MarkUnidentified("slot:2");
        draft.MarkUnidentified(beyondKey);
        draft.Remove(beyondKey);

        draft.Changes.Select(change => (change.Kind, change.SlotIndex, change.EntryId)).Should().Equal(
            (OccupancyChangeKind.Remove, 1, null),
            (OccupancyChangeKind.MarkUnidentified, 2, null),
            (OccupancyChangeKind.Remove, null, Id(101)));
        var rows = draft.Rows();
        rows.Single(row => row.SlotIndex == 1).Should().BeEquivalentTo(new { Projected = (PoleOccupancyEntry?)null, IsFree = false });
        rows.Single(row => row.SlotIndex == 2).Projected!.IsUnidentified.Should().BeTrue();
        draft.TryPreview(out var preview, out _).Should().BeTrue();
        preview.Should().BeEquivalentTo(new { OccupiedCount = 2, HasUnidentifiedOccupant = true, Class = OccupancyClass.Unidentified });
    }

    [Fact]
    public void Remove_OrMarkUnidentified_OnAnEntryThatIsOnlyBeingAdded_DropsTheAdd()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 1));
        draft.AddToSlot(3, Form(), Id(50));
        var beyond = draft.AddBeyondRegulatedSlots(Form(), Id(51));

        draft.Remove("slot:3");
        draft.MarkUnidentified(beyond);

        draft.IsEmpty.Should().BeTrue();
        draft.Rows().Should().HaveCount(6);
        ((Action)(() => draft.Remove("entry:nothing"))).Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Undo_DropsOneRowsChange_AndClear_AllOfThem()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 3));
        draft.Remove("slot:1");
        draft.MarkUnidentified("slot:2");

        draft.Undo("slot:1").Should().BeTrue();
        draft.Undo("slot:1").Should().BeFalse();
        draft.Changes.Should().ContainSingle().Which.SlotIndex.Should().Be(2);

        draft.Clear();
        draft.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Changes_ComeRegulatedSlotsFirstByIndex_ThenEntriesBeyondThem_WhateverTheOrderOfEditing()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 3, beyond: 1));

        draft.AddBeyondRegulatedSlots(Form(), Id(50));
        draft.Remove($"entry:{Id(101):D}");
        draft.Remove("slot:3");
        draft.AddToSlot(5, Form(), Id(51));
        draft.MarkUnidentified("slot:1");

        draft.Changes.Select(change => change.SlotIndex).Should().Equal(1, 3, 5, null, null);
        draft.Changes.Skip(3).Select(change => change.EntryId).Should().Equal(Id(50), Id(101));
    }

    // ── Starting from what is staged ──────────────────────────────────────────

    [Fact]
    public void ADraftOverAStagedSet_StartsWithItsChanges_AndIsDirtyOnlyOnceTheyDiffer()
    {
        OccupancyChange[] staged =
        [
            new() { Kind = OccupancyChangeKind.Remove, SlotIndex = 2 },
            new() { Kind = OccupancyChangeKind.MarkUnidentified, EntryId = Id(101) },
        ];
        var draft = new OccupancyChangeDraft(View(regulated: 3, beyond: 1, staged: staged));

        draft.Changes.Should().Equal(staged);
        draft.IsDirty.Should().BeFalse();
        draft.Rows().Count(row => row.Change != null).Should().Be(2);

        draft.Undo("slot:2");
        draft.IsDirty.Should().BeTrue();
        draft.Remove("slot:2");
        draft.IsDirty.Should().BeFalse("the pending changes are the staged ones again");
    }

    [Fact]
    public void AStagedChangeNamingARegulatedEntryByItsId_LandsOnThatEntrysSlotRow()
    {
        var draft = new OccupancyChangeDraft(View(regulated: 3, staged: new OccupancyChange { Kind = OccupancyChangeKind.Remove, EntryId = Id(2) }));

        var row = draft.Rows().Single(r => r.Change != null);

        row.Key.Should().Be("slot:2");
        draft.Remove("slot:2");
        draft.Changes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Kind = OccupancyChangeKind.Remove, SlotIndex = (int?)2, EntryId = (Guid?)null });
    }

    [Fact]
    public void AStagedChangeForAnEntryNoLongerOnThePole_IsShownAsAnOrphanRow_ThePreviewFails_AndUndoingItRecovers()
    {
        var gone = Id(999);
        var draft = new OccupancyChangeDraft(View(regulated: 2, staged: new OccupancyChange { Kind = OccupancyChangeKind.MarkUnidentified, EntryId = gone }));

        var orphan = draft.Rows()[^1];

        orphan.Should().BeEquivalentTo(new { Key = $"entry:{gone:D}", IsOrphan = true, Current = (PoleOccupancyEntry?)null, Projected = (PoleOccupancyEntry?)null });
        draft.TryPreview(out var preview, out var errorKey).Should().BeFalse();
        preview.Should().BeNull();
        errorKey.Should().Be("occupancy.error.entryNotFound");
        draft.Rows().Take(2).Should().OnlyContain(row => row.Projected == null, "nothing can be previewed while the draft does not apply");

        draft.Undo(orphan.Key);
        draft.TryPreview(out preview, out errorKey).Should().BeTrue();
        preview!.OccupiedCount.Should().Be(2);
    }

    // ── Preview errors ────────────────────────────────────────────────────────

    [Fact]
    public void TryPreview_NamesWhyTheChangesDoNotApply()
    {
        var transition = new OccupancyChangeDraft(View(regulated: 1));
        transition.SetEntry("slot:1", Form(PoleAttachmentOccupantStatus.ActiveReservation));
        var occupied = new OccupancyChangeDraft(View(regulated: 2, staged: new OccupancyChange
        {
            Kind = OccupancyChangeKind.Add, SlotIndex = 2, EntryId = Id(50), Status = PoleAttachmentOccupantStatus.AuthorizedContracted,
        }));
        var invalid = new OccupancyChangeDraft(View(regulated: 1));
        invalid.AddBeyondRegulatedSlots(Form(PoleAttachmentOccupantStatus.ActiveReservation), Id(50));
        var outOfRange = new OccupancyChangeDraft(View(regulated: 1, staged: new OccupancyChange
        {
            Kind = OccupancyChangeKind.Add, SlotIndex = 3, EntryId = Id(50), Status = PoleAttachmentOccupantStatus.AuthorizedContracted, CableDiameterMm = 1.23456m,
        }));

        ErrorOf(transition).Should().Be("occupancy.error.transition");
        ErrorOf(occupied).Should().Be("occupancy.error.slotOccupied");
        ErrorOf(invalid).Should().Be("occupancy.error.invalid");
        ErrorOf(outOfRange).Should().Be("occupancy.error.outOfRange");
        OccupancyEditorMessages.MessageKeys.Should().Contain([ErrorOf(transition)!, ErrorOf(occupied)!, ErrorOf(invalid)!, ErrorOf(outOfRange)!]);

        static string? ErrorOf(OccupancyChangeDraft draft)
        {
            draft.TryPreview(out var preview, out var errorKey).Should().BeFalse();
            preview.Should().BeNull();
            return errorKey;
        }
    }

    [Fact]
    public void WhatTheDraftPreviews_IsWhatTheHostsApplierComputes()
    {
        var view = View(regulated: 6, beyond: 1);
        var draft = new OccupancyChangeDraft(view);
        draft.MarkUnidentified("slot:3");
        draft.Remove("slot:6");
        draft.AddBeyondRegulatedSlots(Form(), Id(50));

        draft.TryPreview(out var preview, out _).Should().BeTrue();

        var baseline = PoleOccupancy.Create(Pole, Owner, view.Current.Entries, view.CurrentVersion, default);
        preview.Should().BeEquivalentTo(OccupancyChangeApplier.Apply(baseline, Pole, Owner, draft.Changes));
        preview.Should().BeEquivalentTo(new { OccupiedCount = 7, IsOverCapacity = true, HasUnidentifiedOccupant = true });
    }

    [Fact]
    public void Constructor_NullView_ThrowsArgumentNullException()
    {
        var act = () => new OccupancyChangeDraft(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
