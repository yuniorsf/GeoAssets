namespace GeoAssets.Identity.Authorization.Models;

/// <summary>The values of <see cref="OrganizationGrant.Source"/> (XD01-411).</summary>
public static class OrganizationGrantSources
{
    /// <summary>Written by a trusted service — a vertical host provisioning the grants its reference data implies.</summary>
    public const string Service = "service";

    /// <summary>Issued by an administrator of the resource organization, through its <c>issued-grants</c> routes.</summary>
    public const string Administrator = "administrator";
}
