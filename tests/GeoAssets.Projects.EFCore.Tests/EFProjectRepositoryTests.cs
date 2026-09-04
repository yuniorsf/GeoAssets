using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Projects.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Projects.EFCore.Tests;

public class EFProjectRepositoryTests
{
    private static Project GeneralProject(Guid? id = null) => new()
    {
        Id              = id ?? Guid.NewGuid(),
        Name            = "Default Project",
        Description     = "Org-wide default",
        OrganizationId  = Guid.NewGuid(),
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt       = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
        UpdatedAt       = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
        Kind            = ProjectKind.General,
        Providers =
        [
            new ProjectProviderEntry
            {
                Name     = "Field GPS",
                PluginId = "geoassets.plugin.gps",
                Values   = new Dictionary<string, string> { ["endpoint"] = "https://example.com" },
                IsOpen   = true,
                IsActive = true
            }
        ],
        AssetTypeScope = new ProjectAssetTypeScope { VisibleAssetTypeIds = [Guid.NewGuid()] },
        LayerScope = new ProjectLayerScope
        {
            Overrides = [new ProjectLayerOverride { LayerId = Guid.NewGuid(), Visible = true, SortOrder = 1 }]
        },
        ViewState = new ProjectViewState { Lat = 18.5, Lon = -69.9, Zoom = 10 }
    };

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsScalarAndScopeFields()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var project = GeneralProject();

        await repo.AddAsync(project);
        var loaded = await repo.GetByIdAsync(project.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(project.Id);
        loaded.Name.Should().Be(project.Name);
        loaded.Description.Should().Be(project.Description);
        loaded.OrganizationId.Should().Be(project.OrganizationId);
        loaded.CreatedByUserId.Should().Be(project.CreatedByUserId);
        loaded.CreatedAt.Should().Be(project.CreatedAt);
        loaded.UpdatedAt.Should().Be(project.UpdatedAt);
        loaded.SchemaVersion.Should().Be(project.SchemaVersion);
        loaded.Kind.Should().Be(ProjectKind.General);

        loaded.Providers.Should().ContainSingle();
        loaded.Providers![0].Name.Should().Be("Field GPS");
        loaded.Providers[0].Values.Should().ContainKey("endpoint");

        loaded.AssetTypeScope.Should().NotBeNull();
        loaded.AssetTypeScope!.VisibleAssetTypeIds.Should().BeEquivalentTo(project.AssetTypeScope!.VisibleAssetTypeIds);

        loaded.LayerScope.Should().NotBeNull();
        loaded.LayerScope!.Overrides.Should().ContainSingle();

        loaded.ViewState.Should().NotBeNull();
        loaded.ViewState!.Lat.Should().Be(18.5);
        loaded.ViewState.Lon.Should().Be(-69.9);
        loaded.ViewState.Zoom.Should().Be(10);
    }

    [Fact]
    public async Task AddAsync_UserProjectWithAllScopesNull_RoundTripsPreservingNulls()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var parent = GeneralProject();
        await repo.AddAsync(parent);
        var project = new Project { Kind = ProjectKind.User, ParentProjectId = parent.Id };

        await repo.AddAsync(project);
        var loaded = await repo.GetByIdAsync(project.Id);

        loaded.Should().NotBeNull();
        loaded!.Kind.Should().Be(ProjectKind.User);
        loaded.ParentProjectId.Should().Be(parent.Id);
        loaded.Providers.Should().BeNull();
        loaded.AssetTypeScope.Should().BeNull();
        loaded.LayerScope.Should().BeNull();
        loaded.ViewState.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_UserProjectWithOnlyViewStateOverridden_RoundTripsMixedNullAndSetScopes()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var parent = GeneralProject();
        await repo.AddAsync(parent);
        var project = new Project
        {
            Kind            = ProjectKind.User,
            ParentProjectId = parent.Id,
            ViewState       = new ProjectViewState { Lat = 1, Lon = 2, Zoom = 3 }
        };

        await repo.AddAsync(project);
        var loaded = await repo.GetByIdAsync(project.Id);

        loaded.Should().NotBeNull();
        loaded!.Providers.Should().BeNull();
        loaded.AssetTypeScope.Should().BeNull();
        loaded.LayerScope.Should().BeNull();
        loaded.ViewState.Should().NotBeNull();
        loaded.ViewState!.Lat.Should().Be(1);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());

        var loaded = await repo.GetByIdAsync(Guid.NewGuid());

        loaded.Should().BeNull();
    }

    // ── Filtered queries ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByOrganizationAsync_ReturnsOnlyMatchingOrganization()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var orgId = Guid.NewGuid();
        var matching = GeneralProject();
        matching.OrganizationId = orgId;
        var other = GeneralProject();

        await repo.AddAsync(matching);
        await repo.AddAsync(other);

        var result = await repo.GetByOrganizationAsync(orgId);

        result.Should().ContainSingle().Which.Id.Should().Be(matching.Id);
    }

    [Fact]
    public async Task GetForksOfAsync_ReturnsOnlyUserProjectsForkedFromParent()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var parent = GeneralProject();
        var otherParent = GeneralProject();
        var fork = new Project { Kind = ProjectKind.User, ParentProjectId = parent.Id };
        var unrelated = new Project { Kind = ProjectKind.User, ParentProjectId = otherParent.Id };

        await repo.AddAsync(parent);
        await repo.AddAsync(otherParent);
        await repo.AddAsync(fork);
        await repo.AddAsync(unrelated);

        var result = await repo.GetForksOfAsync(parent.Id);

        result.Should().ContainSingle().Which.Id.Should().Be(fork.Id);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ChangesMutableFields_LeavesIdentityAndOwnershipUntouched()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var project = GeneralProject();
        await repo.AddAsync(project);

        var updated = GeneralProject(project.Id);
        updated.Name = "Renamed";
        updated.OrganizationId = Guid.NewGuid(); // must be ignored by UpdateAsync
        updated.ViewState = new ProjectViewState { Lat = 99, Lon = 99, Zoom = 1 };

        await repo.UpdateAsync(updated);
        var loaded = await repo.GetByIdAsync(project.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Renamed");
        loaded.ViewState!.Lat.Should().Be(99);
        loaded.OrganizationId.Should().Be(project.OrganizationId); // unchanged from original AddAsync
        loaded.CreatedByUserId.Should().Be(project.CreatedByUserId);
        loaded.CreatedAt.Should().Be(project.CreatedAt);
        loaded.Kind.Should().Be(project.Kind);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_Throws()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());

        var act = () => repo.UpdateAsync(GeneralProject());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Soft delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SoftDeletes_RowExcludedFromNormalQueriesButStillInDatabase()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var project = GeneralProject();
        await repo.AddAsync(project);

        await repo.DeleteAsync(project.Id);

        (await repo.GetByIdAsync(project.Id)).Should().BeNull();
        (await repo.GetAllAsync()).Should().BeEmpty();

        await using var raw = fixture.NewContext();
        var stillExists = await raw.Projects.IgnoreQueryFilters().AnyAsync(p => p.Id == project.Id);
        stillExists.Should().BeTrue();

        var row = await raw.Projects.IgnoreQueryFilters().FirstAsync(p => p.Id == project.Id);
        row.IsDeleted.Should().BeTrue();
        row.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_IsNoOp()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());

        var act = () => repo.DeleteAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    // ── Events ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_RaisesProjectSaved()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        Project? saved = null;
        repo.ProjectSaved += (_, p) => saved = p;

        var project = GeneralProject();
        await repo.AddAsync(project);

        saved.Should().NotBeNull();
        saved!.Id.Should().Be(project.Id);
    }

    [Fact]
    public async Task UpdateAsync_RaisesProjectSaved()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var project = GeneralProject();
        await repo.AddAsync(project);

        var raiseCount = 0;
        repo.ProjectSaved += (_, _) => raiseCount++;
        await repo.UpdateAsync(GeneralProject(project.Id));

        raiseCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_RaisesProjectDeleted()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFProjectRepository(fixture.NewContext());
        var project = GeneralProject();
        await repo.AddAsync(project);

        Guid? deletedId = null;
        repo.ProjectDeleted += (_, id) => deletedId = id;
        await repo.DeleteAsync(project.Id);

        deletedId.Should().Be(project.Id);
    }
}
