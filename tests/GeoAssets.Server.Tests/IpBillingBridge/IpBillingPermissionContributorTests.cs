using FluentAssertions;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.IpBilling.Server;
using Xunit;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>The IP-billing vertical's permissions as IpBilling.Server publishes them to the kernel (XD01-377, XD01-385).</summary>
public sealed class IpBillingPermissionContributorTests
{
    [Fact]
    public void VerticalId_IsIpBilling_AtTheKernelsRegistrationRoute()
    {
        new IpBillingPermissionContributor().VerticalId.Should().Be("ip-billing").And.Be(IpBillingPermissionContributor.IpBillingVerticalId);
    }

    [Fact]
    public void Permissions_AreExactlyTheSevenIpBillingPermissions_AndNoRoleBundles()
    {
        var contributor = new IpBillingPermissionContributor();

        contributor.Permissions.Should().Equal(
            new PermissionDefinition("ip-billing:read", "ip-billing", "read", "permissions.ipBilling.read"),
            new PermissionDefinition("ip-billing:upload", "ip-billing", "upload", "permissions.ipBilling.upload"),
            new PermissionDefinition("ip-billing:calculate", "ip-billing", "calculate", "permissions.ipBilling.calculate"),
            new PermissionDefinition("ip-billing:validate", "ip-billing", "validate", "permissions.ipBilling.validate"),
            new PermissionDefinition("ip-billing:ledger-manual", "ip-billing", "ledger-manual", "permissions.ipBilling.ledgerManual"),
            new PermissionDefinition("ip-billing:ledger-recarga", "ip-billing", "ledger-recarga", "permissions.ipBilling.ledgerRecarga"),
            new PermissionDefinition("ip-billing:reference-manage", "ip-billing", "reference-manage", "permissions.ipBilling.referenceManage"));
        contributor.RoleBundles.Should().BeEmpty();
        new ModulePermissionSet { Permissions = [.. contributor.Permissions] }.FindProblem().Should().BeNull();
    }

    [Fact]
    public void TheManagePermissionTheGrantEndpointsRequire_IsOneOfThePublishedPermissions()
    {
        new IpBillingPermissionContributor().Permissions.Select(p => p.Code).Should().Contain(MunicipioGrantsRestApiExtensions.ManagePermission);
    }
}
