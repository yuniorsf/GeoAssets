using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class ProjectResolverTests
{
    private static Project GeneralParent() => new()
    {
        Kind = ProjectKind.General,
        Providers = [new ProjectProviderEntry { Name = "Parent Provider" }],
        AssetTypeScope = new ProjectAssetTypeScope { VisibleAssetTypeIds = [Guid.NewGuid()] },
        LayerScope = new ProjectLayerScope
        {
            Overrides = [new ProjectLayerOverride { LayerId = Guid.NewGuid(), Visible = true, SortOrder = 0 }]
        },
        ViewState = new ProjectViewState { Lat = 20.0, Lon = -77.0, Zoom = 5 }
    };

    [Fact]
    public void Resolve_FullyNullUserProject_ResolvesEntirelyFromParent()
    {
        var parent = GeneralParent();
        var userProject = new Project { Kind = ProjectKind.User, ParentProjectId = parent.Id };

        var resolved = ProjectResolver.Resolve(userProject, parent);

        resolved.Providers.Should().BeSameAs(parent.Providers);
        resolved.AssetTypeScope.Should().BeSameAs(parent.AssetTypeScope);
        resolved.LayerScope.Should().BeSameAs(parent.LayerScope);
        resolved.ViewState.Should().BeSameAs(parent.ViewState);
    }

    [Fact]
    public void Resolve_PartiallyOverriddenUserProject_MixesOverriddenAndInheritedScopes()
    {
        var parent = GeneralParent();
        var ownViewState = new ProjectViewState { Lat = 1, Lon = 2, Zoom = 3 };
        var userProject = new Project
        {
            Kind = ProjectKind.User,
            ParentProjectId = parent.Id,
            ViewState = ownViewState
        };

        var resolved = ProjectResolver.Resolve(userProject, parent);

        resolved.ViewState.Should().BeSameAs(ownViewState);
        resolved.Providers.Should().BeSameAs(parent.Providers);
        resolved.AssetTypeScope.Should().BeSameAs(parent.AssetTypeScope);
        resolved.LayerScope.Should().BeSameAs(parent.LayerScope);
    }

    [Fact]
    public void Resolve_FullyOverriddenUserProject_UsesNoneOfTheParentsScopes()
    {
        var parent = GeneralParent();
        var ownProviders = new List<ProjectProviderEntry> { new() { Name = "Own Provider" } };
        var ownAssetTypeScope = new ProjectAssetTypeScope { VisibleAssetTypeIds = [Guid.NewGuid()] };
        var ownLayerScope = new ProjectLayerScope();
        var ownViewState = new ProjectViewState { Lat = 9, Lon = 9, Zoom = 9 };
        var userProject = new Project
        {
            Kind = ProjectKind.User,
            ParentProjectId = parent.Id,
            Providers = ownProviders,
            AssetTypeScope = ownAssetTypeScope,
            LayerScope = ownLayerScope,
            ViewState = ownViewState
        };

        var resolved = ProjectResolver.Resolve(userProject, parent);

        resolved.Providers.Should().BeSameAs(ownProviders);
        resolved.AssetTypeScope.Should().BeSameAs(ownAssetTypeScope);
        resolved.LayerScope.Should().BeSameAs(ownLayerScope);
        resolved.ViewState.Should().BeSameAs(ownViewState);
    }

    [Fact]
    public void Resolve_CopiesIdentityAndOwnershipFieldsFromUserProject_NotFromParent()
    {
        var parent = GeneralParent();
        var userProject = new Project
        {
            Kind = ProjectKind.User,
            ParentProjectId = parent.Id,
            Name = "My Fork",
            OrganizationId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid()
        };

        var resolved = ProjectResolver.Resolve(userProject, parent);

        resolved.Id.Should().Be(userProject.Id);
        resolved.Name.Should().Be("My Fork");
        resolved.OrganizationId.Should().Be(userProject.OrganizationId);
        resolved.CreatedByUserId.Should().Be(userProject.CreatedByUserId);
        resolved.Kind.Should().Be(ProjectKind.User);
        resolved.ParentProjectId.Should().Be(parent.Id);
    }
}
