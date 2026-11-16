using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Dispatch;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.Dispatch;

public class CrewSkillMatcherTests
{
    private static readonly GeoPoint SomePoint = new(-46.6, -23.5);

    private static CrewRosterEntry Crew(params string[] skills) =>
        new("crew-1", SomePoint, skills.ToHashSet(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    private static DispatchableOrder Order(params string[] requiredSkills) =>
        new("order-1", SomePoint, requiredSkills.ToHashSet(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal), TimeSpan.FromMinutes(30));

    [Fact]
    public void IsEligible_CrewHasAllRequiredSkills_ReturnsTrue()
    {
        var crew = Crew("electrical", "confined-space");
        var order = Order("electrical");

        CrewSkillMatcher.IsEligible(crew, order).Should().BeTrue();
    }

    [Fact]
    public void IsEligible_CrewMissingARequiredSkill_ReturnsFalse()
    {
        var crew = Crew("electrical");
        var order = Order("electrical", "confined-space");

        CrewSkillMatcher.IsEligible(crew, order).Should().BeFalse();
    }

    [Fact]
    public void IsEligible_OrderRequiresNoSkills_AnyCrewIsEligible()
    {
        var crew = Crew();
        var order = Order();

        CrewSkillMatcher.IsEligible(crew, order).Should().BeTrue();
    }

    [Fact]
    public void IsEligible_CrewMissingRequiredEquipment_ReturnsFalse()
    {
        var crew = new CrewRosterEntry(
            "crew-1", SomePoint, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        var order = new DispatchableOrder(
            "order-1", SomePoint, new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "bucket-truck" }, TimeSpan.FromMinutes(30));

        CrewSkillMatcher.IsEligible(crew, order).Should().BeFalse();
    }
}
