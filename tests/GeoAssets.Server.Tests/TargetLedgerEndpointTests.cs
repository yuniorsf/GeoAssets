using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The global target ledger routes (XD01-379) over the real <c>MapTargetLedgerApi()</c> in a <see cref="TestServer"/>: only a
/// service caller may use them, an order's targets are keyed by (vertical, order) and scoped to the organization the
/// request body names, and a reservation blocked by another order's exclusive hold is 409 <c>targets.conflict</c>.
/// The store, outbox and inbox are in-memory; the EF store and PostgreSQL are covered end to end in GeoAssets.Security.Tests.
/// </summary>
public sealed class TargetLedgerEndpointTests
{
    private const string RolesHeader = "X-Test-Roles";
    private const string ServiceCaller = "GeoAssets.ServiceCaller";
    private const string Base = "/api/targets/ledger";

    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000379");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000379");

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "caller"), new("azp", "vertical-a-service") };
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }

    private sealed class LedgerApp : IAsyncDisposable
    {
        private IHost? _host;

        public InMemoryTargetLedgerStore Store { get; } = new();

        public RecordingOutboxWriter Outbox { get; } = new();

        public InMemoryOrderTypeDescriptorRegistry Descriptors { get; } = new([]);

        public HttpClient Client { get; private set; } = null!;

        public static async Task<LedgerApp> StartAsync()
        {
            var app = new LedgerApp();
            app.Descriptors.Upsert("vertical-a", [Descriptor("vertical-a", "a-type", OrderTargetExclusivity.BlockOnOverlap), Descriptor("vertical-a", "shared-type", OrderTargetExclusivity.None), new OrderTypeDescriptor
            {
                VerticalId = "vertical-a", OrderTypeId = "plain-type", DisplayNameKey = "k", ApiBaseRoute = "/r",
            }]);
            app.Descriptors.Upsert("vertical-b", [Descriptor("vertical-b", "b-type", OrderTargetExclusivity.BlockOnOverlap)]);

            GeoFeature[] catalog =
            [
                .. Enumerable.Range(1, 5).Select(i => TargetLedgerServiceHarness.Feature($"f{i}", OrgX)),
                TargetLedgerServiceHarness.Feature("g1", OrgY),
                TargetLedgerServiceHarness.Feature("shared", OrgX),
                TargetLedgerServiceHarness.Feature("shared", OrgY),
            ];
            var clock = new FakeTimeProvider(TargetLedgerServiceHarness.Now);

            app._host = await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
                        services.AddAuthorization();
                        services.AddSingleton<ITargetLedgerStore>(app.Store);
                        services.AddSingleton<IWorkflowUnitOfWork>(new InMemoryWorkflowUnitOfWork());
                        services.AddSingleton<IOutboxWriter>(app.Outbox);
                        services.AddSingleton<IIdempotentInbox>(new RecordingIdempotentInbox());
                        services.AddSingleton<IOrderTypeDescriptorRegistry>(app.Descriptors);
                        services.AddSingleton<TimeProvider>(clock);
                        services.AddSingleton(sp => new FeatureSelectionRegistry(clock, "no-such-plugins-directory", typeof(FeatureSelectionRegistry).Assembly));
                        services.AddSingleton(sp => new BoundedFeatureSelector(sp.GetRequiredService<FeatureSelectionRegistry>(), new CatalogSpatialQuery(catalog), null, clock));
                        services.AddScoped<ITenantOrganizationScope, TenantOrganizationScope>();
                        services.AddScoped(sp => new TargetLedgerService(
                            TargetLedgerServiceHarness.CreateSelection(sp.GetRequiredService<BoundedFeatureSelector>(), clock),
                            sp.GetRequiredService<ITargetLedgerStore>(),
                            sp.GetRequiredService<IWorkflowUnitOfWork>(),
                            sp.GetRequiredService<IOutboxWriter>(),
                            sp.GetRequiredService<IIdempotentInbox>(),
                            sp.GetRequiredService<IOrderTypeDescriptorRegistry>(),
                            clock,
                            sp.GetRequiredService<ITenantOrganizationScope>()));
                    });
                    webHost.Configure(web =>
                    {
                        web.UseRouting();
                        web.UseAuthentication();
                        web.UseAuthorization();
                        web.UseEndpoints(endpoints => endpoints.MapTargetLedgerApi());
                    });
                })
                .StartAsync();
            app.Client = app._host.GetTestServer().CreateClient();
            return app;
        }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
    }

    private static OrderTypeDescriptor Descriptor(string vertical, string type, OrderTargetExclusivity exclusivity) => new()
    {
        VerticalId = vertical,
        OrderTypeId = type,
        DisplayNameKey = "k",
        ApiBaseRoute = "/r",
        TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = exclusivity },
    };

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body = null, string? roles = ServiceCaller, string? organizationHeader = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (roles is not null) request.Headers.Add(RolesHeader, roles);
        if (organizationHeader is not null) request.Headers.Add("X-Organization-Id", organizationHeader);
        return request;
    }

    private static object Reserve(Guid? org, string orderType = "a-type", string[]? ids = null, bool preview = false, string? actor = null) => new
    {
        organizationId = org,
        orderTypeId = orderType,
        strategyId = "identifier-list",
        parameters = new { attributeKey = "id", values = ids ?? ["f1", "f2"] },
        preview,
        actor,
    };

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(LedgerApp app, HttpRequestMessage request)
    {
        using var response = await app.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        request.Dispose();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static bool NullOrMissing(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    private static Task<(HttpStatusCode Status, JsonElement Body)> ReserveAsync(
        LedgerApp app, string vertical, string order, object body, string? roles = ServiceCaller) =>
        SendAsync(app, Request(HttpMethod.Post, $"{Base}/{vertical}/{order}/reserve", body, roles));

    private static Task<(HttpStatusCode Status, JsonElement Body)> ChangeAsync(
        LedgerApp app, string verb, string vertical, string order, object body) =>
        SendAsync(app, Request(HttpMethod.Post, $"{Base}/{vertical}/{order}/{verb}", body));

    private static Task<(HttpStatusCode Status, JsonElement Body)> UsagesAsync(LedgerApp app, string vertical, string order, string query) =>
        SendAsync(app, Request(HttpMethod.Get, $"{Base}/{vertical}/{order}/usages{query}"));

    // ── Authorization ─────────────────────────────────────────────────────────

    public static TheoryData<string, string> Routes => new()
    {
        { "POST", "reserve" }, { "POST", "release" }, { "POST", "commit" }, { "GET", "usages" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Routes_Anonymous_Return401(string method, string route)
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, _) = await SendAsync(app, Request(new HttpMethod(method), $"{Base}/vertical-a/a1/{route}?organizationId={OrgX}", new { organizationId = OrgX }, roles: null));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Routes_UserTokenEvenAdministrator_Return403(string method, string route)
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, _) = await SendAsync(app, Request(new HttpMethod(method), $"{Base}/vertical-a/a1/{route}?organizationId={OrgX}", Reserve(OrgX), roles: "Administrator,Supervisor"));

        status.Should().Be(HttpStatusCode.Forbidden);
        app.Store.Rows.Should().BeEmpty();
        app.Store.Calls.Should().Be(0);
    }

    // ── Reserve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reserve_Preview_ReportsWhatItWouldAddAndPersistsNothing_ThenApplyReservesAndStagesTheEvent()
    {
        await using var app = await LedgerApp.StartAsync();

        var (previewStatus, preview) = await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, preview: true));
        var previewWrote = app.Store.Rows.Count;
        var previewStaged = app.Outbox.Messages.Count;
        var (applyStatus, applied) = await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        previewStatus.Should().Be(HttpStatusCode.OK);
        preview.GetProperty("matched").GetInt32().Should().Be(2);
        preview.GetProperty("firstPage").GetProperty("totalCount").GetInt32().Should().Be(2);
        previewWrote.Should().Be(0);
        previewStaged.Should().Be(0);
        applyStatus.Should().Be(HttpStatusCode.OK);
        applied.GetProperty("added").GetInt32().Should().Be(2);
        app.Store.Rows.Should().HaveCount(2).And.OnlyContain(r => r.Target.VerticalId == "vertical-a" && r.Target.OrderId == "a1" && r.Target.OrganizationId == OrgX);
        app.Outbox.Messages.Should().ContainSingle().Which.PartitionKey.Should().Be("vertical-a/a1");
    }

    [Fact]
    public async Task Reserve_RecordsTheBodysActor_OtherwiseTheCallingServicesIdentity()
    {
        await using var app = await LedgerApp.StartAsync();

        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, ids: ["f1"], actor: "alice"));
        await ReserveAsync(app, "vertical-a", "a2", Reserve(OrgX, orderType: "shared-type", ids: ["f2"]));

        app.Store.Rows.Select(r => r.Target.AddedBy).Should().Equal("alice", "vertical-a-service");
    }

    [Fact]
    public async Task Reserve_Repeat_IsIdempotent_AddedZeroAndNoSecondEvent()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var (status, body) = await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("added").GetInt32().Should().Be(0);
        app.Store.Rows.Should().HaveCount(2);
        app.Outbox.Messages.Should().ContainSingle();
    }

    public static TheoryData<string, string> InvalidKeys => new()
    {
        { "kernel", "a1" },
        { "Bad_Vertical", "a1" },
        { "-leading", "a1" },
        { "double--hyphen", "a1" },
        { new string('v', 65), "a1" },
        { "vertical-a", new string('o', 37) },
    };

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task Routes_InvalidVerticalOrOrderId_Return400ValidationFailed(string vertical, string orderId)
    {
        await using var app = await LedgerApp.StartAsync();

        var reserve = await ReserveAsync(app, vertical, orderId, Reserve(OrgX));
        var release = await ChangeAsync(app, "release", vertical, orderId, new { organizationId = OrgX });
        var commit = await ChangeAsync(app, "commit", vertical, orderId, new { organizationId = OrgX });
        var usages = await UsagesAsync(app, vertical, orderId, $"?organizationId={OrgX}");

        foreach (var (status, body) in new[] { reserve, release, commit, usages })
        {
            status.Should().Be(HttpStatusCode.BadRequest);
            Code(body).Should().Be("validation.failed");
        }

        app.Store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Routes_VerticalAndOrderIdAtTheLengthLimits_AreAccepted()
    {
        await using var app = await LedgerApp.StartAsync();
        var vertical = new string('v', TargetLedger.MaxVerticalIdLength);
        var order = new string('o', TargetLedger.MaxOrderIdLength);

        var (status, _) = await UsagesAsync(app, vertical, order, $"?organizationId={OrgX}");

        status.Should().Be(HttpStatusCode.NotFound, "the keys are well formed; the order simply holds nothing");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Routes_MissingOrEmptyOrganizationId_Return400(string? organization)
    {
        await using var app = await LedgerApp.StartAsync();
        Guid? org = organization is null ? null : Guid.Parse(organization);

        var reserve = await ReserveAsync(app, "vertical-a", "a1", Reserve(org));
        var release = await ChangeAsync(app, "release", "vertical-a", "a1", new { organizationId = org });
        var commit = await ChangeAsync(app, "commit", "vertical-a", "a1", new { organizationId = org });
        var usages = await UsagesAsync(app, "vertical-a", "a1", organization is null ? string.Empty : $"?organizationId={organization}");

        foreach (var (status, body) in new[] { reserve, release, commit, usages })
        {
            status.Should().Be(HttpStatusCode.BadRequest);
            Code(body).Should().Be("validation.failed");
        }

        app.Store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Routes_OrganizationIdSentAsAHeader_IsIgnored_StillMissingFromTheBody()
    {
        await using var app = await LedgerApp.StartAsync();

        var reserve = await SendAsync(app, Request(HttpMethod.Post, $"{Base}/vertical-a/a1/reserve", Reserve(null), organizationHeader: OrgX.ToString()));
        var release = await SendAsync(app, Request(HttpMethod.Post, $"{Base}/vertical-a/a1/release", new { }, organizationHeader: OrgX.ToString()));
        var usages = await SendAsync(app, Request(HttpMethod.Get, $"{Base}/vertical-a/a1/usages", organizationHeader: OrgX.ToString()));

        reserve.Status.Should().Be(HttpStatusCode.BadRequest);
        release.Status.Should().Be(HttpStatusCode.BadRequest);
        usages.Status.Should().Be(HttpStatusCode.BadRequest);
        app.Store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Reserve_MissingOrderTypeOrStrategy_Returns400()
    {
        await using var app = await LedgerApp.StartAsync();

        var noType = await ReserveAsync(app, "vertical-a", "a1", new { organizationId = OrgX, strategyId = "identifier-list" });
        var noStrategy = await ReserveAsync(app, "vertical-a", "a1", new { organizationId = OrgX, orderTypeId = "a-type" });

        noType.Status.Should().Be(HttpStatusCode.BadRequest);
        noStrategy.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reserve_InvalidPaging_Returns400()
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, _) = await ReserveAsync(app, "vertical-a", "a1", new
        {
            organizationId = OrgX, orderTypeId = "a-type", strategyId = "identifier-list", pageSize = 0,
            parameters = new { attributeKey = "id", values = new[] { "f1" } },
        });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reserve_UnknownStrategy_Returns400UnknownStrategy()
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, body) = await ReserveAsync(app, "vertical-a", "a1", new { organizationId = OrgX, orderTypeId = "a-type", strategyId = "no-such-strategy" });

        status.Should().Be(HttpStatusCode.BadRequest);
        Code(body).Should().Be("selection.unknown-strategy");
    }

    [Fact]
    public async Task Reserve_MissingStrategyParameter_Returns400InvalidParameters()
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, body) = await ReserveAsync(app, "vertical-a", "a1", new { organizationId = OrgX, orderTypeId = "a-type", strategyId = "identifier-list", parameters = new { } });

        status.Should().Be(HttpStatusCode.BadRequest);
        Code(body).Should().Be("selection.invalid-parameters");
    }

    [Fact]
    public async Task Reserve_MalformedJson_Returns400()
    {
        await using var app = await LedgerApp.StartAsync();
        using var request = Request(HttpMethod.Post, $"{Base}/vertical-a/a1/reserve", roles: ServiceCaller);
        request.Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");

        using var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reserve_UnknownOrderType_Returns404OrderTypeNotFound_AndWritesNothing()
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, body) = await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, orderType: "no-such-type"));
        var (otherVertical, otherBody) = await ReserveAsync(app, "vertical-b", "b1", Reserve(OrgX, orderType: "a-type"));

        status.Should().Be(HttpStatusCode.NotFound);
        Code(body).Should().Be("order-types.not-found");
        otherVertical.Should().Be(HttpStatusCode.NotFound, "a type is published by one vertical; another cannot reserve under it");
        Code(otherBody).Should().Be("order-types.not-found");
        app.Store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Reserve_OrderTypeWithoutATargetPolicy_Returns422NoTargetPolicy()
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, body) = await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, orderType: "plain-type"));

        status.Should().Be((HttpStatusCode)422);
        Code(body).Should().Be("orders.no-target-policy");
        app.Store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Reserve_BlockedByAnotherVerticalsExclusiveHold_Returns409WithTheConflictsBody_AndWritesNothingForTheCaller()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, ids: ["f1", "f2"]));

        var (status, body) = await ReserveAsync(app, "vertical-b", "b1", Reserve(OrgX, orderType: "b-type", ids: ["f1", "f3"]));

        status.Should().Be(HttpStatusCode.Conflict);
        Code(body).Should().Be("targets.conflict");
        body.GetProperty("orderId").GetString().Should().Be("b1");
        var conflict = body.GetProperty("conflicts").EnumerateArray().Should().ContainSingle().Subject;
        conflict.GetProperty("featureId").GetString().Should().Be("f1");
        conflict.GetProperty("verticalId").GetString().Should().Be("vertical-a");
        conflict.GetProperty("orderId").GetString().Should().Be("a1");
        conflict.GetProperty("orderTypeId").GetString().Should().Be("a-type");
        conflict.TryGetProperty("exclusivity", out _).Should().BeTrue();
        app.Store.Rows.Should().HaveCount(2).And.OnlyContain(r => r.Target.VerticalId == "vertical-a");
        app.Outbox.Messages.Should().ContainSingle("nothing is staged for the refused reservation");
        (await UsagesAsync(app, "vertical-b", "b1", $"?organizationId={OrgX}")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reserve_ConflictWithAnotherOrganizationsHolder_Returns409WithoutItsOrder()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgY, ids: ["shared"]));

        var (status, body) = await ReserveAsync(app, "vertical-b", "b1", Reserve(OrgX, orderType: "b-type", ids: ["shared"]));

        status.Should().Be(HttpStatusCode.Conflict);
        var conflict = body.GetProperty("conflicts").EnumerateArray().Should().ContainSingle().Subject;
        conflict.GetProperty("featureId").GetString().Should().Be("shared");
        conflict.GetProperty("verticalId").GetString().Should().Be("vertical-a");
        NullOrMissing(conflict, "orderId").Should().BeTrue("another organization's order is not named");
        NullOrMissing(conflict, "orderTypeId").Should().BeTrue();
        app.Store.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Reserve_AFeatureOfAnotherOrganization_MatchesNothing()
    {
        await using var app = await LedgerApp.StartAsync();

        var (status, body) = await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, ids: ["g1"]));

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("matched").GetInt32().Should().Be(0);
        app.Store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Reserve_SharedPolicyAnywhere_DoesNotBlock()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, orderType: "shared-type", ids: ["f1"]));

        var (status, body) = await ReserveAsync(app, "vertical-a", "a2", Reserve(OrgX, orderType: "shared-type", ids: ["f1"]));

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("added").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Reserve_AnotherOrganizationsOrder_Returns404OrderNotFound_AndWritesNothing()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, ids: ["f1"]));

        var (status, body) = await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgY, ids: ["g1"]));

        status.Should().Be(HttpStatusCode.NotFound);
        Code(body).Should().Be("orders.not-found");
        app.Store.Rows.Should().ContainSingle().Which.Target.OrganizationId.Should().Be(OrgX);
    }

    // ── Release and commit ────────────────────────────────────────────────────

    [Fact]
    public async Task Release_ReleasesTheOrdersTargets_IsIdempotent_AndStagesOneEvent()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var (status, body) = await ChangeAsync(app, "release", "vertical-a", "a1", new { organizationId = OrgX });
        var (againStatus, again) = await ChangeAsync(app, "release", "vertical-a", "a1", new { organizationId = OrgX });

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("verticalId").GetString().Should().Be("vertical-a");
        body.GetProperty("orderId").GetString().Should().Be("a1");
        body.GetProperty("changed").GetInt32().Should().Be(2);
        body.GetProperty("targets").EnumerateArray().Select(t => t.GetProperty("featureId").GetString()).Should().BeEquivalentTo("f1", "f2");
        againStatus.Should().Be(HttpStatusCode.OK);
        again.GetProperty("changed").GetInt32().Should().Be(0);
        again.GetProperty("targets").GetArrayLength().Should().Be(0);
        app.Outbox.Messages.Select(m => m.MessageType).Should().Equal(
            "geoassets.targets.targets-reserved.v1", "geoassets.targets.targets-released.v1");
        app.Store.Rows.Should().OnlyContain(r => r.ReleasedAt != null);
    }

    [Fact]
    public async Task Release_BySubsetOfTargetIds_ReleasesOnlyThose()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var (_, body) = await ChangeAsync(app, "release", "vertical-a", "a1", new { organizationId = OrgX, targetIds = new[] { TargetLedger.TargetId("vertical-a", "a1", "f2") } });

        body.GetProperty("changed").GetInt32().Should().Be(1);
        app.Store.Rows.Count(r => r.ReleasedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task Release_ReleasedFeature_CanBeReservedByAnotherVertical()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, ids: ["f1"]));
        await ChangeAsync(app, "release", "vertical-a", "a1", new { organizationId = OrgX });

        var (status, _) = await ReserveAsync(app, "vertical-b", "b1", Reserve(OrgX, orderType: "b-type", ids: ["f1"]));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Commit_MovesTargetsToDone_IsIdempotent_AndTheTargetStillBlocks()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var (status, body) = await ChangeAsync(app, "commit", "vertical-a", "a1", new { organizationId = OrgX });
        var (_, again) = await ChangeAsync(app, "commit", "vertical-a", "a1", new { organizationId = OrgX });
        var blocked = await ReserveAsync(app, "vertical-b", "b1", Reserve(OrgX, orderType: "b-type", ids: ["f1"]));

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("changed").GetInt32().Should().Be(2);
        again.GetProperty("changed").GetInt32().Should().Be(0);
        app.Store.Rows.Should().OnlyContain(r => r.Target.Status == OrderTargetStatus.Done && r.ReleasedAt == null);
        app.Outbox.Messages.Select(m => m.MessageType).Should().Equal(
            "geoassets.targets.targets-reserved.v1", "geoassets.targets.targets-committed.v1");
        blocked.Status.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("commit")]
    public async Task ReleaseAndCommit_UnknownOrderOrAnotherOrganizationsOrder_Return404_AndChangeNothing(string verb)
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var other = await ChangeAsync(app, verb, "vertical-a", "a1", new { organizationId = OrgY });
        var unknown = await ChangeAsync(app, verb, "vertical-a", "nope", new { organizationId = OrgX });
        var otherVertical = await ChangeAsync(app, verb, "vertical-b", "a1", new { organizationId = OrgX });

        foreach (var (status, body) in new[] { other, unknown, otherVertical })
        {
            status.Should().Be(HttpStatusCode.NotFound);
            Code(body).Should().Be("orders.not-found");
        }

        app.Store.Rows.Should().OnlyContain(r => r.ReleasedAt == null && r.Target.Status == OrderTargetStatus.Pending);
        app.Outbox.Messages.Should().ContainSingle();
    }

    [Theory]
    [InlineData("release")]
    [InlineData("commit")]
    public async Task ReleaseAndCommit_MoreThan500TargetIds_Return400(string verb)
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var (status, body) = await ChangeAsync(app, verb, "vertical-a", "a1", new { organizationId = OrgX, targetIds = Enumerable.Range(0, 501).Select(i => $"t{i}").ToArray() });
        var (emptyStatus, _) = await ChangeAsync(app, verb, "vertical-a", "a1", new { organizationId = OrgX, targetIds = Array.Empty<string>() });
        var exactly500 = await ChangeAsync(app, verb, "vertical-a", "a1", new { organizationId = OrgX, targetIds = Enumerable.Range(0, 500).Select(i => $"t{i}").ToArray() });

        status.Should().Be(HttpStatusCode.BadRequest);
        Code(body).Should().Be("validation.failed");
        emptyStatus.Should().Be(HttpStatusCode.BadRequest);
        exactly500.Status.Should().Be(HttpStatusCode.OK);
        exactly500.Body.GetProperty("changed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ReleaseAndCommit_OverlongActor_Returns400()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var (status, _) = await ChangeAsync(app, "release", "vertical-a", "a1", new { organizationId = OrgX, actor = new string('x', 257) });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Usages ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Usages_PagesTheOrdersTargetsWithATotal_ReleasedOnesIncluded()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX, ids: ["f1", "f2", "f3", "f4", "f5"]));
        await ChangeAsync(app, "release", "vertical-a", "a1", new { organizationId = OrgX, targetIds = new[] { TargetLedger.TargetId("vertical-a", "a1", "f1") } });

        var (status, first) = await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgX}&skip=0&take=2");
        var (_, last) = await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgX}&skip=4&take=2");
        var (_, defaults) = await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgX}");

        status.Should().Be(HttpStatusCode.OK);
        first.GetProperty("totalCount").GetInt32().Should().Be(5);
        first.GetProperty("skip").GetInt32().Should().Be(0);
        first.GetProperty("take").GetInt32().Should().Be(2);
        first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("target").GetProperty("featureId").GetString()).Should().Equal("f1", "f2");
        NullOrMissing(first.GetProperty("items")[0], "releasedAt").Should().BeFalse();
        NullOrMissing(first.GetProperty("items")[1], "releasedAt").Should().BeTrue();
        last.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("target").GetProperty("featureId").GetString()).Should().Equal("f5");
        defaults.GetProperty("take").GetInt32().Should().Be(TargetLedgerRestApiExtensions.DefaultTake);
        defaults.GetProperty("items").GetArrayLength().Should().Be(5);
    }

    [Theory]
    [InlineData("&take=0")]
    [InlineData("&take=1001")]
    [InlineData("&take=-5")]
    [InlineData("&take=abc")]
    [InlineData("&skip=-1")]
    [InlineData("&skip=abc")]
    public async Task Usages_InvalidPaging_Returns400PagingOutOfRange(string query)
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var (status, body) = await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgX}{query}");

        status.Should().Be(HttpStatusCode.BadRequest);
        Code(body).Should().Be("paging.out-of-range");
    }

    [Fact]
    public async Task Usages_TakeAtTheLimits_IsAccepted()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        (await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgX}&take=1")).Status.Should().Be(HttpStatusCode.OK);
        (await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgX}&take=1000")).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Usages_AnotherOrganizationsOrUnknownOrder_Returns404()
    {
        await using var app = await LedgerApp.StartAsync();
        await ReserveAsync(app, "vertical-a", "a1", Reserve(OrgX));

        var other = await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgY}");
        var unknown = await UsagesAsync(app, "vertical-a", "nope", $"?organizationId={OrgX}");
        var pastTheEnd = await UsagesAsync(app, "vertical-a", "a1", $"?organizationId={OrgX}&skip=100");

        other.Status.Should().Be(HttpStatusCode.NotFound);
        Code(other.Body).Should().Be("orders.not-found");
        unknown.Status.Should().Be(HttpStatusCode.NotFound);
        pastTheEnd.Status.Should().Be(HttpStatusCode.OK, "the order exists; a page past its end is simply empty");
        pastTheEnd.Body.GetProperty("items").GetArrayLength().Should().Be(0);
        pastTheEnd.Body.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    [Fact]
    public void MapTargetLedgerApi_NullRoutes_Throws()
    {
        var act = () => TargetLedgerRestApiExtensions.MapTargetLedgerApi(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Limits_AreTheDocumentedOnes()
    {
        TargetLedgerRestApiExtensions.DefaultTake.Should().Be(100);
        TargetLedgerRestApiExtensions.MaxTake.Should().Be(1000);
    }
}
