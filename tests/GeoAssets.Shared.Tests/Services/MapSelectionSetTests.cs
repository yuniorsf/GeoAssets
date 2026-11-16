using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>Guards and branches MAP-G1 and MAP-B1..B5 of the Uso Mútuo plan (XD01-310).</summary>
public class MapSelectionSetTests
{
    private static (MapSelectionSet Set, List<MapSelectionChange> Events) Sut(int maxCount = MapSelectionSet.DefaultMaxCount, params string[] initial)
    {
        var set = new MapSelectionSet(maxCount);
        set.Add(initial);
        var events = new List<MapSelectionChange>();
        set.Changed += events.Add;
        return (set, events);
    }

    [Fact]
    public void NewSet_IsEmpty_WithTheDefaultCapOfTenThousand()
    {
        var set = new MapSelectionSet();

        set.Ids.Should().BeEmpty();
        set.MaxCount.Should().Be(10_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveMaxCount_Throws(int maxCount)
    {
        var act = () => new MapSelectionSet(maxCount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── MAP-G1 ───────────────────────────────────────────────────────────────

    [Fact]
    public void MAP_G1_Add_Null_ThrowsArgumentNullException()
    {
        var (set, events) = Sut();

        var act = () => set.Add(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ids");
        events.Should().BeEmpty();
    }

    [Fact]
    public void MAP_G1_Remove_Null_ThrowsArgumentNullException()
    {
        var (set, _) = Sut();

        var act = () => set.Remove(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ids");
    }

    [Fact]
    public void MAP_G1_Replace_Null_ThrowsArgumentNullException_AndKeepsTheSelection()
    {
        var (set, events) = Sut(initial: "a");

        var act = () => set.Replace(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ids");
        set.Ids.Should().BeEquivalentTo("a");
        events.Should().BeEmpty();
    }

    [Fact]
    public void MAP_G1_Toggle_Null_ThrowsArgumentNullException()
    {
        var (set, _) = Sut();

        var act = () => set.Toggle(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("id");
    }

    [Fact]
    public void MAP_G1_Add_BlankIds_AreIgnored()
    {
        var (set, events) = Sut();

        var change = set.Add(["", "  ", null!, "a", "\t"]);

        change.Added.Should().Equal("a");
        set.Ids.Should().BeEquivalentTo("a");
        events.Should().ContainSingle();
    }

    [Fact]
    public void MAP_G1_Add_OnlyBlankIds_ChangesNothingAndRaisesNoEvent()
    {
        var (set, events) = Sut();

        var change = set.Add(["", " "]);

        change.IsEmpty.Should().BeTrue();
        events.Should().BeEmpty();
    }

    [Fact]
    public void MAP_G1_RemoveReplaceAndToggle_IgnoreBlankIds()
    {
        var (set, events) = Sut(initial: "a");

        set.Remove(["", " "]).IsEmpty.Should().BeTrue();
        set.Toggle(" ").IsEmpty.Should().BeTrue();
        set.Replace(["a", "", " "]).IsEmpty.Should().BeTrue();

        set.Ids.Should().BeEquivalentTo("a");
        events.Should().BeEmpty();
    }

    // ── MAP-B1 ───────────────────────────────────────────────────────────────

    [Fact]
    public void MAP_B1_Add_IdsAlreadyPresent_RaisesNoChanged()
    {
        var (set, events) = Sut(initial: ["a", "b"]);

        var change = set.Add(["b", "a"]);

        change.IsEmpty.Should().BeTrue();
        events.Should().BeEmpty();
    }

    [Fact]
    public void MAP_B1_Add_MixOfNewAndPresent_RaisesChangedOnce_WithOnlyTheNewIdsInOrder()
    {
        var (set, events) = Sut(initial: "a");

        var change = set.Add(["c", "a", "b", "c"]);

        var raised = events.Should().ContainSingle().Subject;
        raised.Should().BeSameAs(change);
        raised.Added.Should().Equal("c", "b");
        raised.Removed.Should().BeEmpty();
        raised.Overflow.Should().Be(0);
        set.Ids.Should().BeEquivalentTo("a", "b", "c");
    }

    [Fact]
    public void MAP_B1_Ids_AreComparedOrdinally()
    {
        var (set, _) = Sut(initial: "pole-1");

        set.Add(["POLE-1"]).Added.Should().Equal("POLE-1");
    }

    [Fact]
    public void Remove_PresentAndAbsentIds_RaisesChangedOnce_WithOnlyThePresentOnes()
    {
        var (set, events) = Sut(initial: ["a", "b"]);

        var change = set.Remove(["b", "x", "b"]);

        events.Should().ContainSingle().Which.Should().BeSameAs(change);
        change.Removed.Should().Equal("b");
        change.Added.Should().BeEmpty();
        set.Ids.Should().BeEquivalentTo("a");
    }

    [Fact]
    public void Remove_OnlyAbsentIds_RaisesNoChanged()
    {
        var (set, events) = Sut(initial: "a");

        set.Remove(["x"]).IsEmpty.Should().BeTrue();

        events.Should().BeEmpty();
    }

    // ── MAP-B2 ───────────────────────────────────────────────────────────────

    [Fact]
    public void MAP_B2_Toggle_AbsentId_AddsIt_AndRaisesChangedOnce()
    {
        var (set, events) = Sut();

        var change = set.Toggle("a");

        change.Added.Should().Equal("a");
        change.Removed.Should().BeEmpty();
        events.Should().ContainSingle();
        set.Ids.Should().BeEquivalentTo("a");
    }

    [Fact]
    public void MAP_B2_Toggle_PresentId_RemovesIt_AndRaisesChangedOnce()
    {
        var (set, events) = Sut(initial: "a");

        var change = set.Toggle("a");

        change.Removed.Should().Equal("a");
        change.Added.Should().BeEmpty();
        events.Should().ContainSingle();
        set.Ids.Should().BeEmpty();
    }

    // ── MAP-B3 ───────────────────────────────────────────────────────────────

    [Fact]
    public void MAP_B3_Replace_ComputesTheDelta_AndRaisesChangedOnce()
    {
        var (set, events) = Sut(initial: ["a", "b", "c"]);

        var change = set.Replace(["c", "d", "e"]);

        events.Should().ContainSingle().Which.Should().BeSameAs(change);
        change.Added.Should().Equal("d", "e");
        change.Removed.Should().BeEquivalentTo("a", "b");
        change.Overflow.Should().Be(0);
        set.Ids.Should().BeEquivalentTo("c", "d", "e");
    }

    [Fact]
    public void MAP_B3_Replace_IdenticalSet_InAnyOrderWithDuplicates_RaisesNoEvent()
    {
        var (set, events) = Sut(initial: ["a", "b"]);

        var change = set.Replace(["b", "a", "b"]);

        change.IsEmpty.Should().BeTrue();
        events.Should().BeEmpty();
    }

    [Fact]
    public void MAP_B3_Replace_WithEmpty_RemovesEverything()
    {
        var (set, events) = Sut(initial: ["a", "b"]);

        var change = set.Replace([]);

        change.Removed.Should().BeEquivalentTo("a", "b");
        events.Should().ContainSingle();
        set.Ids.Should().BeEmpty();
    }

    // ── MAP-B4 ───────────────────────────────────────────────────────────────

    [Fact]
    public void MAP_B4_Clear_OnEmptySet_RaisesNoEvent()
    {
        var (set, events) = Sut();

        set.Clear().IsEmpty.Should().BeTrue();

        events.Should().BeEmpty();
    }

    [Fact]
    public void MAP_B4_Clear_OnNonEmptySet_RemovesAll_AndRaisesChangedOnce()
    {
        var (set, events) = Sut(initial: ["a", "b"]);

        var change = set.Clear();

        change.Removed.Should().BeEquivalentTo("a", "b");
        events.Should().ContainSingle();
        set.Ids.Should().BeEmpty();
    }

    // ── MAP-B5 ───────────────────────────────────────────────────────────────

    [Fact]
    public void MAP_B5_Add_BeyondMaxCount_AddsUpToCapacity_AndReportsTheOverflow()
    {
        var (set, events) = Sut(maxCount: 3, initial: "a");

        var change = set.Add(["b", "c", "d", "e", "a"]);

        change.Added.Should().Equal("b", "c");
        change.Overflow.Should().Be(2);
        events.Should().ContainSingle().Which.Overflow.Should().Be(2);
        set.Ids.Should().BeEquivalentTo("a", "b", "c");
    }

    [Fact]
    public void MAP_B5_Add_ToAFullSet_AddsNothing_ButStillRaisesChangedWithTheOverflow()
    {
        var (set, events) = Sut(maxCount: 2, initial: ["a", "b"]);

        var change = set.Add(["c"]);

        change.Added.Should().BeEmpty();
        change.Overflow.Should().Be(1);
        events.Should().ContainSingle().Which.Overflow.Should().Be(1);
        set.Ids.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void MAP_B5_Toggle_NewIdOnAFullSet_ReportsOverflow_AndTogglingAMemberStillRemoves()
    {
        var (set, _) = Sut(maxCount: 1, initial: "a");

        set.Toggle("b").Overflow.Should().Be(1);
        set.Ids.Should().BeEquivalentTo("a");

        set.Toggle("a").Removed.Should().Equal("a");
    }

    [Fact]
    public void MAP_B5_Replace_WithMoreThanMaxCount_KeepsTheFirstMaxCount_AndReportsTheOverflow()
    {
        var (set, events) = Sut(maxCount: 2, initial: ["a", "z"]);

        var change = set.Replace(["a", "b", "c", "d"]);

        change.Added.Should().Equal("b");
        change.Removed.Should().Equal("z");
        change.Overflow.Should().Be(2);
        events.Should().ContainSingle();
        set.Ids.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void MAP_B5_DefaultCap_HoldsTenThousand_AndReportsTheRest()
    {
        var set = new MapSelectionSet();

        var change = set.Add([.. Enumerable.Range(0, 10_050).Select(i => $"f{i}")]);

        change.Added.Should().HaveCount(10_000);
        change.Overflow.Should().Be(50);
        set.Ids.Should().HaveCount(10_000);
    }
}
