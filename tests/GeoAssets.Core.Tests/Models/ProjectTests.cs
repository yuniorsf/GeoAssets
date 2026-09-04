using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

public class ProjectTests
{
    [Fact]
    public void Construction_AssignsDefaultId()
    {
        new Project().Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Construction_TwoInstances_HaveDifferentIds()
    {
        new Project().Id.Should().NotBe(new Project().Id);
    }

    [Fact]
    public void Construction_HasExpectedDefaults()
    {
        var project = new Project();

        project.Name.Should().BeEmpty();
        project.Description.Should().BeEmpty();
        project.OrganizationId.Should().Be(Guid.Empty);
        project.CreatedByUserId.Should().Be(Guid.Empty);
        project.SchemaVersion.Should().Be(Project.CurrentSchemaVersion);
        project.Kind.Should().Be(ProjectKind.General);
        project.ParentProjectId.Should().BeNull();
    }

    [Fact]
    public void Construction_UserProject_HasAllFourScopesNull()
    {
        // Guards against a default initializer (e.g. `= []`) on any scope property, which would make
        // every freshly-forked User Project immediately override the parent instead of inheriting it.
        var project = new Project { Kind = ProjectKind.User };

        project.Providers.Should().BeNull();
        project.AssetTypeScope.Should().BeNull();
        project.LayerScope.Should().BeNull();
        project.ViewState.Should().BeNull();
    }

    [Fact]
    public void ProjectViewState_HasDefaultsMatchingMapContainerInitParameters()
    {
        var viewState = new ProjectViewState();

        viewState.Lat.Should().Be(20.0);
        viewState.Lon.Should().Be(-77.0);
        viewState.Zoom.Should().Be(5);
    }

    [Fact]
    public void ProjectAssetTypeScope_DefaultsToEmptyList()
    {
        new ProjectAssetTypeScope().VisibleAssetTypeIds.Should().BeEmpty();
    }

    [Fact]
    public void ProjectLayerScope_DefaultsToEmptyList()
    {
        new ProjectLayerScope().Overrides.Should().BeEmpty();
    }

    [Fact]
    public void ProjectProviderEntry_HasExpectedDefaults()
    {
        var entry = new ProjectProviderEntry();

        entry.EntryId.Should().NotBe(Guid.Empty);
        entry.Position.Should().Be(0);
        entry.Name.Should().BeEmpty();
        entry.PluginId.Should().BeEmpty();
        entry.Values.Should().BeEmpty();
        entry.IsOpen.Should().BeFalse();
        entry.IsEnabled.Should().BeTrue();
        entry.IsActive.Should().BeFalse();
    }

    [Fact]
    public void GeneralProject_WithAllScopesPopulated_RoundTripsThroughJson()
    {
        var providerEntryId = Guid.NewGuid();
        var assetTypeId = Guid.NewGuid();
        var layerId = Guid.NewGuid();

        var original = new Project
        {
            Name = "Default Project",
            Description = "Org-wide default",
            OrganizationId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 9, 2, 8, 30, 0, DateTimeKind.Utc),
            Kind = ProjectKind.General,
            Providers =
            [
                new ProjectProviderEntry
                {
                    EntryId = providerEntryId,
                    Position = 0,
                    Name = "Field GPS",
                    PluginId = "geoassets.plugin.gps",
                    Values = new Dictionary<string, string> { ["endpoint"] = "https://example.com" },
                    IsOpen = true,
                    IsEnabled = true,
                    IsActive = true
                }
            ],
            AssetTypeScope = new ProjectAssetTypeScope { VisibleAssetTypeIds = [assetTypeId] },
            LayerScope = new ProjectLayerScope
            {
                Overrides = [new ProjectLayerOverride { LayerId = layerId, Visible = true, SortOrder = 1 }]
            },
            ViewState = new ProjectViewState { Lat = 18.5, Lon = -69.9, Zoom = 10 }
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<Project>(json);

        restored.Should().NotBeNull();
        restored!.Id.Should().Be(original.Id);
        restored.Name.Should().Be(original.Name);
        restored.Description.Should().Be(original.Description);
        restored.OrganizationId.Should().Be(original.OrganizationId);
        restored.CreatedByUserId.Should().Be(original.CreatedByUserId);
        restored.CreatedAt.Should().Be(original.CreatedAt);
        restored.UpdatedAt.Should().Be(original.UpdatedAt);
        restored.SchemaVersion.Should().Be(original.SchemaVersion);
        restored.Kind.Should().Be(ProjectKind.General);

        restored.Providers.Should().ContainSingle();
        restored.Providers![0].EntryId.Should().Be(providerEntryId);
        restored.Providers[0].Values.Should().ContainKey("endpoint").WhoseValue.Should().Be("https://example.com");

        restored.AssetTypeScope.Should().NotBeNull();
        restored.AssetTypeScope!.VisibleAssetTypeIds.Should().ContainSingle().Which.Should().Be(assetTypeId);

        restored.LayerScope.Should().NotBeNull();
        restored.LayerScope!.Overrides.Should().ContainSingle();
        restored.LayerScope.Overrides[0].LayerId.Should().Be(layerId);
        restored.LayerScope.Overrides[0].Visible.Should().BeTrue();
        restored.LayerScope.Overrides[0].SortOrder.Should().Be(1);

        restored.ViewState.Should().NotBeNull();
        restored.ViewState!.Lat.Should().Be(18.5);
        restored.ViewState.Lon.Should().Be(-69.9);
        restored.ViewState.Zoom.Should().Be(10);
    }

    [Fact]
    public void UserProject_WithAllScopesNull_RoundTripsThroughJson_KeepingScopesNull()
    {
        var parentId = Guid.NewGuid();
        var original = new Project
        {
            Kind = ProjectKind.User,
            ParentProjectId = parentId
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<Project>(json);

        restored.Should().NotBeNull();
        restored!.Kind.Should().Be(ProjectKind.User);
        restored.ParentProjectId.Should().Be(parentId);
        restored.Providers.Should().BeNull();
        restored.AssetTypeScope.Should().BeNull();
        restored.LayerScope.Should().BeNull();
        restored.ViewState.Should().BeNull();
    }

    [Fact]
    public void UserProject_WithOnlyViewStateOverridden_RoundTripsThroughJson_MixingNullAndSetScopes()
    {
        var original = new Project
        {
            Kind = ProjectKind.User,
            ParentProjectId = Guid.NewGuid(),
            ViewState = new ProjectViewState { Lat = 1, Lon = 2, Zoom = 3 }
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<Project>(json);

        restored.Should().NotBeNull();
        restored!.Providers.Should().BeNull();
        restored.AssetTypeScope.Should().BeNull();
        restored.LayerScope.Should().BeNull();
        restored.ViewState.Should().NotBeNull();
        restored.ViewState!.Lat.Should().Be(1);
        restored.ViewState.Lon.Should().Be(2);
        restored.ViewState.Zoom.Should().Be(3);
    }
}
