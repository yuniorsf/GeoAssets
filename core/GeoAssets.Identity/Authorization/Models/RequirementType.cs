namespace GeoAssets.Identity.Authorization.Models;

/// <summary>Discriminator for what a <see cref="PolicyRequirement"/> checks.</summary>
public enum RequirementType
{
    /// <summary>Checks that the user has a specific application role.</summary>
    Role,

    /// <summary>Checks that the user has a specific application claim (and optionally a specific value).</summary>
    Claim,

    /// <summary>Checks that the user has a specific permission (directly or through a role).</summary>
    Permission
}
