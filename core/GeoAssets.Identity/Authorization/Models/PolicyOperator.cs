namespace GeoAssets.Identity.Authorization.Models;

/// <summary>Controls how multiple <see cref="PolicyRequirement"/> items are combined.</summary>
public enum PolicyOperator
{
    /// <summary>All requirements must be satisfied (logical AND).</summary>
    All,

    /// <summary>At least one requirement must be satisfied (logical OR).</summary>
    Any
}
