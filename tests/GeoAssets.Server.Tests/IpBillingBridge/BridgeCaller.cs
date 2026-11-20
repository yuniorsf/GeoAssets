namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>Who a bearer token is, as the stand-in kernel and the IpBilling host's stand-in token validation both read it.</summary>
internal sealed record BridgeCaller(string Subject, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, bool KernelAcceptsToken = true);
