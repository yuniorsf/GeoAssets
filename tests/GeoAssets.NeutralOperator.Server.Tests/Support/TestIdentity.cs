namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>Who a bearer token is: what the kernel's <c>/api/identity/me</c> answers for it.</summary>
internal sealed record TestIdentity(Guid UserId, Guid OrganizationId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
