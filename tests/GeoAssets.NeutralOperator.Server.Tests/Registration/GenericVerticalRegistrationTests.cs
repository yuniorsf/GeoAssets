using System.Net;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Hosting;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>
/// The bootstrap registration as the chassis offers it to every vertical host (XD01-385): a vertical that only publishes its
/// permissions (<c>VerticalRegistrationSource</c> without descriptors — IpBilling.Server) makes no order-type call, one with
/// descriptors (NeutralOperator.Server) publishes them first, and only the contributors of the source's own vertical are published.
/// </summary>
public sealed class GenericVerticalRegistrationTests
{
    private const string Vertical = "widgets";

    private static readonly OrderTypeDescriptor Descriptor = new()
    {
        VerticalId = Vertical,
        OrderTypeId = "widget-work",
        DisplayNameKey = "orderTypes.widgetWork",
        ApiBaseRoute = "/api/v/widgets/workflow/service-orders",
        TargetPolicy = new OrderTargetPolicy { TargetAssetTypeRoles = ["widget"], SelectionLimit = 10 },
        TerminalStatuses = ["Done"],
    };

    private sealed class Contributor(string verticalId, params string[] codes) : IModulePermissionContributor
    {
        public string VerticalId => verticalId;

        public IEnumerable<PermissionDefinition> Permissions { get; } = [.. codes.Select(c => new PermissionDefinition(c, "widgets", "act", $"permissions.{c}"))];

        public IEnumerable<RoleBundleContribution> RoleBundles { get; } = [];
    }

    private sealed class ScopedMarker;

    private sealed class Loop : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly VerticalRegistrationService _service;

        public Loop(VerticalRegistrationSource source, params IModulePermissionContributor[] contributors)
        {
            Client = new FakeVerticalRegistrationClient();
            Time = new SchedulingAwareTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
            State = new VerticalRegistrationState();
            _provider = new ServiceCollection()
                .AddScoped<IVerticalRegistrationClient>(_ => Client)
                .AddScoped<ScopedMarker>()
                .BuildServiceProvider();
            _service = new VerticalRegistrationService(
                _provider.GetRequiredService<IServiceScopeFactory>(), source, contributors, State,
                Options.Create(new VerticalRegistrationOptions()), Time, NullLogger<VerticalRegistrationService>.Instance);
        }

        public FakeVerticalRegistrationClient Client { get; }

        public SchedulingAwareTimeProvider Time { get; }

        public VerticalRegistrationState State { get; }

        public Task StartAsync() => _service.StartAsync(CancellationToken.None);

        public Task UntilRegisteredAsync() => RegistrationLoop.UntilAsync(() => State.Current.Status == VerticalRegistrationStatus.Registered);

        public async ValueTask DisposeAsync()
        {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
            await _provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task WithoutDescriptors_OnlyThePermissionsArePublished_AndTheHostIsRegistered()
    {
        await using var loop = new Loop(new VerticalRegistrationSource(Vertical), new Contributor(Vertical, "widgets:read", "widgets:edit"));

        await loop.StartAsync();
        await loop.UntilRegisteredAsync();

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.Permissions);
        var call = loop.Client.Calls.Single();
        call.VerticalId.Should().Be(Vertical);
        ((ModulePermissionSet)call.Payload).Permissions.Select(p => p.Code).Should().Equal("widgets:read", "widgets:edit");
    }

    [Fact]
    public async Task WithoutDescriptors_TheRefreshRepublishesOnlyThePermissions_NeverAnOrderTypeSet()
    {
        await using var loop = new Loop(new VerticalRegistrationSource(Vertical), new Contributor(Vertical, "widgets:read"));
        await loop.StartAsync();
        await loop.UntilRegisteredAsync();

        await loop.Time.AdvanceWhenScheduledAsync(new VerticalRegistrationOptions().RefreshInterval);
        await RegistrationLoop.UntilAsync(() => loop.Client.Calls.Count == 2);

        loop.Client.Kinds.Should().OnlyContain(kind => kind == FakeVerticalRegistrationClient.Permissions);
    }

    [Fact]
    public async Task WithoutDescriptors_APermissionsFailure_IsRetried_AndNoOrderTypeCallIsEverMade()
    {
        await using var loop = new Loop(new VerticalRegistrationSource(Vertical), new Contributor(Vertical, "widgets:read"));
        loop.Client.Fails = _ => true;

        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Failing);
        loop.Client.Fails = _ => false;
        await loop.Time.AdvanceWhenScheduledAsync(new VerticalRegistrationOptions().MaxDelay);
        await loop.UntilRegisteredAsync();

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.Permissions, FakeVerticalRegistrationClient.Permissions);
    }

    [Fact]
    public async Task WithDescriptors_TheOrderTypesArePublishedFirst_ThenThePermissions()
    {
        var source = new VerticalRegistrationSource(Vertical, _ => [Descriptor]);
        await using var loop = new Loop(source, new Contributor(Vertical, "widgets:read"));

        await loop.StartAsync();
        await loop.UntilRegisteredAsync();

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions);
        ((IReadOnlyList<OrderTypeDescriptor>)loop.Client.Calls[0].Payload).Should().ContainSingle().Which.OrderTypeId.Should().Be("widget-work");
    }

    [Fact]
    public async Task WithDescriptors_RefusedOrderTypes_StopThePassBeforeThePermissions()
    {
        var source = new VerticalRegistrationSource(Vertical, _ => [Descriptor]);
        await using var loop = new Loop(source, new Contributor(Vertical, "widgets:read"));
        loop.Client.Fails = kind => string.Equals(kind, FakeVerticalRegistrationClient.OrderTypes, StringComparison.Ordinal);

        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Failing);

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.OrderTypes);
    }

    [Fact]
    public async Task WithAnEmptyDescriptorSet_AnEmptyOrderTypeSetIsStillPublished_ReplacingTheVerticalsSetAtTheKernel()
    {
        // A source that names a descriptor factory owns the vertical's set at the kernel, even when it is empty; only a null
        // factory leaves the kernel's set untouched.
        await using var loop = new Loop(new VerticalRegistrationSource(Vertical, _ => []), new Contributor(Vertical, "widgets:read"));

        await loop.StartAsync();
        await loop.UntilRegisteredAsync();

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions);
        ((IReadOnlyList<OrderTypeDescriptor>)loop.Client.Calls[0].Payload).Should().BeEmpty();
    }

    [Fact]
    public async Task TheDescriptorFactory_ReadsTheRegistrationsOwnScope()
    {
        IServiceProvider? seen = null;
        var source = new VerticalRegistrationSource(Vertical, sp =>
        {
            seen = sp;
            _ = sp.GetRequiredService<ScopedMarker>();
            return [Descriptor];
        });
        await using var loop = new Loop(source);

        await loop.StartAsync();
        await loop.UntilRegisteredAsync();

        seen.Should().NotBeNull();
    }

    [Fact]
    public async Task OnlyTheSourcesOwnContributors_ArePublished_ForeignOnesAreLeftOut_AndNoContributorMeansAnEmptySet()
    {
        await using var own = new Loop(new VerticalRegistrationSource(Vertical), new Contributor("other", "other:read"), new Contributor(Vertical, "widgets:read"), new Contributor(Vertical, "widgets:edit"));
        await using var none = new Loop(new VerticalRegistrationSource(Vertical), new Contributor("other", "other:read"));

        await own.StartAsync();
        await none.StartAsync();
        await own.UntilRegisteredAsync();
        await none.UntilRegisteredAsync();

        ((ModulePermissionSet)own.Client.Calls.Single().Payload).Permissions.Select(p => p.Code).Should().Equal("widgets:read", "widgets:edit");
        ((ModulePermissionSet)none.Client.Calls.Single().Payload).Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task TheVerticalIdOfTheSource_IsMatchedOrdinally()
    {
        await using var loop = new Loop(new VerticalRegistrationSource("Widgets"), new Contributor("widgets", "widgets:read"));

        await loop.StartAsync();
        await loop.UntilRegisteredAsync();

        ((ModulePermissionSet)loop.Client.Calls.Single().Payload).Permissions.Should().BeEmpty();
        loop.Client.Calls.Single().VerticalId.Should().Be("Widgets");
    }

    [Fact]
    public async Task TheNeutralOperatorSource_StillPublishesItsOrderTypesBeforeItsPermissions()
    {
        await using var loop = new RegistrationLoop();

        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions);
        loop.Client.Calls.Should().OnlyContain(call => call.VerticalId == NeutralOperatorModule.ModuleId);
    }

    [Fact]
    public void VerticalRegistrationSource_DefaultsToNoDescriptors()
    {
        new VerticalRegistrationSource(Vertical).OrderTypes.Should().BeNull();
    }

    [Fact]
    public void AddVerticalRegistration_RegistersTheLoop_TheStateTheSourceAndTheReadinessTaggedCheck()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["VerticalRegistration:InitialDelay"] = "00:00:03",
            ["VerticalRegistration:MaxDelay"] = "00:00:30",
            ["VerticalRegistration:RefreshInterval"] = "00:10:00",
        }).Build();
        var services = new ServiceCollection().AddSingleton(TimeProvider.System).AddLogging();
        var source = new VerticalRegistrationSource(Vertical);

        services.AddVerticalRegistration(configuration, source);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<VerticalRegistrationSource>().Should().BeSameAs(source);
        provider.GetRequiredService<VerticalRegistrationState>().Should().BeSameAs(provider.GetRequiredService<VerticalRegistrationState>());
        provider.GetServices<IHostedService>().Should().ContainSingle(s => s is VerticalRegistrationService);
        var options = provider.GetRequiredService<IOptions<VerticalRegistrationOptions>>().Value;
        options.InitialDelay.Should().Be(TimeSpan.FromSeconds(3));
        options.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        options.RefreshInterval.Should().Be(TimeSpan.FromMinutes(10));
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => string.Equals(r.Name, VerticalRegistrationHealthCheck.Name, StringComparison.Ordinal))
            .Which.Tags.Should().Contain(VerticalRegistrationHealthCheck.ReadinessTag);
    }

    [Fact]
    public void AddVerticalRegistration_WithoutConfigurationTheSchedule_IsTheDefault()
    {
        var services = new ServiceCollection();

        services.AddVerticalRegistration(new ConfigurationBuilder().Build(), new VerticalRegistrationSource(Vertical));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<VerticalRegistrationOptions>>().Value.Should().Be(new VerticalRegistrationOptions());
    }

    [Fact]
    public void AddVerticalRegistration_RefusesMissingArgumentsAndABlankVerticalId()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var source = new VerticalRegistrationSource(Vertical);

        ((Action)(() => VerticalRegistrationHostExtensions.AddVerticalRegistration(null!, configuration, source))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddVerticalRegistration(null!, source))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddVerticalRegistration(configuration, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddVerticalRegistration(configuration, new VerticalRegistrationSource(" ")))).Should().Throw<ArgumentException>();
        ((Action)(() => services.AddVerticalRegistration(configuration, new VerticalRegistrationSource("")))).Should().Throw<ArgumentException>();
        services.Should().BeEmpty("a refused call registers nothing");
    }

    [Fact]
    public void MapVerticalReadiness_MapsReadyzAnonymously_AndRefusesANullBuilder()
    {
        ((Action)(() => VerticalRegistrationHostExtensions.MapVerticalReadiness(null!))).Should().Throw<ArgumentNullException>();
        VerticalRegistrationHostExtensions.ReadinessRoute.Should().Be("/readyz");
    }

    [Fact]
    public async Task Readyz_Is503WhileTheRegistrationFails_And200OnceRegistered_WhileHealthzStays200()
    {
        var client = new FakeVerticalRegistrationClient { Fails = _ => true };
        var time = new SchedulingAwareTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication();
                    services.AddAuthorization(o => o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                    services.AddSingleton<TimeProvider>(time);
                    services.AddScoped<IVerticalRegistrationClient>(_ => client);
                    services.AddSingleton<IModulePermissionContributor>(new Contributor(Vertical, "widgets:read"));
                    services.AddHealthChecks();
                    services.AddVerticalRegistration(new ConfigurationBuilder().Build(), new VerticalRegistrationSource(Vertical));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHealthChecks("/healthz").AllowAnonymous();
                        endpoints.MapVerticalReadiness();
                    });
                });
            })
            .StartAsync();
        using var http = host.GetTestServer().CreateClient();

        await RegistrationLoop.UntilAsync(() => host.Services.GetRequiredService<VerticalRegistrationState>().Current.Status == VerticalRegistrationStatus.Failing);
        var failingReady = await http.GetAsync(new Uri("/readyz", UriKind.Relative));
        var failingHealth = await http.GetAsync(new Uri("/healthz", UriKind.Relative));

        client.Fails = _ => false;
        await time.AdvanceWhenScheduledAsync(new VerticalRegistrationOptions().MaxDelay);
        await RegistrationLoop.UntilAsync(() => host.Services.GetRequiredService<VerticalRegistrationState>().Current.Status == VerticalRegistrationStatus.Registered);
        var readyReady = await http.GetAsync(new Uri("/readyz", UriKind.Relative));

        failingReady.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        failingHealth.StatusCode.Should().Be(HttpStatusCode.OK, "a Degraded registration never fails liveness");
        readyReady.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
