using GeoAssets.Workflow.Dispatch;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// Pure skill/equipment eligibility check (XD01-240) — a crew is eligible for an order only if
/// it documents every skill and every piece of equipment the order requires. An order with no
/// documented requirements is eligible for any crew.
/// </summary>
public static class CrewSkillMatcher
{
    public static bool IsEligible(CrewRosterEntry crew, DispatchableOrder order)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(order);

        return crew.Skills.IsSupersetOf(order.RequiredSkills)
            && crew.Equipment.IsSupersetOf(order.RequiredEquipment);
    }
}
