using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Liskov Substitution Principle behavioural contract for every
/// <see cref="IPoleOccupancyReader"/> + <see cref="IPoleOccupancyWriter"/> adapter (XD01-314).
/// Each fact pins one sentence of the member documentation on those two interfaces, so an
/// adapter that passes here can replace any other.
///
/// Subclass it, implement <see cref="CreateHarnessAsync"/>, and declare at least one
/// <c>[Fact]</c> of your own on the subclass — xUnit only discovers <c>public</c> classes, and a
/// class whose tests are all inherited is the shape analyzers push back to <c>internal</c>.
///
/// The facts are split over two files: writing (here) and reading (<c>.Reads.cs</c>).
/// </summary>
public abstract partial class PoleOccupancyStoreContractTests
{
    /// <summary>An organization that is never the harness's own.</summary>
    protected static readonly Guid OtherOrganizationId = new("0b0b0b0b-0000-4000-8000-000000000002");

    protected static readonly Guid OccupantA = new("0c0c0c0c-0000-4000-8000-00000000000a");
    protected static readonly Guid OccupantB = new("0c0c0c0c-0000-4000-8000-00000000000b");
    protected static readonly Guid OccupantC = new("0c0c0c0c-0000-4000-8000-00000000000c");

    /// <summary>A fresh, empty store scoped to one organization.</summary>
    protected abstract Task<PoleOccupancyStoreContractHarness> CreateHarnessAsync();

    // ── Arrange helpers ───────────────────────────────────────────────────────

    private static PoleOccupancyEntryBuilder Entry(PoleOccupancyStoreContractHarness harness, string poleFeatureId) =>
        new PoleOccupancyEntryBuilder().ForPole(poleFeatureId).OwnedBy(harness.OrganizationId).OccupiedBy(OccupantA);

    /// <summary><paramref name="regulated"/> contracted entries on slots 1.., then <paramref name="beyond"/> entries without a slot.</summary>
    private static List<PoleOccupancyEntry> Entries(PoleOccupancyStoreContractHarness harness, string poleFeatureId, int regulated, int beyond = 0)
    {
        var entries = Enumerable.Range(1, regulated)
            .Select(slotIndex => Entry(harness, poleFeatureId).OnSlot(slotIndex).Build())
            .ToList();
        entries.AddRange(Enumerable.Range(0, beyond).Select(_ => Entry(harness, poleFeatureId).BeyondRegulatedSlots().Build()));
        return entries;
    }

    private static Task<PoleOccupancy> WriteAsync(
        PoleOccupancyStoreContractHarness harness, string poleFeatureId, int expectedVersion, params PoleOccupancyEntry[] entries) =>
        harness.Writer.ReplaceEntriesAsync(poleFeatureId, expectedVersion, entries, CancellationToken.None);

    // ── ReplaceEntriesAsync: what is stored ───────────────────────────────────

    [Fact]
    public async Task ReplaceEntriesAsync_FirstWrite_StoresVersionOneStampedWithTheClock_AndReturnsThePoleAsStored()
    {
        await using var harness = await CreateHarnessAsync();
        var entry = Entry(harness, "pole-1").OnSlot(2).Build();

        var written = await WriteAsync(harness, "pole-1", PoleOccupancy.UnsavedVersion, entry);

        written.PoleFeatureId.Should().Be("pole-1");
        written.PoleOwnerOrganizationId.Should().Be(harness.OrganizationId);
        written.Version.Should().Be(1);
        written.UpdatedAt.Should().Be(harness.Clock.GetUtcNow());
        written.Entries.Should().Equal(entry);
        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored.Should().BeEquivalentTo(written);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_EveryFieldOfEveryStatus_RoundTripsExactly()
    {
        await using var harness = await CreateHarnessAsync();
        var entries = new[]
        {
            Entry(harness, "pole-1").OnSlot(1).WithStatus(PoleAttachmentOccupantStatus.ActiveReservation).Unidentified()
                .WithCableDiameterMm(0m).Build(),
            Entry(harness, "pole-1").OnSlot(2).WithStatus(PoleAttachmentOccupantStatus.AuthorizedContracted).OccupiedBy(OccupantA)
                .WithOperatorCnpj("12.345.678/0001-90").WithAnatelLicenseNumber("SCM-0042").WithCableDiameterMm(12.345m)
                .WithCableLoadDaN(150.125m).WithAttachedCableFeatureId("cable-7").Build(),
            Entry(harness, "pole-1").OnSlot(3).WithStatus(PoleAttachmentOccupantStatus.AbandonedDeadDrop).OccupiedBy(OccupantB)
                .WithCableDiameterMm(PoleOccupancyEntry.MaxCableDiameterMm).WithCableLoadDaN(PoleOccupancyEntry.MaxCableLoadDaN).Build(),
            Entry(harness, "pole-1").OnSlot(6).WithStatus(PoleAttachmentOccupantStatus.ClandestineUnauthorized).Unidentified()
                .WithOperatorCnpj("98765432000110").Build(),
            Entry(harness, "pole-1").BeyondRegulatedSlots().WithStatus(PoleAttachmentOccupantStatus.ClandestineUnauthorized).Unidentified()
                .WithId(new Guid("00000000-0000-4000-8000-000000000002")).Build(),
            Entry(harness, "pole-1").BeyondRegulatedSlots().WithStatus(PoleAttachmentOccupantStatus.AuthorizedContracted).OccupiedBy(OccupantC)
                .WithId(new Guid("00000000-0000-4000-8000-000000000001")).WithCableDiameterMm(9.5m).Build(),
        };

        await WriteAsync(harness, "pole-1", 0, entries[4], entries[3], entries[0], entries[5], entries[2], entries[1]);

        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.Entries.Should().Equal(
            [entries[0], entries[1], entries[2], entries[3], entries[5], entries[4]],
            "regulated entries come by slot index, then the over-capacity ones by the text of their id");
        stored.RegulatedEntries.Should().HaveCount(4);
        stored.OverCapacityEntries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(9, 3)]
    [InlineData(40, 34)]
    public async Task ReplaceEntriesAsync_MoreOccupantsThanRegulatedSlots_StoresEveryOneOfThem_AndThePoleIsOverCapacity(int occupants, int beyond)
    {
        await using var harness = await CreateHarnessAsync();
        var entries = Entries(harness, "pole-1", regulated: PoleAttachmentSlotMatrix.MaxSlots, beyond);

        var written = await harness.Writer.ReplaceEntriesAsync("pole-1", 0, entries, CancellationToken.None);

        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.Entries.Should().HaveCount(occupants).And.BeEquivalentTo(entries);
        stored.OverCapacityEntries.Should().HaveCount(beyond).And.OnlyContain(entry => entry.SlotIndex == null);
        stored.RegulatedEntries.Select(entry => entry.SlotIndex).Should().Equal(1, 2, 3, 4, 5, 6);
        stored.OccupiedCount.Should().Be(occupants);
        stored.IsOverCapacity.Should().BeTrue();
        stored.IsIrregular.Should().BeTrue();
        stored.HasUnidentifiedOccupant.Should().BeFalse();
        stored.Class.Should().Be(OccupancyClass.OverCapacity);
        written.Should().BeEquivalentTo(stored);
    }

    [Theory]
    [InlineData(0, OccupancyClass.None)]
    [InlineData(1, OccupancyClass.Low)]
    [InlineData(5, OccupancyClass.Low)]
    [InlineData(6, OccupancyClass.High)]
    public async Task ReplaceEntriesAsync_SixOccupantsOrFewer_IsNotOverCapacity(int occupants, OccupancyClass expectedClass)
    {
        await using var harness = await CreateHarnessAsync();

        await harness.Writer.ReplaceEntriesAsync("pole-1", 0, Entries(harness, "pole-1", regulated: occupants), CancellationToken.None);

        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.OccupiedCount.Should().Be(occupants);
        stored.IsOverCapacity.Should().BeFalse();
        stored.IsIrregular.Should().BeFalse();
        stored.OverCapacityEntries.Should().BeEmpty();
        stored.Class.Should().Be(expectedClass);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_OverCapacityWithAnUnidentifiedOccupant_KeepsBothFlags_AndClassifiesAsUnidentified()
    {
        await using var harness = await CreateHarnessAsync();
        var entries = Entries(harness, "pole-1", regulated: 6, beyond: 1);
        entries.Add(Entry(harness, "pole-1").BeyondRegulatedSlots().WithStatus(PoleAttachmentOccupantStatus.ClandestineUnauthorized).Unidentified().Build());

        await harness.Writer.ReplaceEntriesAsync("pole-1", 0, entries, CancellationToken.None);

        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.IsOverCapacity.Should().BeTrue();
        stored.HasUnidentifiedOccupant.Should().BeTrue();
        stored.Class.Should().Be(OccupancyClass.Unidentified);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_LaterWrites_ReplaceTheWholeSet_AddOneToTheVersion_AndRestampWithTheClock()
    {
        await using var harness = await CreateHarnessAsync();
        var kept = Entry(harness, "pole-1").OnSlot(1).Build();
        var dropped = Entry(harness, "pole-1").OnSlot(2).Build();
        var beyond = Entry(harness, "pole-1").BeyondRegulatedSlots().Build();
        var first = await WriteAsync(harness, "pole-1", 0, kept, dropped, beyond);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        var promoted = beyond with { SlotIndex = 2 };

        var second = await WriteAsync(harness, "pole-1", first.Version, kept, promoted);
        var third = await WriteAsync(harness, "pole-1", second.Version, kept, promoted);

        second.Version.Should().Be(2);
        second.UpdatedAt.Should().Be(first.UpdatedAt.AddMinutes(5));
        third.Version.Should().Be(3, "the version grows on every write, also when nothing changed");
        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.Entries.Should().Equal(kept, promoted);
        stored.Version.Should().Be(3);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_EmptySet_EmptiesThePoleButKeepsItOnFileWithItsVersion()
    {
        await using var harness = await CreateHarnessAsync();
        var first = await WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").Build());

        var emptied = await WriteAsync(harness, "pole-1", first.Version);

        emptied.Version.Should().Be(2);
        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.Entries.Should().BeEmpty();
        stored.Version.Should().Be(2);
        stored.Class.Should().Be(OccupancyClass.None);
        var act = () => WriteAsync(harness, "pole-1", PoleOccupancy.UnsavedVersion, Entry(harness, "pole-1").Build());
        await act.Should().ThrowAsync<OccupancyConflictException>("an emptied pole does not go back to version 0");
    }

    [Fact]
    public async Task ReplaceEntriesAsync_EmptySetOnAPoleNotOnFile_PutsItOnFileEmptyAtVersionOne()
    {
        await using var harness = await CreateHarnessAsync();

        var written = await WriteAsync(harness, "pole-1", 0);

        written.Version.Should().Be(1);
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().BeEmpty();
    }

    // ── ReplaceEntriesAsync: version ──────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ReplaceEntriesAsync_ExpectedVersionOtherThanTheStoredOne_ThrowsOccupancyConflictNamingThePole_AndWritesNothing(int staleVersion)
    {
        await using var harness = await CreateHarnessAsync();
        var first = await WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").OnSlot(1).Build());
        var current = await WriteAsync(harness, "pole-1", first.Version, [.. first.Entries, Entry(harness, "pole-1").OnSlot(2).Build()]);

        var act = () => WriteAsync(harness, "pole-1", staleVersion, Entry(harness, "pole-1").OnSlot(5).Build());

        (await act.Should().ThrowAsync<OccupancyConflictException>()).Which.PoleFeatureIds.Should().Equal("pole-1");
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeEquivalentTo(current);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_NonZeroExpectedVersionForAPoleNotOnFile_ThrowsOccupancyConflict_AndCreatesNothing()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => WriteAsync(harness, "pole-1", 1, Entry(harness, "pole-1").Build());

        await act.Should().ThrowAsync<OccupancyConflictException>();
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_StaleVersionAndInvalidEntries_ReportsTheConflictFirst()
    {
        await using var harness = await CreateHarnessAsync();
        await WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").Build());

        var act = () => WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").OnSlot(99).Build());

        await act.Should().ThrowAsync<OccupancyConflictException>();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_VersionsAreKeptPerPole()
    {
        await using var harness = await CreateHarnessAsync();
        var first = await WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").Build());
        await WriteAsync(harness, "pole-1", first.Version, Entry(harness, "pole-1").Build());

        var other = await WriteAsync(harness, "pole-2", 0, Entry(harness, "pole-2").Build());

        other.Version.Should().Be(1);
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(2);
    }

    // ── ReplaceEntriesAsync: rules ────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task ReplaceEntriesAsync_SlotIndexOutsideOneToSix_ThrowsArgumentOutOfRange_AndWritesNothing(int slotIndex)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").OnSlot(slotIndex).Build());

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_TwoEntriesOnOneRegulatedSlot_ThrowsInvalidOperation_AndKeepsWhatWasStored()
    {
        await using var harness = await CreateHarnessAsync();
        var first = await WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").OnSlot(1).Build());

        var act = () => WriteAsync(
            harness, "pole-1", first.Version, Entry(harness, "pole-1").OnSlot(3).Build(), Entry(harness, "pole-1").OnSlot(3).OccupiedBy(OccupantB).Build());

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_IllegalStatusChangeOnARegulatedSlot_ThrowsInvalidTransition_AndKeepsWhatWasStored()
    {
        await using var harness = await CreateHarnessAsync();
        var reserved = Entry(harness, "pole-1").OnSlot(4).WithStatus(PoleAttachmentOccupantStatus.ActiveReservation).Build();
        var first = await WriteAsync(harness, "pole-1", 0, reserved);

        var act = () => WriteAsync(harness, "pole-1", first.Version, reserved with { Status = PoleAttachmentOccupantStatus.AbandonedDeadDrop });

        var thrown = (await act.Should().ThrowAsync<InvalidPoleAttachmentSlotTransitionException>()).Which;
        thrown.From.Should().Be(PoleAttachmentOccupantStatus.ActiveReservation);
        thrown.To.Should().Be(PoleAttachmentOccupantStatus.AbandonedDeadDrop);
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_LegalStatusChange_AndAnyStatusOnAFreedOrOverCapacityEntry_AreAccepted()
    {
        await using var harness = await CreateHarnessAsync();
        var reserved = Entry(harness, "pole-1").OnSlot(1).WithStatus(PoleAttachmentOccupantStatus.ActiveReservation).Build();
        var clandestine = Entry(harness, "pole-1").OnSlot(2).WithStatus(PoleAttachmentOccupantStatus.ClandestineUnauthorized).Build();
        var beyond = Entry(harness, "pole-1").BeyondRegulatedSlots().WithStatus(PoleAttachmentOccupantStatus.AuthorizedContracted).Build();
        var first = await WriteAsync(harness, "pole-1", 0, reserved, clandestine, beyond);

        // Slot 1: reservation → contracted (legal). Slot 2: emptied. Over-capacity entry:
        // contracted → abandoned → (next write) contracted, which the slot graph would refuse.
        var second = await WriteAsync(
            harness, "pole-1", first.Version,
            reserved with { Status = PoleAttachmentOccupantStatus.AuthorizedContracted },
            beyond with { Status = PoleAttachmentOccupantStatus.AbandonedDeadDrop });
        var third = await WriteAsync(
            harness, "pole-1", second.Version,
            reserved with { Status = PoleAttachmentOccupantStatus.AuthorizedContracted },
            Entry(harness, "pole-1").OnSlot(2).WithStatus(PoleAttachmentOccupantStatus.AbandonedDeadDrop).Build(),
            beyond with { Status = PoleAttachmentOccupantStatus.AuthorizedContracted });

        third.Version.Should().Be(3);
        third.Entries.Select(entry => entry.Status).Should().Equal(
            PoleAttachmentOccupantStatus.AuthorizedContracted,
            PoleAttachmentOccupantStatus.AbandonedDeadDrop,
            PoleAttachmentOccupantStatus.AuthorizedContracted);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_EntryOfAnotherPoleOrAnotherOwner_ThrowsArgumentException_AndWritesNothing()
    {
        await using var harness = await CreateHarnessAsync();

        var otherPole = () => WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-2").Build());
        var otherOwner = () => WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").OwnedBy(OtherOrganizationId).Build());

        await otherPole.Should().ThrowAsync<ArgumentException>();
        await otherOwner.Should().ThrowAsync<ArgumentException>("a store only writes poles of the organization it is scoped to");
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
        (await harness.AsOrganization(OtherOrganizationId).Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_ReservationBeyondTheRegulatedSlots_ThrowsArgumentException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => WriteAsync(
            harness, "pole-1", 0, Entry(harness, "pole-1").BeyondRegulatedSlots().WithStatus(PoleAttachmentOccupantStatus.ActiveReservation).Build());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── ReplaceEntriesAsync: arguments and cancellation ───────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReplaceEntriesAsync_BlankPole_ThrowsArgumentException(string? poleFeatureId)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Writer.ReplaceEntriesAsync(poleFeatureId!, 0, [], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_NullEntriesOrNegativeVersion_Throw()
    {
        await using var harness = await CreateHarnessAsync();

        var nullEntries = () => harness.Writer.ReplaceEntriesAsync("pole-1", 0, null!, CancellationToken.None);
        var negativeVersion = () => harness.Writer.ReplaceEntriesAsync("pole-1", -1, [], CancellationToken.None);

        await nullEntries.Should().ThrowAsync<ArgumentNullException>();
        await negativeVersion.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_TokenAlreadyCancelled_ThrowsOperationCanceled_AndWritesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => harness.Writer.ReplaceEntriesAsync("pole-1", 0, [Entry(harness, "pole-1").Build()], cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
    }

    // ── Tenancy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceEntriesAsync_TwoOrganizationsWritingTheSamePoleId_EachHasItsOwnPoleAndVersion()
    {
        await using var harness = await CreateHarnessAsync();
        var other = harness.AsOrganization(OtherOrganizationId);
        var mine = await WriteAsync(harness, "pole-1", 0, Entry(harness, "pole-1").OnSlot(1).Build());
        await WriteAsync(harness, "pole-1", mine.Version, Entry(harness, "pole-1").OnSlot(1).Build());

        var theirs = await other.Writer.ReplaceEntriesAsync(
            "pole-1", 0, [Entry(harness, "pole-1").OwnedBy(OtherOrganizationId).OnSlot(6).Build()], CancellationToken.None);

        theirs.Version.Should().Be(1);
        theirs.PoleOwnerOrganizationId.Should().Be(OtherOrganizationId);
        var storedMine = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        storedMine!.Version.Should().Be(2);
        storedMine.Entries.Should().ContainSingle().Which.SlotIndex.Should().Be(1);
        (await other.Reader.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().ContainSingle().Which.SlotIndex.Should().Be(6);
    }
}
