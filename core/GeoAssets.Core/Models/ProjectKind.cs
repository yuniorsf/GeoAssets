namespace GeoAssets.Core.Models;

/// <summary>Which of the two Project tiers a <see cref="Project"/> row represents.</summary>
public enum ProjectKind
{
    /// <summary>Shared, governed, org-scoped Project.</summary>
    General,

    /// <summary>Personal fork of a <see cref="General"/> Project, owner-controlled.</summary>
    User
}
