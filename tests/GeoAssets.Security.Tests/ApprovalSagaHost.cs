using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The approval saga end to end (XD01-383), on a PostgreSQL database built by the production migrations of every history
/// and the registrations <c>GeoAssets.Server</c>'s <c>Program.cs</c> makes with no broker: the NeutralOperator module's
/// approval and saga, the kernel's asset-mutation consumer over the real catalog (written as a non-owner role, under row-level
/// security), the in-process transport the outbox relay delivers through — driven by the test, one batch at a time — and the
/// asset endpoints the map reads. The kernel's applier can be made to fail on command (<see cref="Fault"/>).
///
/// Each host gets a database of its own in the suite's container.
/// </summary>
internal sealed class ApprovalSagaHost : IAsyncDisposable
{
    public const string OwnersPole = "saga-pole-owner";
    public const string SecondPole = "saga-pole-second";
    public const string PoleType = "00000000-0000-0000-0000-000000000004";
    public const string OrganizationClaim = "test_org";
    public const string OrganizationHeader = "X-Test-Organization";

    public static readonly Guid Owner = new("a3a3a3a3-0000-4000-8000-000000000383");
    public static readonly Guid Other = new("a4a4a4a4-0000-4000-8000-000000000383");
    public static readonly Guid Occupant = new("a5a5a5a5-0000-4000-8000-000000000383");
    public static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly WorkflowPrincipal Approver = new("approver-1", Owner.ToString(), ["Supervisor"], [], []);

    private const double Lon = -38.5;
    private const double Lat = -12.9;

    private readonly WebApplication _web;
    private readonly bool _withSecondPole;

    private ApprovalSagaHost(WebApplication web, string connectionString, FaultInjectingApplier fault, bool withSecondPole)
    {
        _web = web;
        _withSecondPole = withSecondPole;
        ConnectionString = connectionString;
        Fault = fault;
    }

    /// <summary>The id of this host's order: unique, so the spans and rows of hosts running in parallel never mix.</summary>
    public string Order { get; } = $"saga-{Guid.NewGuid():N}"[..17];

    public string ConnectionString { get; }

    public FaultInjectingApplier Fault { get; }

    public IServiceProvider Services => _web.Services;

    public OutboxRelayProcessor Relay => Services.GetRequiredService<OutboxRelayProcessor>();

    /// <param name="poleInCatalog">Whether the order's pole is in the owner's catalog; without it the kernel cannot apply the batch (a rejection).</param>
    /// <param name="staleCatalogInstanceId">The catalog instance the approval addresses when it is not this host's (a misaddressed batch).</param>
    /// <param name="maxAttempts">The relay's delivery attempts per message before it leaves one failed.</param>
    /// <param name="withSecondPole">Whether the order also targets <see cref="SecondPole"/> (in the owner's catalog when <paramref name="poleInCatalog"/>).</param>
    public static async Task<ApprovalSagaHost> StartAsync(
        bool poleInCatalog = true, string? staleCatalogInstanceId = null, int? maxAttempts = null, bool withSecondPole = false)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"approval_saga_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
        {
            await geoAssets.Database.MigrateAsync();
            await geoAssets.Database.ExecuteSqlRawAsync($"""
                GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity_attribute TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON asset_type TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON applied_mutation_batches TO {SecurityTestContainerFixture.TenantScopedRole};
                """);
        }

        await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
            await identity.Database.MigrateAsync();
        await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
            await serviceOrders.Database.MigrateAsync();
        await SecurityTestContainerFixture.MigrateNeutralOperatorAsync(connectionString);

        await SeedCatalogAsync(connectionString, poleInCatalog, withSecondPole);

        var catalogConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
        }.ConnectionString;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        var configuration = builder.Configuration;
        var orderConfiguration = staleCatalogInstanceId is null
            ? (IConfiguration)configuration
            : new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [AssetCatalogConfiguration.InstanceIdKey] = staleCatalogInstanceId,
            }).Build();

        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        services.AddWarmedPostgresAssetProvider(connectionString);
        services.AddPostgresOrganizationAssetCatalogs(connectionString);
        services.AddServerOrganizationAssetCatalogs();
        services.AddGeoAssetsDbContextFactory(connectionString);
        services.AddPostgresAssetRenderQuery(connectionString);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor>(sp => new ClaimsPrincipalCurrentUserAccessor(
            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            new ClaimMapping { ObjectIdClaimTypes = ["oid"], OrganizationIdClaimTypes = [OrganizationClaim] }));
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<IHttpContextAccessor>().HttpContext is null
            ? new FixedTenant(Owner)
            : new HttpTenantContext(sp.GetRequiredService<ICurrentUserAccessor>()));
        services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddGeoAuthorizationPolicyBridge();
        services.AddScoped<IGeoAuthorizationService, PermittingAuthorizationService>();
        services.AddSingleton<IOrganizationGrantRepository, NoGrants>();

        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton(new GeoAssetsMeter("1.0.0"));
        services.AddOrderTypeRegistry(registry => registry.RegisterSharedUseOrderTypes());
        services.AddWorkflowPersistence(o => o.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
        services.AddNeutralOperatorPersistence();
        services.AddServiceOrderRules();
        services.AddServerOrderOccupancyStaging(orderConfiguration);
        services.AddServerWorkflowMessaging(configuration);
        services.AddServerOccupancyCommit(configuration);
        services.AddPostgresAssetMutationBatchApplier(catalogConnectionString);
        var applier = services.Last(d => d.ServiceType == typeof(IAssetMutationBatchApplier));
        services.Remove(applier);
        services.AddSingleton(sp => new FaultInjectingApplier((IAssetMutationBatchApplier)applier.ImplementationFactory!(sp)));
        services.AddSingleton<IAssetMutationBatchApplier>(sp => sp.GetRequiredService<FaultInjectingApplier>());
        services.AddServerAssetMutationApply(configuration);
        if (maxAttempts is { } attempts)
            services.Configure<OutboxRelayOptions>(o => o.MaxAttempts = attempts);
        var relay = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(OutboxRelayProcessor));
        services.Remove(relay);
        services.AddSingleton<OutboxRelayProcessor>();

        var web = builder.Build();
        await web.Services.WarmPostgresAssetProviderAsync();
        web.UseGeoAssetsAuthentication();
        web.UseOrganizationAssetCatalogs();
        web.MapGeoAssetsApi();
        await web.StartAsync();

        var host = new ApprovalSagaHost(web, connectionString, web.Services.GetRequiredService<FaultInjectingApplier>(), withSecondPole);
        await host.SeedOrderAsync();
        return host;
    }

    // ── Act ───────────────────────────────────────────────────────────────────

    public Task StageAsync(params OccupancyChange[] changes) => StageAsync(OwnersPole, changes);

    public async Task StageAsync(string pole, params OccupancyChange[] changes)
    {
        await using var scope = Services.CreateAsyncScope();
        var baseline = await scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>().GetForPoleAsync(pole, CancellationToken.None);
        var set = OccupancyChangeSet.Stage(
            Order, OrderTargetSelectionService.TargetId(Order, pole), pole, Owner, changes, baseline, "crew-1", new DateTimeOffset(SeedTime));
        await scope.ServiceProvider.GetRequiredService<IOccupancyChangeSetWriter>().StageAsync(set, CancellationToken.None);
    }

    public async Task<OccupancyCommitResult> ApproveAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOccupancyCommitService>()
            .ApproveAndCommitAsync(Order, Approver, CancellationToken.None);
    }

    public async Task<OccupancyApplySagaView> RetryAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOccupancyApplySagaService>().RetryAsync(Order, Approver, CancellationToken.None);
    }

    /// <summary>One batch of the outbox relay: what the hosted service does on each tick. Returns the messages it delivered.</summary>
    public Task<int> RelayAsync() => Relay.ProcessNextBatchAsync(CancellationToken.None);

    /// <summary>Makes every undelivered message due now, as a relay would find it once its backoff elapsed.</summary>
    public Task ExpireBackoffAsync() => ExecuteAsync("""UPDATE "WorkflowOutboxMessages" SET "NextRetryAtUtc" = "CreatedAtUtc" WHERE "Status" = 0""");

    /// <summary>The relay hands a message over again (a broker's redelivery).</summary>
    public Task MarkPendingAgainAsync(string messageType) => ExecuteAsync(
        $"""UPDATE "WorkflowOutboxMessages" SET "Status" = {(int)OutboxMessageStatus.Pending}, "DispatchedAtUtc" = NULL WHERE "MessageType" = '{messageType}'""");

    public HttpClient ClientFor(Guid organizationId)
    {
        var client = _web.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add(OrganizationHeader, organizationId.ToString());
        return client;
    }

    /// <summary>Puts <paramref name="entries"/> on the order's pole before the approval — what an earlier approval left.</summary>
    public Task OccupyAsync(params PoleOccupancyEntry[] entries) => OccupyAsync(OwnersPole, entries);

    /// <summary>Replaces <paramref name="pole"/>'s live entries through the real store, as any other order's commit would.</summary>
    public async Task<PoleOccupancy> OccupyAsync(string pole, params PoleOccupancyEntry[] entries)
    {
        await using var scope = Services.CreateAsyncScope();
        var live = await scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>().GetForPoleAsync(pole, CancellationToken.None);
        return await scope.ServiceProvider.GetRequiredService<IPoleOccupancyWriter>().ReplaceEntriesAsync(
            pole, live?.Version ?? PoleOccupancy.UnsavedVersion, entries, CancellationToken.None);
    }

    // ── Observe ───────────────────────────────────────────────────────────────

    public async Task<ServiceOrder> OrderAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return (ServiceOrder)(await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().GetByIdAsync(Order))!;
    }

    public async Task<OccupancyApplySaga?> SagaAsync(int? attempt = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var sagas = scope.ServiceProvider.GetRequiredService<IOccupancyApplySagaStore>();
        return attempt is { } n
            ? await sagas.FindAsync(OccupancyCommitService.AssetMutationBatchId(Order, n), CancellationToken.None)
            : await sagas.FindLatestForOrderAsync(Order, CancellationToken.None);
    }

    public async Task<IReadOnlyList<OccupancyChangeSet>> SetsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IOccupancyChangeSetReader>()
            .GetPageAsync(new OccupancyChangeSetQuery(Order, pageSize: OccupancyChangeSetQuery.MaxPageSize), CancellationToken.None)).Items;
    }

    public Task<PoleOccupancy?> OccupancyAsync() => OccupancyAsync(OwnersPole);

    public async Task<PoleOccupancy?> OccupancyAsync(string pole)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>().GetForPoleAsync(pole, CancellationToken.None);
    }

    /// <summary>The pole as its owner's catalog reads it.</summary>
    public async Task<GeoFeature?> CatalogPoleAsync()
    {
        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(ConnectionString);
        await using var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options, new FixedTenant(Owner)), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        return catalog.GetById(OwnersPole);
    }

    /// <summary>The pole as the owner's map reads it: through the asset endpoints.</summary>
    public async Task<GeoFeature> MapPoleAsync()
    {
        using var client = ClientFor(Owner);
        using var response = await client.GetAsync(new Uri($"/api/geoassets/features/{OwnersPole}", UriKind.Relative));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        return JsonSerializer.Deserialize<GeoFeature>(await response.Content.ReadAsStringAsync(), GeoJsonSerializer.Options)!;
    }

    public async Task<IReadOnlyList<OutboxRow>> OutboxAsync(string messageType)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Id", "MessageType", "Status", "AttemptCount", "LastError", "CreatedAtUtc", "TraceParent"
            FROM "WorkflowOutboxMessages" WHERE "MessageType" = @type ORDER BY "CreatedAtUtc", "Id"
            """;
        command.Parameters.AddWithValue("type", messageType);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<OutboxRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new OutboxRow(
                reader.GetGuid(0), reader.GetString(1), (OutboxMessageStatus)reader.GetInt32(2), reader.GetInt32(3),
                await reader.IsDBNullAsync(4) ? null : reader.GetString(4), reader.GetDateTime(5), await reader.IsDBNullAsync(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    public async Task<long> AppliedBatchesAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM applied_mutation_batches";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task<long> InboxRecordsAsync(string consumerName)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM "WorkflowInboxProcessedMessages" WHERE "ConsumerName" = @consumer""";
        command.Parameters.AddWithValue("consumer", consumerName);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>The batch the approval staged, as the outbox carries it.</summary>
    public async Task<AssetMutationBatchRequestedEvent> StagedBatchAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Payload" FROM "WorkflowOutboxMessages" WHERE "MessageType" = @type ORDER BY "CreatedAtUtc" LIMIT 1""";
        command.Parameters.AddWithValue("type", IntegrationEventTypes.AssetMutationBatchRequestedV1);
        var payload = (byte[])(await command.ExecuteScalarAsync())!;
        return new GeoAssets.Workflow.Messaging.Protobuf.ProtobufIntegrationMessageSerializer().Deserialize<AssetMutationBatchRequestedEvent>(payload);
    }

    public async Task<AssetMutationBatchRejectedEvent> StagedRejectionAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Payload" FROM "WorkflowOutboxMessages" WHERE "MessageType" = @type ORDER BY "CreatedAtUtc" LIMIT 1""";
        command.Parameters.AddWithValue("type", IntegrationEventTypes.AssetMutationBatchRejectedV1);
        var payload = (byte[])(await command.ExecuteScalarAsync())!;
        return new GeoAssets.Workflow.Messaging.Protobuf.ProtobufIntegrationMessageSerializer().Deserialize<AssetMutationBatchRejectedEvent>(payload);
    }

    public static OccupancyChange Add(int slot) => new()
    {
        Kind = OccupancyChangeKind.Add,
        SlotIndex = slot,
        EntryId = Guid.NewGuid(),
        Status = PoleAttachmentOccupantStatus.AuthorizedContracted,
        OccupantOrganizationId = Occupant,
        OperatorCnpj = "98.765.432/0001-10",
        CableDiameterMm = 9.5m,
        CableLoadDaN = 80m,
    };

    public async ValueTask DisposeAsync()
    {
        await _web.StopAsync();
        await _web.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    // ── Arrange ───────────────────────────────────────────────────────────────

    private async Task SeedOrderAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().AddAsync(new ServiceOrder
        {
            Id = Order,
            Title = "Pole work",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            Status = ServiceOrderStatus.Completed,
            CreatedBy = "planner-1",
            OrganizationId = Owner,
            CreatedAt = SeedTime,
        });
        string[] poles = _withSecondPole ? [OwnersPole, SecondPole] : [OwnersPole];
        await scope.ServiceProvider.GetRequiredService<IServiceOrderTargetWriter>().AddRangeAsync(
            Order,
            [
                .. poles.Select(pole => new ServiceOrderTarget
                {
                    Id = OrderTargetSelectionService.TargetId(Order, pole),
                    OrderId = Order,
                    FeatureId = pole,
                    AssetTypeId = PoleType,
                    ActivityIds = ["relocate-attachment"],
                    Status = OrderTargetStatus.Pending,
                    AddedAt = SeedTime,
                    AddedBy = "planner-1",
                    OrganizationId = Owner,
                }),
            ],
            CancellationToken.None);
    }

    private static async Task SeedCatalogAsync(string connectionString, bool poleInCatalog, bool withSecondPole)
    {
        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString);
        await using var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        // A pole of another organization keeps the catalog non-empty when the order's own is missing.
        catalog.AddRange(poleInCatalog
            ? withSecondPole ? [Pole(OwnersPole, Owner, 0.01), Pole(SecondPole, Owner, 0.03)] : [Pole(OwnersPole, Owner, 0.01)]
            : [Pole("saga-pole-other", Other, 0.02)]);
    }

    private static GeoFeature Pole(string id, Guid organizationId, double offset) => new()
    {
        Id = id,
        Geometry = new GeoPoint(Lon + offset, Lat + offset),
        Properties = new GeoFeatureProperties
        {
            Name = id,
            AssetTypeId = PoleType,
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = $"P-{id}" },
        },
    };

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var db = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(ConnectionString));
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(OrganizationHeader, out var organization))
                return Task.FromResult(AuthenticateResult.NoResult());

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", "user-1"), new Claim(OrganizationClaim, organization.ToString())], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class PermittingAuthorizationService(ITenantContext tenant) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = "user@example.com",
                    DisplayName = "User",
                    CreatedAt = SeedTime,
                    OrganizationId = tenant.CurrentOrganizationId == Guid.Empty ? null : tenant.CurrentOrganizationId,
                },
                Roles = [],
                Claims = [],
                Permissions = [],
            });
    }

    private sealed class NoGrants : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
