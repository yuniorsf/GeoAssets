using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

public class OrgOwnershipTests
{
    // ── GeoFeature ─────────────────────────────────────────────────────────────

    [Fact]
    public void GeoFeature_OrganizationId_DefaultsToEmpty()
    {
        new GeoFeature().Properties.OrganizationId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void GeoFeature_AsIOrgOwnedResource_DelegatesToProperties()
    {
        var orgId = Guid.NewGuid();
        var feature = new GeoFeature { Properties = new GeoFeatureProperties { OrganizationId = orgId } };

        ((IOrgOwnedResource)feature).OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void GeoFeature_OrganizationId_RoundTripsThroughJson()
    {
        var orgId = Guid.NewGuid();
        var original = new GeoFeature { Id = "f1", Properties = new GeoFeatureProperties { OrganizationId = orgId } };

        var json     = GeoJsonSerializer.SerializeFeature(original);
        var restored = GeoJsonSerializer.DeserializeFeature(json);

        json.Should().Contain("\"organizationId\"");
        restored.Should().NotBeNull();
        restored!.Properties.OrganizationId.Should().Be(orgId);
    }

    // ── AssetType ──────────────────────────────────────────────────────────────

    [Fact]
    public void AssetType_OrganizationId_DefaultsToEmpty()
    {
        new AssetType().OrganizationId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AssetType_ImplementsIOrgOwnedResource()
    {
        var orgId = Guid.NewGuid();
        var assetType = new AssetType { OrganizationId = orgId };

        ((IOrgOwnedResource)assetType).OrganizationId.Should().Be(orgId);
    }
}
