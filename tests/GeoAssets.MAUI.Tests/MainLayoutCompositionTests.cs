using System.Collections.Concurrent;
using Bunit;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Localization;
using GeoAssets.Core.Navigation;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.IpBilling.UI.Navigation;
using GeoAssets.MAUI.Extensions;
using GeoAssets.MAUI.Navigation;
using GeoAssets.NeutralOperator.Rest;
using GeoAssets.NeutralOperator.UI;
using GeoAssets.NeutralOperator.UI.Navigation;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PMTiles.Packs;
using GeoAssets.Provider.Rest;
using GeoAssets.Shared.Components.Layout;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Navigation;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.FeatureActions;
using GeoAssets.Shared.Services.Observability;
using GeoAssets.Shared.Services.OrderDetail;
using GeoAssets.Shared.Services.Rendering;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using IndexPage = GeoAssets.Shared.Pages.Index;

namespace GeoAssets.MAUI.Tests;

/// <summary>
/// XD01-476 — the map page (<see cref="IndexPage"/> in <see cref="MainLayout"/>) rendered under the services
/// <c>MauiProgram.CreateMauiApp</c> registers. <c>MauiProgram</c> itself needs the MAUI runtime, so the composition below
/// makes the same registrations it makes for this page's tree, with in-memory stand-ins only for what is bound to the
/// device (MSAL sign-in, the app data folder, the identity backend's network calls); the layout-specific registrations
/// come from the app's own <c>AddMauiMainLayoutServices</c>, which <c>MauiProgram</c> is checked to call.
/// </summary>
public sealed class MainLayoutCompositionTests : IDisposable
{
    private const string GatewayBaseUrl = "http://localhost:5070";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geoassets-maui-layout-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task MainLayout_WithTheMapPage_RendersUnderTheMauiComposition()
    {
        await using var context = NewMauiContext();

        var render = () => context.Render<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, builder =>
            {
                builder.OpenComponent<IndexPage>(0);
                builder.CloseComponent();
            }));

        var cut = render.Should().NotThrow().Subject;

        cut.WaitForAssertion(() => cut.FindAll(".sidebar-scrollable *").Should().NotBeEmpty("the nav menu renders once identity resolves"));
        cut.Find(".topbar").Should().NotBeNull();
        cut.Find(".map-area").Should().NotBeNull();
        cut.Find(".route-content").GetAttribute("style").Should().Be("display:none", "the map page is the map route");
        context.Services.GetRequiredService<WarningRecorder>().Entries.Should().BeEmpty(
            "the layout logs a warning for an identity, invitation or Project service it cannot use");
    }

    [Fact]
    public async Task EverySidebarPanel_OfTheApp_RendersUnderTheMauiComposition()
    {
        await using var context = NewMauiContext();
        var panels = context.Services.GetRequiredService<MenuRegistry>().All.OfType<MenuPanelItem>().ToList();

        panels.Select(panel => panel.Id).Should().Contain(["collections", "projects", "offline-packs"]);
        foreach (var panel in panels)
        {
            var render = () => context.Render(builder =>
            {
                builder.OpenComponent(0, panel.ComponentType);
                builder.CloseComponent();
            });

            render.Should().NotThrow("the {0} panel ({1}) opens from the app's sidebar", panel.Id, panel.ComponentType.Name);
        }
    }

    private BunitContext NewMauiContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddAuthorization().SetAuthorized("Field Crew");
        AddAsMauiProgramDoes(context.Services, Path.Combine(_directory, "offline-packs"));
        return context;
    }

    private sealed class WarningRecorder : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose() { }

        private sealed class Logger(WarningRecorder recorder, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    recorder.Entries.Enqueue($"{category}: {formatter(state, exception)} ({exception?.Message})");
            }
        }
    }

    [Fact]
    public void MauiProgram_RegistersTheMainLayoutServices()
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "apps", "GeoAssets.MAUI", "MauiProgram.cs"));

        text.Should().Contain("builder.Services.AddMauiMainLayoutServices();");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln not found above the test output.");
    }

    private static void AddAsMauiProgramDoes(IServiceCollection services, string packDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["GeoAssetsServer:BaseUrl"] = GatewayBaseUrl,
                ["ServiceOrders:ApiBaseUrl"] = GatewayBaseUrl + "/api/workflow",
                ["MapInterop:RenderMode"] = "Leaflet",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        var recorder = new WarningRecorder();
        services.AddSingleton(recorder);
        services.AddLogging(b => b.AddProvider(recorder));

        // AddCrossCuttingServices
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IProviderPool, ProviderPool>();
        services.AddGeoAssetsPanelState();
        services.AddScoped<ProviderConnectionMapRenderer>();
        services.AddProviderMapLayerRenderers();
        services.AddGeoAssetsNavigation(
            typeof(OverviewMenuItem).Assembly,
            typeof(IspTenantPortalMenuItem).Assembly,
            typeof(AdminIpBillingMunicipiosMenuItem).Assembly,
            typeof(OfflinePacksMenuItem).Assembly);

        // AddAuthentication — the MSAL state provider and token handler stand in as bUnit's test authorization and
        // a plain "GeoAssetsServer" client.
        services.AddScoped<ICurrentUserAccessor, SignedInUser>();
        services.AddScoped<IAuthNavigationService, NoAuthNavigation>();
        services.AddHttpClient("GeoAssetsServer");

        // AddIdentityAuthorization — the kernel's identity API stands in as a fixed authorization context.
        services.AddScoped<IGeoAuthorizationService, FixedAuthorization>();
        services.AddGeoCoreRestClient(GatewayBaseUrl + "/api/geoassets");
        services.AddFeatureActionsRest(GatewayBaseUrl);
        services.AddSingleton<IJsonStringLocalizer, KeyLocalizer>();

        // AddServiceOrderWorkflow — all but the MAUI-only OrderTypeCatalogLoader.
        services.AddOrderTypeRegistry();
        services.AddDescriptorRoutedWorkflowRest(GatewayBaseUrl + "/api/workflow");
        services.AddDescriptorRoutedNeutralOperatorRest();
        services.AddServiceOrderRules();
        services.AddScoped<WorkflowPrincipalFactory>();
        services.AddSingleton<ICrossOrgEntitlementsService, NoCrossOrgEntitlementsService>();
        services.AddOrderDetailPanels();
        services.AddNeutralOperatorUi();

        // AddAssetsAndMap — the app data folder stands in as an in-memory store.
        services.AddDiscoveredProviderPlugins(
            configuration,
            ProviderHostTargets.MobileDesktop,
            new StaticAssemblyPluginSource()
                .Add<RestProviderPluginModule>()
                .Add<PMTilesProviderPluginModule>());
        services.AddPMTilesPackStorage(packDirectory);
        services.AddSingleton<ActiveAssetProvider>();
        services.AddSingleton<IAssetProvider>(sp => new ObservableAssetProvider(
            new ValidatingAssetProvider(sp.GetRequiredService<ActiveAssetProvider>()),
            sp.GetRequiredService<ILogger<ObservableAssetProvider>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<FeatureRenderPipeline>();
        services.AddScoped<IStorageService, InMemoryStorage>();
        services.Configure<MapInteropOptions>(configuration.GetSection("MapInterop"));
        services.AddScoped<MapInteropService>();
        services.AddScoped(sp => new ObservableMapInterop(
            sp.GetRequiredService<MapInteropService>(),
            sp.GetRequiredService<MapInteropService>(),
            sp.GetRequiredService<IOptions<MapInteropOptions>>().Value.RenderMode,
            sp.GetRequiredService<ILogger<ObservableMapInterop>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IMapInterop>(sp => sp.GetRequiredService<ObservableMapInterop>());
        services.AddScoped<IMapSelectionInterop>(sp => sp.GetRequiredService<ObservableMapInterop>());
        services.AddScoped<IMapLocalTileLayerInterop>(sp => sp.GetRequiredService<MapInteropService>());
        services.AddScoped<AssetService>();
        services.AddScoped<IAssetService>(sp => new ObservableAssetService(
            sp.GetRequiredService<AssetService>(),
            sp.GetRequiredService<ILogger<ObservableAssetService>>(),
            sp.GetRequiredService<TimeProvider>()));

        // XD01-476
        services.AddMauiMainLayoutServices();
    }

    private sealed class SignedInUser : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => new("oid-476", "crew@example.com", "Field Crew", []);
    }

    private sealed class NoAuthNavigation : IAuthNavigationService
    {
        public void NavigateToLogin(string returnUrl = "/") { }
        public void NavigateToLogout(string returnUrl = "/login") { }
    }

    private sealed class FixedAuthorization : IGeoAuthorizationService
    {
        private static readonly AuthorizationContext Context = new()
        {
            User = new AppUser
            {
                ExternalObjectId = "oid-476",
                OrganizationId = new Guid("0a0a0a0a-0000-4000-8000-000000000476"),
                CreatedAt = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc),
            },
            Roles = [],
            Claims = [],
            Permissions = [],
        };

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => Task.FromResult(false);
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(Context);
    }

    private sealed class KeyLocalizer : IJsonStringLocalizer
    {
        public string this[string key] => key;
        public string GetString(string key, params object[] args) => key;
        public event EventHandler LocalizationChanged { add { } remove { } }
    }

    private sealed class InMemoryStorage : IStorageService
    {
        private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

        public Task<GeoAssets.Core.Models.GeoFeatureCollection> LoadAsync(string key = "default", CancellationToken ct = default) =>
            Task.FromResult(new GeoAssets.Core.Models.GeoFeatureCollection());
        public Task SaveAsync(GeoAssets.Core.Models.GeoFeatureCollection collection, string key = "default", CancellationToken ct = default) => Task.CompletedTask;
        public Task<GeoAssets.Core.Models.GeoFeatureCollection> ImportFromStringAsync(string geoJson, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> ExportToStringAsync(GeoAssets.Core.Models.GeoFeatureCollection collection, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> PickImportFileAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SaveExportFileAsync(string geoJson, string suggestedName = "export.geojson", CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveExportBinaryFileAsync(byte[] content, string suggestedName, string mimeType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetStringAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_strings.TryGetValue(key, out var value) ? value : null);
        public Task SetStringAsync(string key, string value, CancellationToken ct = default)
        {
            _strings[key] = value;
            return Task.CompletedTask;
        }
    }
}
