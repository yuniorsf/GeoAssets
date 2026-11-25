namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>The callers of the tests, by bearer token: the token alone decides who the caller is.</summary>
internal static class TestIdentities
{
    public static readonly Guid OwnerOrganization = new("a0a0a0a0-0000-4000-8000-000000000382");
    public static readonly Guid OtherOrganization = new("b0b0b0b0-0000-4000-8000-000000000382");
    public static readonly Guid OccupantOne = new("c1c1c1c1-0000-4000-8000-000000000382");
    public static readonly Guid OccupantTwo = new("c2c2c2c2-0000-4000-8000-000000000382");

    private static readonly string[] _all = ["serviceorders:read", "serviceorders:create", "features:read"];

    public static IReadOnlyDictionary<string, TestIdentity> ByToken { get; } = new Dictionary<string, TestIdentity>(StringComparer.Ordinal)
    {
        ["owner-admin"] = new(new Guid("00000000-0000-4000-8000-000000000a01"), OwnerOrganization, ["Administrator"], _all),
        ["owner-reader"] = new(new Guid("00000000-0000-4000-8000-000000000a02"), OwnerOrganization, [], ["serviceorders:read"]),
        ["outsider-admin"] = new(new Guid("00000000-0000-4000-8000-000000000b01"), OtherOrganization, ["Administrator"], _all),
        // XD01-409: a member of the other organization with no home role, who may hold a role on the owner organization.
        ["contractor"] = new(new Guid("00000000-0000-4000-8000-000000000b02"), OtherOrganization, [], ["serviceorders:read"]),
    };
}
