using FluentAssertions;
using GeoAssets.Workflow.Registry;
using Xunit;
using static GeoAssets.Workflow.Tests.Registry.RegistryTestData;

namespace GeoAssets.Workflow.Tests.Registry;

public sealed class InMemoryOrderTypeDescriptorRegistryTests
{
    private static InMemoryOrderTypeDescriptorRegistry Empty() => new([]);

    [Fact]
    public void Constructor_NullSources_Throws()
    {
        var act = () => new InMemoryOrderTypeDescriptorRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_UpsertsEverySource()
    {
        var registry = new InMemoryOrderTypeDescriptorRegistry(
        [
            new OrderTypeDescriptorSource("vertical-b", [Descriptor("vertical-b", "b-1")]),
            new OrderTypeDescriptorSource("vertical-a", [Descriptor("vertical-a", "a-1")]),
        ]);

        registry.GetAll().Select(d => d.OrderTypeId).Should().Equal("a-1", "b-1");
    }

    [Fact]
    public void Constructor_InvalidSource_ThrowsArgumentException()
    {
        var act = () => new InMemoryOrderTypeDescriptorRegistry(
            [new OrderTypeDescriptorSource("vertical-a", [Descriptor("other-vertical")])]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Upsert_NewVertical_ReturnsTrueAndPublishesIt()
    {
        var registry = Empty();

        var changed = registry.Upsert("vertical-a", [Descriptor()]);

        changed.Should().BeTrue();
        registry.GetAll().Should().ContainSingle().Which.OrderTypeId.Should().Be("pole-work");
    }

    [Fact]
    public void Upsert_SamePayloadTwice_SecondReturnsFalseAndNothingChanges()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor(actions: Action())]).Should().BeTrue();
        var before = registry.GetAll();

        var second = registry.Upsert("vertical-a", [Descriptor(actions: Action())]);

        second.Should().BeFalse();
        registry.GetAll().Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Upsert_SameSetInAnotherOrder_ReturnsFalse()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor(orderTypeId: "a-1"), Descriptor(orderTypeId: "a-2")]);

        var changed = registry.Upsert("vertical-a", [Descriptor(orderTypeId: "a-2"), Descriptor(orderTypeId: "a-1")]);

        changed.Should().BeFalse();
    }

    [Fact]
    public void Upsert_ChangedDescriptor_ReturnsTrueAndReplacesIt()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor()]);

        var changed = registry.Upsert("vertical-a", [Descriptor(actions: Action())]);

        changed.Should().BeTrue();
        registry.GetAll().Should().ContainSingle().Which.FeatureActions.Should().ContainSingle();
    }

    [Fact]
    public void Upsert_SetWithoutAnEntry_RemovesTheEntry()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor(orderTypeId: "a-1"), Descriptor(orderTypeId: "a-2")]);

        var changed = registry.Upsert("vertical-a", [Descriptor(orderTypeId: "a-2")]);

        changed.Should().BeTrue();
        registry.GetAll().Select(d => d.OrderTypeId).Should().Equal("a-2");
    }

    [Fact]
    public void Upsert_ReplacesOnlyThatVerticalsSet()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor("vertical-a", "a-1")]);
        registry.Upsert("vertical-b", [Descriptor("vertical-b", "b-1")]);

        registry.Upsert("vertical-a", [Descriptor("vertical-a", "a-2")]);

        registry.GetAll().Select(d => d.OrderTypeId).Should().Equal("a-2", "b-1");
    }

    [Fact]
    public void Upsert_EmptySet_RemovesTheVertical()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor()]);

        var changed = registry.Upsert("vertical-a", []);

        changed.Should().BeTrue();
        registry.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Upsert_EmptySetForUnknownVertical_ReturnsFalse()
    {
        Empty().Upsert("vertical-a", []).Should().BeFalse();
    }

    [Fact]
    public void Upsert_InvalidSet_ThrowsArgumentExceptionAndKeepsTheRegistry()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor()]);

        var act = () => registry.Upsert("vertical-a", [Descriptor("vertical-b")]);

        act.Should().Throw<ArgumentException>().WithMessage("*vertical-b*");
        registry.GetAll().Should().ContainSingle().Which.VerticalId.Should().Be("vertical-a");
    }

    [Fact]
    public void Upsert_BlankVerticalId_ThrowsArgumentException()
    {
        var act = () => Empty().Upsert(" ", [Descriptor()]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Upsert_NullSet_ThrowsArgumentException()
    {
        var act = () => Empty().Upsert("vertical-a", null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetAll_OrdersByVerticalIdThenOrderTypeId()
    {
        var registry = Empty();
        registry.Upsert("vertical-b", [Descriptor("vertical-b", "z"), Descriptor("vertical-b", "a")]);
        registry.Upsert("vertical-a", [Descriptor("vertical-a", "m")]);

        registry.GetAll().Select(d => $"{d.VerticalId}/{d.OrderTypeId}")
            .Should().Equal("vertical-a/m", "vertical-b/a", "vertical-b/z");
    }

    [Fact]
    public void GetAll_EmptyRegistry_ReturnsEmpty()
    {
        Empty().GetAll().Should().BeEmpty();
    }

    [Fact]
    public void FeatureActionsFor_ReturnsOnlyActionsWhoseRoleIsAmongTheRoles()
    {
        var registry = Empty();
        registry.Upsert("vertical-a",
        [
            Descriptor("vertical-a", "a-1", Action("occupiable-support", "a-1"), Action("isp:pole", "a-1")),
            Descriptor("vertical-a", "a-2", Action("manhole", "a-2")),
        ]);

        var actions = registry.FeatureActionsFor(["occupiable-support", "manhole"]);

        actions.Select(a => a.Role).Should().Equal("occupiable-support", "manhole");
    }

    [Fact]
    public void FeatureActionsFor_NoMatchingRole_ReturnsEmpty()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor(actions: Action())]);

        registry.FeatureActionsFor(["manhole"]).Should().BeEmpty();
        registry.FeatureActionsFor([]).Should().BeEmpty();
    }

    [Fact]
    public void FeatureActionsFor_MatchesRolesOrdinally()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor(actions: Action("occupiable-support"))]);

        registry.FeatureActionsFor(["Occupiable-Support"]).Should().BeEmpty();
    }

    [Fact]
    public void FeatureActionsFor_SpansVerticals()
    {
        var registry = Empty();
        registry.Upsert("vertical-a", [Descriptor("vertical-a", "a-1", Action("pole", "a-1"))]);
        registry.Upsert("vertical-b", [Descriptor("vertical-b", "b-1", Action("pole", "b-1"))]);

        registry.FeatureActionsFor(["pole"]).Select(a => a.OrderTypeId).Should().Equal("a-1", "b-1");
    }

    [Fact]
    public void FeatureActionsFor_NullRoles_Throws()
    {
        var act = () => Empty().FeatureActionsFor(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
