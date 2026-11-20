namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>The callers of the Município grant bridge tests, by bearer token.</summary>
internal static class BridgeCallers
{
    public const string ServiceCallerToken = "service-caller-token";
    public const string ReferenceManagerToken = "reference-manager";
    public const string ReaderToken = "ip-billing-reader";
    public const string AdministratorToken = "administrator-without-the-permission";
    public const string WrongAudienceToken = "token-the-kernel-does-not-accept";

    public const string ServiceCallerRole = "GeoAssets.ServiceCaller";
    public const string ManagePermission = "ip-billing:reference-manage";

    public static IReadOnlyDictionary<string, BridgeCaller> ByToken { get; } = new Dictionary<string, BridgeCaller>(StringComparer.Ordinal)
    {
        [ServiceCallerToken] = new("service-caller", [ServiceCallerRole], []),
        [ReferenceManagerToken] = new("reference-manager", [], [ManagePermission, "ip-billing:read"]),
        [ReaderToken] = new("reader", [], ["ip-billing:read"]),
        [AdministratorToken] = new("administrator", ["Administrator"], ["users:read"]),
        [WrongAudienceToken] = new("wrong-audience", [], [ManagePermission], KernelAcceptsToken: false),
    };
}
