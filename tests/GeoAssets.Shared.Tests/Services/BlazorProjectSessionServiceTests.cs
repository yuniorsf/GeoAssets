using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// Coverage of <see cref="BlazorProjectSessionService"/> (XD01-142) — the load/reconnect flow,
/// the raw-vs-resolved two-tier design, dirty tracking, save/discard, and the close guard.
/// </summary>
public class BlazorProjectSessionServiceTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeProjectClient : IProjectClient
    {
        private readonly Dictionary<Guid, Project> _store;

        /// <summary>Simulates server-side copy-on-write: an id in this map redirects every
        /// scope-update call for it onto the mapped fork id (which must already be seeded).</summary>
        public Dictionary<Guid, Guid> RedirectTo { get; } = [];

        public List<(string Scope, Guid RequestedId)> Calls { get; } = [];

        public FakeProjectClient(params Project[] seed) => _store = seed.ToDictionary(p => p.Id, Clone);

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(id, out var p) ? Clone(p) : null);

        public Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Project> CreateAsync(Project project, CancellationToken ct = default)
        {
            var created = Clone(project);
            created.Id = Guid.NewGuid();
            created.Kind = ProjectKind.User;
            var parent = _store[project.ParentProjectId!.Value];
            created.OrganizationId = parent.OrganizationId;
            _store[created.Id] = created;
            return Task.FromResult(Clone(created));
        }

        public Task<Project> UpdateProvidersAsync(Guid id, List<ProjectProviderEntry>? providers, CancellationToken ct = default) =>
            ApplyAsync(id, "providers", p => p.Providers = providers);

        public Task<Project> UpdateAssetTypeScopeAsync(Guid id, ProjectAssetTypeScope? scope, CancellationToken ct = default) =>
            ApplyAsync(id, "asset-types", p => p.AssetTypeScope = scope);

        public Task<Project> UpdateLayerScopeAsync(Guid id, ProjectLayerScope? scope, CancellationToken ct = default) =>
            ApplyAsync(id, "layers", p => p.LayerScope = scope);

        public Task<Project> UpdateViewStateAsync(Guid id, ProjectViewState? viewState, CancellationToken ct = default) =>
            ApplyAsync(id, "view", p => p.ViewState = viewState);

        private Task<Project> ApplyAsync(Guid requestedId, string scope, Action<Project> mutate)
        {
            Calls.Add((scope, requestedId));
            var targetId = RedirectTo.GetValueOrDefault(requestedId, requestedId);
            var target = _store[targetId];
            mutate(target);
            return Task.FromResult(Clone(target));
        }

        private static Project Clone(Project p) => new()
        {
            Id = p.Id, Name = p.Name, Description = p.Description,
            OrganizationId = p.OrganizationId, CreatedByUserId = p.CreatedByUserId,
            CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt, SchemaVersion = p.SchemaVersion,
            Kind = p.Kind, ParentProjectId = p.ParentProjectId,
            Providers = p.Providers is null ? null : [.. p.Providers],
            AssetTypeScope = p.AssetTypeScope is null ? null : new ProjectAssetTypeScope { VisibleAssetTypeIds = [.. p.AssetTypeScope.VisibleAssetTypeIds] },
            LayerScope = p.LayerScope is null ? null : new ProjectLayerScope { Overrides = [.. p.LayerScope.Overrides] },
            ViewState = p.ViewState is null ? null : new ProjectViewState { Lat = p.ViewState.Lat, Lon = p.ViewState.Lon, Zoom = p.ViewState.Zoom },
        };
    }

    private sealed class FakeProviderPlugin(string id) : IProviderPlugin
    {
        public string Id => id;
        public string DisplayName => id;
        public string Description => "";
        public IReadOnlyList<ProviderConfigField> ConfigFields => [];

        public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
            Task.FromResult<IAssetProvider>(new FakeAssetProvider());
    }

    private sealed class FakeAssetProvider : IAssetProvider
    {
        public IReadOnlyList<GeoFeature> GetAll() => [];
        public GeoFeature? GetById(string id) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> Search(string query) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetNeighbors(string featureId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetDescendants(string featureId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetAncestors(string featureId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> FindPath(string fromId, string toId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents() => throw new NotSupportedException();
        public bool HasCycles() => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> TopologicalSort() => throw new NotSupportedException();
        public void Add(GeoFeature feature) => throw new NotSupportedException();
        public void Update(GeoFeature feature) => throw new NotSupportedException();
        public void AddRange(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void LoadAll(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public IReadOnlyList<AssetType> GetAssetTypes() => throw new NotSupportedException();
        public void AddAssetType(AssetType assetType) => throw new NotSupportedException();
        public void DeleteAssetType(Guid id) => throw new NotSupportedException();
        public IReadOnlyList<Layer> GetLayers() => throw new NotSupportedException();
        public void AddLayer(Layer layer) => throw new NotSupportedException();
        public void DeleteLayer(Guid id) => throw new NotSupportedException();
        public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => throw new NotSupportedException();
        public void AddLayerRule(LayerRule layerRule) => throw new NotSupportedException();
        public void DeleteLayerRule(Guid id) => throw new NotSupportedException();
        public event EventHandler<GeoFeature>? FeatureAdded;
        public event EventHandler<GeoFeature>? FeatureUpdated;
        public event EventHandler<string>? FeatureDeleted;
        public event EventHandler? CollectionChanged;
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new NotSupportedException();
    }

    /// <summary>Same shape as NavMenuTests' StubAuthorizationService — a lambda-driven fake for
    /// the coarse, unscoped permission check <see cref="BlazorProjectSessionService.CanAsync"/>
    /// delegates to (XD01-148).</summary>
    private sealed class FakeGeoAuthorizationService(Func<string, bool>? hasPermission = null) : IGeoAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            Task.FromResult(hasPermission?.Invoke(permissionCode) ?? false);

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMapInterop : IMapInterop
    {
        public List<(string DivId, double Lat, double Lon, int Zoom)> SetViewCalls { get; } = [];

        public Task SetViewAsync(string divId, double lat, double lon, int zoom)
        {
            SetViewCalls.Add((divId, lat, lon, zoom));
            return Task.CompletedTask;
        }

        public Task InitializeMapAsync(string divId, double lat, double lon, int zoom) => throw new NotSupportedException();
        public Task DestroyMapAsync(string divId) => throw new NotSupportedException();
        public Task InvalidateSizeAsync(string divId) => throw new NotSupportedException();
        public Task RenderFeatureAsync(string divId, GeoFeature feature) => throw new NotSupportedException();
        public Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features) => throw new NotSupportedException();
        public Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson) => throw new NotSupportedException();
        public Task RenderFeatureBatchRawJsonAsync(string divId, string rawFeaturesJson) => throw new NotSupportedException();
        public Task RemoveFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
        public Task ClearAllFeaturesAsync(string divId) => throw new NotSupportedException();
        public Task EnableDrawModeAsync(string divId, GeometryType mode) => throw new NotSupportedException();
        public Task DisableDrawModeAsync(string divId) => throw new NotSupportedException();
        public Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds) => throw new NotSupportedException();
        public Task ClearSnapTargetScopeAsync(string divId) => throw new NotSupportedException();
        public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) => throw new NotSupportedException();
        public Task RemoveTileLayerAsync(string divId, string layerId) => throw new NotSupportedException();
        public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options) => throw new NotSupportedException();
        public Task RemoveWmsLayerAsync(string divId, string layerId) => throw new NotSupportedException();
        public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible) => throw new NotSupportedException();
        public Task FitBoundsAsync(string divId, double[] bbox) => throw new NotSupportedException();
        public Task PanToFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
        public Task HighlightFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
        public Task ClearHighlightAsync(string divId, string featureId) => throw new NotSupportedException();
        public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) => throw new NotSupportedException();
    }

    private sealed class FakeMapContext(string mapDivId = "test-map") : ICurrentMapContext
    {
        public string MapDivId => mapDivId;
    }

    private static BlazorProjectSessionService BuildSut(
        FakeProjectClient client, ProviderPool pool, out FakeMapInterop mapInterop,
        params IProviderPlugin[] plugins)
    {
        mapInterop = new FakeMapInterop();
        var registry = new ProviderPluginRegistry(plugins);
        return new BlazorProjectSessionService(
            client, pool, registry, new ThrowingServiceProvider(), mapInterop,
            new FakeMapContext(), new FakeGeoAuthorizationService(), NullLogger<BlazorProjectSessionService>.Instance);
    }

    /// <summary>Variant of <see cref="BuildSut(FakeProjectClient, ProviderPool, out FakeMapInterop, IProviderPlugin[])"/>
    /// for the <see cref="BlazorProjectSessionService.CanAsync"/> tests (XD01-148), which need
    /// control over what <see cref="IGeoAuthorizationService"/> reports.</summary>
    private static BlazorProjectSessionService BuildSutWithAuth(
        FakeProjectClient client, ProviderPool pool, IGeoAuthorizationService authService)
    {
        var registry = new ProviderPluginRegistry([]);
        return new BlazorProjectSessionService(
            client, pool, registry, new ThrowingServiceProvider(), new FakeMapInterop(),
            new FakeMapContext(), authService, NullLogger<BlazorProjectSessionService>.Instance);
    }

    private static Project GeneralProject(Guid? id = null) => new()
    {
        Id             = id ?? Guid.NewGuid(),
        Kind           = ProjectKind.General,
        OrganizationId = Guid.Empty,
        Name           = "General",
        Providers      = [],
        AssetTypeScope = new ProjectAssetTypeScope(),
        LayerScope     = new ProjectLayerScope(),
        ViewState      = new ProjectViewState { Lat = 10, Lon = 20, Zoom = 8 },
    };

    private static Project UserProjectAllScopesNull(Guid parentId, Guid? id = null) => new()
    {
        Id              = id ?? Guid.NewGuid(),
        Kind            = ProjectKind.User,
        ParentProjectId = parentId,
        Name            = "My Fork",
        Providers       = null,
        AssetTypeScope  = null,
        LayerScope      = null,
        ViewState       = null,
    };

    // ── OpenAsync: reconnect flow ─────────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_UnregisteredPluginAmongThree_SkipsItButConnectsTheOtherTwo()
    {
        // Literal acceptance criterion: a 3-provider Project where one entry's plugin is
        // intentionally unregistered still succeeds, with the other two connected.
        var general = GeneralProject();
        general.Providers =
        [
            new ProjectProviderEntry { Name = "A", PluginId = "known-a", Position = 0 },
            new ProjectProviderEntry { Name = "Ghost", PluginId = "unregistered-plugin", Position = 1 },
            new ProjectProviderEntry { Name = "B", PluginId = "known-b", Position = 2 },
        ];
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        var sut = BuildSut(client, pool, out _, new FakeProviderPlugin("known-a"), new FakeProviderPlugin("known-b"));

        await sut.OpenAsync(general.Id);

        pool.All.Should().HaveCount(2);
        pool.All.Select(e => e.Name).Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public async Task OpenAsync_GeneralProject_SetsCurrentToTheRawRowAndClearsDirty()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        var sut = BuildSut(client, pool, out _);

        await sut.OpenAsync(general.Id);

        sut.Current!.Id.Should().Be(general.Id);
        sut.Current.Kind.Should().Be(ProjectKind.General);
        sut.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAsync_ClearsPreExistingPoolEntries()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        pool.Add("Stale", new FakeAssetProvider());
        var sut = BuildSut(client, pool, out _);

        await sut.OpenAsync(general.Id);

        pool.All.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAsync_AppliesViewStateToTheMap()
    {
        var general = GeneralProject();
        general.ViewState = new ProjectViewState { Lat = 12.5, Lon = -34.5, Zoom = 9 };
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out var mapInterop);

        await sut.OpenAsync(general.Id);

        mapInterop.SetViewCalls.Should().ContainSingle()
            .Which.Should().Be(("test-map", 12.5, -34.5, 9));
    }

    [Fact]
    public async Task OpenAsync_UserProject_ResolvesNullScopesFromParent()
    {
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSut(client, new ProviderPool(), out _);

        await sut.OpenAsync(fork.Id);

        sut.Current!.Kind.Should().Be(ProjectKind.User);
        sut.Current.AssetTypeScope.Should().BeEquivalentTo(general.AssetTypeScope);
        sut.Current.ViewState.Should().BeEquivalentTo(general.ViewState);
    }

    [Fact]
    public async Task OpenAsync_UnknownProjectId_ThrowsKeyNotFoundException()
    {
        var sut = BuildSut(new FakeProjectClient(), new ProviderPool(), out _);

        var act = () => sut.OpenAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    [Fact]
    public async Task PoolChanged_AfterOpen_MarksSessionDirtyAndFiresEvent()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        var sut = BuildSut(client, pool, out _);
        await sut.OpenAsync(general.Id);

        var fired = false;
        sut.DirtyChanged += (_, _) => fired = true;
        pool.Add("New", new FakeAssetProvider());

        sut.IsDirty.Should().BeTrue();
        fired.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ScopeSetters))]
    public async Task ScopeSetter_AfterOpen_MarksDirtyAndUpdatesCurrent(
        Action<IProjectSessionService> setScope, Func<Project, object?> readScope, object expected)
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);

        setScope(sut);

        sut.IsDirty.Should().BeTrue();
        readScope(sut.Current!).Should().BeEquivalentTo(expected);
    }

    public static IEnumerable<object[]> ScopeSetters()
    {
        var newAssetTypeScope = new ProjectAssetTypeScope { VisibleAssetTypeIds = [Guid.NewGuid()] };
        var newLayerScope = new ProjectLayerScope { Overrides = [new ProjectLayerOverride { LayerId = Guid.NewGuid() }] };
        var newViewState = new ProjectViewState { Lat = 1, Lon = 2, Zoom = 3 };

        yield return
        [
            (Action<IProjectSessionService>)(s => s.SetAssetTypeScope(newAssetTypeScope)),
            (Func<Project, object?>)(p => p.AssetTypeScope), newAssetTypeScope
        ];
        yield return
        [
            (Action<IProjectSessionService>)(s => s.SetLayerScope(newLayerScope)),
            (Func<Project, object?>)(p => p.LayerScope), newLayerScope
        ];
        yield return
        [
            (Action<IProjectSessionService>)(s => s.SetViewState(newViewState)),
            (Func<Project, object?>)(p => p.ViewState), newViewState
        ];
    }

    // ── SaveAsync: the raw-vs-resolved regression test ───────────────────────

    [Fact]
    public async Task SaveAsync_UserProjectAllScopesNull_ChangingOnlyViewState_LeavesOtherScopesNullOnTheRawRow()
    {
        // The ticket's own critical regression test: open a User Project with all four scopes
        // null, change only ViewState, save, re-fetch the raw row, assert the other three are
        // still null. Fails without the two-tier design — a naive SaveAsync that persisted the
        // *resolved* view would bake in AssetTypeScope/LayerScope as permanent overrides here.
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(fork.Id);

        sut.SetViewState(new ProjectViewState { Lat = 99, Lon = -99, Zoom = 15 });
        await sut.SaveAsync();

        var rawAfterSave = await client.GetByIdAsync(fork.Id);
        rawAfterSave!.ViewState.Should().NotBeNull();
        rawAfterSave.ViewState!.Lat.Should().Be(99);
        rawAfterSave.AssetTypeScope.Should().BeNull();
        rawAfterSave.LayerScope.Should().BeNull();

        client.Calls.Should().ContainSingle().Which.Scope.Should().Be("view");
    }

    [Fact]
    public async Task SaveAsync_NothingChanged_MakesNoClientCallsAndClearsDirty()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);

        await sut.SaveAsync();

        client.Calls.Should().BeEmpty();
        sut.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_PoolChangedWithNoExplicitScopeSetterCalled_PersistsProvidersScope()
    {
        // XD01-157: ProviderEntry couldn't round-trip into ProjectProviderEntry, so pool
        // mutations marked the session dirty but SaveAsync silently discarded them — this is the
        // ticket's own regression test. Fails without the fix (client.Calls stays empty).
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        var sut = BuildSut(client, pool, out _, new FakeProviderPlugin("known-a"));
        await sut.OpenAsync(general.Id);

        pool.Add("New", new FakeAssetProvider(), "known-a", new Dictionary<string, string> { ["url"] = "http://x" });
        await sut.SaveAsync();

        client.Calls.Should().ContainSingle().Which.Scope.Should().Be("providers");
        var raw = await client.GetByIdAsync(general.Id);
        var persisted = raw!.Providers.Should().ContainSingle().Subject;
        persisted.Name.Should().Be("New");
        persisted.PluginId.Should().Be("known-a");
        persisted.Values.Should().ContainKey("url").WhoseValue.Should().Be("http://x");
    }

    [Fact]
    public async Task SaveAsync_ReconnectedProvidersUntouched_MakesNoProvidersClientCall()
    {
        // Round-trip stability: opening a Project with existing Providers and saving without
        // touching the pool must not spuriously re-persist Providers.
        var general = GeneralProject();
        general.Providers =
        [
            new ProjectProviderEntry
            {
                Name = "A", PluginId = "known-a", Position = 0,
                Values = new Dictionary<string, string> { ["url"] = "http://x" },
                IsOpen = true, IsEnabled = true, IsActive = true,
            },
        ];
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _, new FakeProviderPlugin("known-a"));
        await sut.OpenAsync(general.Id);

        await sut.SaveAsync();

        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task PoolChanged_RenameExistingEntry_UpdatesProvidersOnCurrent()
    {
        // Providers has no dedicated SetX setter like the other three scopes — OnPoolChanged is
        // the only place Current.Providers gets refreshed, so a plain rename must flow through.
        var general = GeneralProject();
        general.Providers = [new ProjectProviderEntry { Name = "A", PluginId = "known-a", Position = 0 }];
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        var sut = BuildSut(client, pool, out _, new FakeProviderPlugin("known-a"));
        await sut.OpenAsync(general.Id);

        pool.Rename(pool.All[0].Id, "Renamed");

        sut.Current!.Providers.Should().ContainSingle().Which.Name.Should().Be("Renamed");
        sut.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_ClearsDirtyAndFiresSaved()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);
        sut.SetViewState(new ProjectViewState { Lat = 1, Lon = 1, Zoom = 1 });

        Project? saved = null;
        sut.Saved += (_, p) => saved = p;
        await sut.SaveAsync();

        sut.IsDirty.Should().BeFalse();
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAsync_CopyOnWriteRedirectMidSave_CurrentBecomesTheFork()
    {
        var general = GeneralProject();
        var forkId = Guid.NewGuid();
        var preexistingFork = new Project
        {
            Id = forkId, Kind = ProjectKind.User, ParentProjectId = general.Id,
            OrganizationId = general.OrganizationId, CreatedByUserId = Guid.NewGuid(),
        };
        var client = new FakeProjectClient(general, preexistingFork);
        client.RedirectTo[general.Id] = forkId; // simulates the server's copy-on-write fallback
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);

        sut.SetViewState(new ProjectViewState { Lat = 5, Lon = 5, Zoom = 5 });
        await sut.SaveAsync();

        sut.Current!.Id.Should().Be(forkId);
        sut.Current.Kind.Should().Be(ProjectKind.User);
        // AssetTypeScope/ViewState untouched on the fork resolve from the General parent (unset
        // by this save) except ViewState, which was the one scope actually changed.
        sut.Current.ViewState!.Lat.Should().Be(5);
        sut.Current.AssetTypeScope.Should().BeEquivalentTo(general.AssetTypeScope);
    }

    // ── SaveAsAsync ("Save As") ───────────────────────────────────────────────

    [Fact]
    public async Task SaveAsAsync_FromGeneralProject_CreatesUserForkAndOpensIt()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);

        await sut.SaveAsAsync("My Copy", "desc");

        sut.Current.Should().NotBeNull();
        sut.Current!.Id.Should().NotBe(general.Id);
        sut.Current.Kind.Should().Be(ProjectKind.User);
        sut.Current.ParentProjectId.Should().Be(general.Id);
        sut.Current.Name.Should().Be("My Copy");
    }

    [Fact]
    public async Task SaveAsAsync_CarriesOverTheLiveWorkingCopyNotTheResolvedView()
    {
        // Fails without the fix: persisting Current (resolved) instead of _liveRaw (raw) would
        // bake in every untouched scope on the new fork instead of letting it keep inheriting.
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(fork.Id);

        sut.SetViewState(new ProjectViewState { Lat = 1, Lon = 1, Zoom = 1 });
        await sut.SaveAsAsync("My Second Copy", "desc");

        sut.Current!.ParentProjectId.Should().Be(general.Id);
        sut.Current.ViewState!.Lat.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsAsync_FromAnExistingUserFork_ForksTheSameGeneralAncestor()
    {
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(fork.Id);

        await sut.SaveAsAsync("Another Copy", "desc");

        sut.Current!.ParentProjectId.Should().Be(general.Id);
        sut.Current.Id.Should().NotBe(fork.Id);
    }

    [Fact]
    public async Task SaveAsAsync_DoesNotMutateTheOriginallyOpenProject()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);

        await sut.SaveAsAsync("My Copy", "desc");

        var originalStillExists = await client.GetByIdAsync(general.Id);
        originalStillExists!.Name.Should().Be(general.Name);
    }

    // ── DiscardChangesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DiscardChangesAsync_RevertsMaterializedScopeBackToInherited()
    {
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(fork.Id);

        sut.SetAssetTypeScope(new ProjectAssetTypeScope { VisibleAssetTypeIds = [Guid.NewGuid()] });
        sut.IsDirty.Should().BeTrue();

        await sut.DiscardChangesAsync();

        sut.IsDirty.Should().BeFalse();
        sut.Current!.AssetTypeScope.Should().BeEquivalentTo(general.AssetTypeScope);
    }

    [Fact]
    public async Task DiscardChangesAsync_ClearsAndRebuildsThePool()
    {
        var general = GeneralProject();
        general.Providers = [new ProjectProviderEntry { Name = "A", PluginId = "known-a", Position = 0 }];
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        var sut = BuildSut(client, pool, out _, new FakeProviderPlugin("known-a"));
        await sut.OpenAsync(general.Id);

        pool.Add("Extra", new FakeAssetProvider());
        pool.All.Should().HaveCount(2);

        await sut.DiscardChangesAsync();

        pool.All.Should().ContainSingle().Which.Name.Should().Be("A");
    }

    // ── CloseAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseAsync_UnloadsCurrentAndClearsDirty()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);
        sut.SetViewState(new ProjectViewState { Lat = 1, Lon = 1, Zoom = 1 });

        await sut.CloseAsync();

        sut.Current.Should().BeNull();
        sut.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task CloseAsync_DisconnectsEveryPooledProvider()
    {
        var general = GeneralProject();
        general.Providers = [new ProjectProviderEntry { Name = "A", PluginId = "known-a", Position = 0 }];
        var client = new FakeProjectClient(general);
        var pool = new ProviderPool();
        var sut = BuildSut(client, pool, out _, new FakeProviderPlugin("known-a"));
        await sut.OpenAsync(general.Id);

        await sut.CloseAsync();

        pool.All.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseAsync_FiresCurrentChanged()
    {
        var general = GeneralProject();
        var sut = BuildSut(new FakeProjectClient(general), new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);

        var fired = false;
        sut.CurrentChanged += (_, _) => fired = true;
        await sut.CloseAsync();

        fired.Should().BeTrue();
    }

    // ── CurrentChanged ────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_FiresCurrentChanged()
    {
        var general = GeneralProject();
        var sut = BuildSut(new FakeProjectClient(general), new ProviderPool(), out _);

        var fired = false;
        sut.CurrentChanged += (_, _) => fired = true;
        await sut.OpenAsync(general.Id);

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_NoRedirect_DoesNotFireCurrentChanged()
    {
        var general = GeneralProject();
        var sut = BuildSut(new FakeProjectClient(general), new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);
        sut.SetViewState(new ProjectViewState { Lat = 1, Lon = 1, Zoom = 1 });

        var fired = false;
        sut.CurrentChanged += (_, _) => fired = true;
        await sut.SaveAsync();

        fired.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_CopyOnWriteRedirect_FiresCurrentChanged()
    {
        var general = GeneralProject();
        var forkId = Guid.NewGuid();
        var preexistingFork = new Project
        {
            Id = forkId, Kind = ProjectKind.User, ParentProjectId = general.Id,
            OrganizationId = general.OrganizationId, CreatedByUserId = Guid.NewGuid(),
        };
        var client = new FakeProjectClient(general, preexistingFork);
        client.RedirectTo[general.Id] = forkId;
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);
        sut.SetViewState(new ProjectViewState { Lat = 5, Lon = 5, Zoom = 5 });

        var fired = false;
        sut.CurrentChanged += (_, _) => fired = true;
        await sut.SaveAsync();

        fired.Should().BeTrue();
    }

    // ── RequestCloseAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RequestCloseAsync_NotDirty_ReturnsTrueWithoutInvokingHook()
    {
        var general = GeneralProject();
        var sut = BuildSut(new FakeProjectClient(general), new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);

        var invoked = false;
        sut.CloseRequested = () => { invoked = true; return Task.FromResult(ProjectCloseChoice.Cancel); };

        (await sut.RequestCloseAsync()).Should().BeTrue();
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task RequestCloseAsync_Dirty_NoHandlerRegistered_ReturnsFalse()
    {
        var general = GeneralProject();
        var pool = new ProviderPool();
        var sut = BuildSut(new FakeProjectClient(general), pool, out _);
        await sut.OpenAsync(general.Id);
        pool.Add("Dirty maker", new FakeAssetProvider());

        (await sut.RequestCloseAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RequestCloseAsync_Dirty_SaveAndClose_SavesAndReturnsTrue()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);
        sut.SetViewState(new ProjectViewState { Lat = 1, Lon = 1, Zoom = 1 });
        sut.CloseRequested = () => Task.FromResult(ProjectCloseChoice.SaveAndClose);

        (await sut.RequestCloseAsync()).Should().BeTrue();

        client.Calls.Should().ContainSingle();
        sut.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task RequestCloseAsync_Dirty_DiscardAndClose_DiscardsAndReturnsTrue()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(general.Id);
        sut.SetViewState(new ProjectViewState { Lat = 1, Lon = 1, Zoom = 1 });
        sut.CloseRequested = () => Task.FromResult(ProjectCloseChoice.DiscardAndClose);

        (await sut.RequestCloseAsync()).Should().BeTrue();

        client.Calls.Should().BeEmpty();
        sut.IsDirty.Should().BeFalse();
        sut.Current!.ViewState.Should().BeEquivalentTo(general.ViewState);
    }

    [Fact]
    public async Task RequestCloseAsync_Dirty_Cancel_ReturnsFalseAndLeavesStateDirty()
    {
        var general = GeneralProject();
        var pool = new ProviderPool();
        var sut = BuildSut(new FakeProjectClient(general), pool, out _);
        await sut.OpenAsync(general.Id);
        pool.Add("Dirty maker", new FakeAssetProvider());
        sut.CloseRequested = () => Task.FromResult(ProjectCloseChoice.Cancel);

        (await sut.RequestCloseAsync()).Should().BeFalse();

        sut.IsDirty.Should().BeTrue();
    }

    // ── RawCurrent (XD01-148) ─────────────────────────────────────────────────

    [Fact]
    public void RawCurrent_NoProjectOpen_IsNull()
    {
        var sut = BuildSut(new FakeProjectClient(), new ProviderPool(), out _);

        sut.RawCurrent.Should().BeNull();
    }

    [Fact]
    public async Task RawCurrent_GeneralProject_MatchesCurrent()
    {
        var general = GeneralProject();
        var sut = BuildSut(new FakeProjectClient(general), new ProviderPool(), out _);

        await sut.OpenAsync(general.Id);

        sut.RawCurrent.Should().BeEquivalentTo(sut.Current);
    }

    [Fact]
    public async Task RawCurrent_UserProjectWithNullScopes_StaysNullUnlikeTheResolvedCurrent()
    {
        // The whole point of RawCurrent: Current resolves null scopes from the parent, but
        // RawCurrent must keep reporting them as null — that's what lets a UI tell "inherited"
        // apart from "personalized". Fails without the fix (no way to observe this at all
        // before RawCurrent existed).
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSut(client, new ProviderPool(), out _);

        await sut.OpenAsync(fork.Id);

        sut.Current!.AssetTypeScope.Should().NotBeNull();
        sut.RawCurrent!.AssetTypeScope.Should().BeNull();
        sut.RawCurrent.LayerScope.Should().BeNull();
        sut.RawCurrent.ViewState.Should().BeNull();
        sut.RawCurrent.Providers.Should().BeNull();
    }

    [Fact]
    public async Task RawCurrent_AfterScopeSetter_ReflectsTheOverrideImmediately()
    {
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSut(client, new ProviderPool(), out _);
        await sut.OpenAsync(fork.Id);

        sut.SetViewState(new ProjectViewState { Lat = 1, Lon = 2, Zoom = 3 });

        sut.RawCurrent!.ViewState.Should().NotBeNull();
        sut.RawCurrent.AssetTypeScope.Should().BeNull();
    }

    // ── CanAsync (XD01-148) ───────────────────────────────────────────────────

    [Fact]
    public async Task CanAsync_NoProjectOpen_ReturnsTrue()
    {
        var sut = BuildSutWithAuth(new FakeProjectClient(), new ProviderPool(), new FakeGeoAuthorizationService(_ => false));

        (await sut.CanAsync("projects:manage-providers")).Should().BeTrue();
    }

    [Fact]
    public async Task CanAsync_OwnUserForkOpen_ReturnsTrueRegardlessOfPermission()
    {
        // Acceptance criterion (implicit): a user always has full rights on their own fork —
        // gating only applies to the open General Project.
        var general = GeneralProject();
        var fork = UserProjectAllScopesNull(general.Id);
        var client = new FakeProjectClient(general, fork);
        var sut = BuildSutWithAuth(client, new ProviderPool(), new FakeGeoAuthorizationService(_ => false));
        await sut.OpenAsync(fork.Id);

        (await sut.CanAsync("projects:manage-providers")).Should().BeTrue();
    }

    [Fact]
    public async Task CanAsync_GeneralProjectOpen_PermissionGranted_ReturnsTrue()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSutWithAuth(client, new ProviderPool(),
            new FakeGeoAuthorizationService(code => code == "projects:manage-providers"));
        await sut.OpenAsync(general.Id);

        (await sut.CanAsync("projects:manage-providers")).Should().BeTrue();
    }

    [Fact]
    public async Task CanAsync_GeneralProjectOpen_PermissionDenied_ReturnsFalse()
    {
        var general = GeneralProject();
        var client = new FakeProjectClient(general);
        var sut = BuildSutWithAuth(client, new ProviderPool(), new FakeGeoAuthorizationService(_ => false));
        await sut.OpenAsync(general.Id);

        (await sut.CanAsync("projects:manage-providers")).Should().BeFalse();
    }
}
