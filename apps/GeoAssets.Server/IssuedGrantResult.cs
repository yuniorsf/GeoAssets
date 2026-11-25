using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Server;

/// <summary>The outcome of an issued-grant write and, unless it found nothing, the grant it concerns.</summary>
public sealed record IssuedGrantResult(IssuedGrantOutcome Outcome, OrganizationGrant? Grant);
