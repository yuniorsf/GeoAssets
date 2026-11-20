namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Answer of <c>PUT {prefix}/organization-grants</c> (XD01-385): the grant as it now stands, and what was done.</summary>
public sealed record OrganizationGrantUpsertResult(OrganizationGrantDto Grant, OrganizationGrantUpsertOutcome Outcome);
