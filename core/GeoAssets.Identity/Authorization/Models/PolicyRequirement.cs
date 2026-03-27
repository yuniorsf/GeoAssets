namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// A single requirement within an <see cref="AppPolicy"/>.
///
/// Depending on <see cref="Type"/>:
///   Role       → <see cref="Value"/> = role name (e.g. "Supervisor")
///   Claim      → <see cref="Value"/> = claim type (e.g. "zone"),
///                <see cref="ClaimValue"/> = expected value or null to accept any value
///   Permission → <see cref="Value"/> = permission code (e.g. "serviceorders:create")
/// </summary>
public sealed class PolicyRequirement
{
    public Guid            Id           { get; set; } = Guid.NewGuid();
    public Guid            PolicyId     { get; set; }
    public RequirementType Type         { get; set; }

    /// <summary>Role name, claim type, or permission code depending on <see cref="Type"/>.</summary>
    public string          Value        { get; set; } = string.Empty;

    /// <summary>Expected claim value when <see cref="Type"/> is <see cref="RequirementType.Claim"/>.
    /// Null means any value is accepted.</summary>
    public string?         ClaimValue   { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public AppPolicy Policy { get; set; } = null!;
}
