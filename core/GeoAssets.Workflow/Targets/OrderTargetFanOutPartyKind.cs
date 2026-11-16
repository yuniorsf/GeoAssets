namespace GeoAssets.Workflow.Targets;

/// <summary>Which party each child order of a fan-out is addressed to.</summary>
public enum OrderTargetFanOutPartyKind
{
    /// <summary>One child order per organization occupying at least one of the parent's targets.</summary>
    OccupantOrganization,
}
