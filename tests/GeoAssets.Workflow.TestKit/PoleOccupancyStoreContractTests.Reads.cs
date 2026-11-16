using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

public abstract partial class PoleOccupancyStoreContractTests
{
    private static Task<PoleOccupancy> SeedAsync(PoleOccupancyStoreContractHarness harness, string poleFeatureId, params Func<PoleOccupancyEntryBuilder, PoleOccupancyEntryBuilder>[] entries) =>
        harness.Writer.ReplaceEntriesAsync(
            poleFeatureId,
            PoleOccupancy.UnsavedVersion,
            entries.Select(configure => configure(Entry(harness, poleFeatureId)).Build()).ToList(),
            CancellationToken.None);

    private static Func<PoleOccupancyEntryBuilder, PoleOccupancyEntryBuilder> Slot(
        int slotIndex, Guid? occupant, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) =>
        entry => entry.OnSlot(slotIndex).OccupiedBy(occupant).WithStatus(status);

    private static Func<PoleOccupancyEntryBuilder, PoleOccupancyEntryBuilder> Beyond(
        Guid? occupant, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) =>
        entry => entry.BeyondRegulatedSlots().OccupiedBy(occupant).WithStatus(status);

    // ── GetForPoleAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetForPoleAsync_PoleNotOnFile_ReturnsNull()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-1", Slot(1, OccupantA));

        (await harness.Reader.GetForPoleAsync("pole-2", CancellationToken.None)).Should().BeNull();
        (await harness.Reader.GetForPoleAsync("POLE-1", CancellationToken.None)).Should().BeNull("pole feature ids are compared ordinally");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetForPoleAsync_BlankPole_ThrowsArgumentException(string? poleFeatureId)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Reader.GetForPoleAsync(poleFeatureId!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── GetForPolesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetForPolesAsync_ReturnsThePolesOnFileInOrdinalOrder_IgnoringUnknownNullAndRepeatedIds()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-b", Slot(1, OccupantA));
        await SeedAsync(harness, "pole-a", Slot(1, OccupantA), Beyond(OccupantB));
        await SeedAsync(harness, "Pole-C", Slot(2, OccupantC));
        await SeedAsync(harness, "pole-not-asked", Slot(1, OccupantA));

        var poles = await harness.Reader.GetForPolesAsync(["pole-b", "missing", null!, "pole-a", "Pole-C", "pole-b"], CancellationToken.None);

        poles.Select(pole => pole.PoleFeatureId).Should().Equal("Pole-C", "pole-a", "pole-b");
        poles[1].Entries.Should().HaveCount(2, "each pole comes with all of its entries");
        poles[1].OverCapacityEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetForPolesAsync_EmptyInput_ReturnsEmpty_AndNullInputThrows()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-1", Slot(1, OccupantA));

        (await harness.Reader.GetForPolesAsync([], CancellationToken.None)).Should().BeEmpty();
        var act = () => harness.Reader.GetForPolesAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── FindContractedOccupantsAsync ──────────────────────────────────────────

    [Fact]
    public async Task FindContractedOccupantsAsync_EmptyInput_ReturnsEmpty_AndNullInputThrows()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-1", Slot(1, OccupantA));

        (await harness.Reader.FindContractedOccupantsAsync([], CancellationToken.None)).Should().BeEmpty();
        var act = () => harness.Reader.FindContractedOccupantsAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FindContractedOccupantsAsync_GroupsThePolesByOccupantOrganization_InOrder_WithEachPoleOnce()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-2", Slot(1, OccupantB), Slot(2, OccupantA), Slot(3, OccupantA));
        await SeedAsync(harness, "pole-1", Slot(1, OccupantA));
        await SeedAsync(harness, "pole-3", Slot(1, OccupantB));
        await SeedAsync(harness, "pole-not-asked", Slot(1, OccupantC));

        var presences = await harness.Reader.FindContractedOccupantsAsync(["pole-3", "pole-2", "pole-1", "pole-2", "missing", null!], CancellationToken.None);

        presences.Should().HaveCount(2);
        presences[0].OccupantOrganizationId.Should().Be(OccupantA);
        presences[0].PoleFeatureIds.Should().Equal("pole-1", "pole-2");
        presences[1].OccupantOrganizationId.Should().Be(OccupantB);
        presences[1].PoleFeatureIds.Should().Equal("pole-2", "pole-3");
    }

    [Fact]
    public async Task FindContractedOccupantsAsync_ExcludesUnidentifiedEntriesAndEveryStatusButContracted()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(
            harness,
            "pole-1",
            Slot(1, null),
            Slot(2, OccupantA, PoleAttachmentOccupantStatus.ActiveReservation),
            Slot(3, OccupantA, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
            Slot(4, OccupantA, PoleAttachmentOccupantStatus.ClandestineUnauthorized),
            Slot(5, OccupantB),
            Beyond(OccupantC, PoleAttachmentOccupantStatus.ClandestineUnauthorized),
            Beyond(null));

        var presences = await harness.Reader.FindContractedOccupantsAsync(["pole-1"], CancellationToken.None);

        presences.Should().ContainSingle().Which.Should().BeEquivalentTo(new PoleOccupantPresence(OccupantB, ["pole-1"]));
    }

    [Fact]
    public async Task FindContractedOccupantsAsync_IncludesContractedEntriesBeyondTheRegulatedSlots()
    {
        await using var harness = await CreateHarnessAsync();
        var entries = Entries(harness, "pole-1", regulated: 6);
        entries.Add(Entry(harness, "pole-1").BeyondRegulatedSlots().OccupiedBy(OccupantC).Build());
        await harness.Writer.ReplaceEntriesAsync("pole-1", 0, entries, CancellationToken.None);

        var presences = await harness.Reader.FindContractedOccupantsAsync(["pole-1"], CancellationToken.None);

        presences.Select(presence => presence.OccupantOrganizationId).Should().Equal(OccupantA, OccupantC);
        presences[1].PoleFeatureIds.Should().Equal("pole-1");
    }

    [Fact]
    public async Task FindContractedOccupantsAsync_NoContractedOccupantOnThePolesAsked_ReturnsEmpty()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-1", Slot(1, null, PoleAttachmentOccupantStatus.ClandestineUnauthorized));
        await SeedAsync(harness, "pole-2");

        (await harness.Reader.FindContractedOccupantsAsync(["pole-1", "pole-2", "missing"], CancellationToken.None)).Should().BeEmpty();
    }

    // ── GetPageAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPageAsync_PagesThroughEveryPoleOnceInOrdinalOrder_AndAPagePastTheEndIsEmptyWithTheTrueTotal()
    {
        await using var harness = await CreateHarnessAsync();
        foreach (var pole in new[] { "pole-c", "pole-a", "Pole-B", "pole-e", "pole-d" })
            await SeedAsync(harness, pole, Slot(1, OccupantA), Beyond(OccupantB));

        var pages = new List<Core.Models.PagedResult<PoleOccupancy>>();
        for (var page = 1; page <= 4; page++)
            pages.Add(await harness.Reader.GetPageAsync(new PoleOccupancyQuery(page, pageSize: 2), CancellationToken.None));

        pages.Select(page => page.TotalCount).Should().AllBeEquivalentTo(5);
        pages[0].Items.Select(pole => pole.PoleFeatureId).Should().Equal("Pole-B", "pole-a");
        pages[1].Items.Select(pole => pole.PoleFeatureId).Should().Equal("pole-c", "pole-d");
        pages[2].Items.Select(pole => pole.PoleFeatureId).Should().Equal("pole-e");
        pages[3].Items.Should().BeEmpty();
        pages[0].Items[0].Entries.Should().HaveCount(2, "a listed pole comes with all of its entries");
    }

    [Fact]
    public async Task GetPageAsync_FilteredByOccupant_ListsOnlyThePolesThatOrganizationIsOn_InAnyStatusOrPosition()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-1", Slot(1, OccupantA));
        await SeedAsync(harness, "pole-2", Slot(1, OccupantA), Beyond(OccupantB, PoleAttachmentOccupantStatus.ClandestineUnauthorized));
        await SeedAsync(harness, "pole-3", Slot(1, OccupantB, PoleAttachmentOccupantStatus.ActiveReservation));
        await SeedAsync(harness, "pole-4", Slot(1, null));

        var page = await harness.Reader.GetPageAsync(new PoleOccupancyQuery(occupantOrganizationId: OccupantB), CancellationToken.None);
        var nobody = await harness.Reader.GetPageAsync(new PoleOccupancyQuery(occupantOrganizationId: OccupantC), CancellationToken.None);

        page.TotalCount.Should().Be(2);
        page.Items.Select(pole => pole.PoleFeatureId).Should().Equal("pole-2", "pole-3");
        page.Items[0].Entries.Should().HaveCount(2, "the filter chooses poles, it does not trim their entries");
        nobody.TotalCount.Should().Be(0);
        nobody.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAsync_EmptyStore_ReturnsNoItemsAndZero_AndNullQueryThrows()
    {
        await using var harness = await CreateHarnessAsync();

        var page = await harness.Reader.GetPageAsync(new PoleOccupancyQuery(), CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        var act = () => harness.Reader.GetPageAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Cancellation and tenancy ──────────────────────────────────────────────

    [Fact]
    public async Task EveryRead_TokenAlreadyCancelled_ThrowsOperationCanceled()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-1", Slot(1, OccupantA));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var reads = new Func<Task>[]
        {
            () => harness.Reader.GetForPoleAsync("pole-1", cancelled.Token),
            () => harness.Reader.GetForPolesAsync(["pole-1"], cancelled.Token),
            () => harness.Reader.FindContractedOccupantsAsync(["pole-1"], cancelled.Token),
            () => harness.Reader.GetPageAsync(new PoleOccupancyQuery(), cancelled.Token),
        };

        foreach (var read in reads)
            await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EveryRead_AsAnotherOrganization_SeesNothingOfTheOwners_EvenWhenItIsAnOccupantThere()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAsync(harness, "pole-1", Slot(1, OccupantA), Slot(2, OtherOrganizationId), Beyond(OccupantB));
        var occupant = harness.AsOrganization(OccupantA).Reader;
        var stranger = harness.AsOrganization(OtherOrganizationId).Reader;

        foreach (var reader in new[] { occupant, stranger })
        {
            (await reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
            (await reader.GetForPolesAsync(["pole-1"], CancellationToken.None)).Should().BeEmpty();
            (await reader.FindContractedOccupantsAsync(["pole-1"], CancellationToken.None)).Should().BeEmpty();
            var page = await reader.GetPageAsync(new PoleOccupancyQuery(), CancellationToken.None);
            page.TotalCount.Should().Be(0);
            page.Items.Should().BeEmpty();
            (await reader.GetPageAsync(new PoleOccupancyQuery(occupantOrganizationId: OccupantA), CancellationToken.None)).TotalCount.Should().Be(0);
        }

        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().HaveCount(3);
    }
}
