using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-337 end to end, on a PostgreSQL database built by the production migrations of all three
/// histories, with the registrations <c>GeoAssets.Server</c>'s <c>Program.cs</c> makes and no
/// broker configured: approving a pole-work order commits its staged occupancy (XD01-318), whose
/// asset mutation batch the in-process outbox relay applies to the poles — so the poles carry
/// their new occupancy class and flags, and the thematic layer rule seeded for that class styles
/// them. The catalog is written as a non-owner role, under the Row-Level Security of
/// <c>geo_entity</c>, <c>geo_entity_attribute</c> and <c>applied_mutation_batches</c>.
///
/// Each test gets a database of its own in the suite's container.
/// </summary>
public sealed class OccupancyApprovalAppliedToTheCatalogTests
{
    private const string Order = "oac-order";
    private const string LightPole = "oac-pole-light";
    private const string CrowdedPole = "oac-pole-crowded";
    private const string PoleType = "00000000-0000-0000-0000-000000000004";

    private static readonly Guid Owner = new("a3a3a3a3-0000-4000-8000-000000000337");
    private static readonly Guid Occupant = new("a4a4a4a4-0000-4000-8000-000000000337");
    private static readonly Guid DefaultPoleLayerId = Guid.Parse("00000000-0000-0000-0001-000000000001");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly WorkflowPrincipal Approver = new("approver-1", Owner.ToString(), ["Supervisor"], [], []);

    [Fact]
    public async Task ApprovingAPoleWorkOrder_WithoutABroker_WritesEachPolesOccupancyClassAndFlags_TheSeededThematicRuleStylesThem_AndARedeliveryChangesNothing()
    {
        await using var host = await HostAsync();
        await SeedAsync(host, [LightPole, CrowdedPole]);
        await StageAsync(host, LightPole, Add(1));
        await StageAsync(host, CrowdedPole, [.. Enumerable.Range(1, PoleAttachmentSlotMatrix.MaxSlots).Select(slot => Add(slot)), Add(slot: null)]);
        var notification = await StageUnrelatedNotificationAsync(host);

        var result = await ApproveAsync(host);
        var dispatched = await Relay(host).ProcessNextBatchAsync(CancellationToken.None);

        result.PolesUpdated.Should().Be(2);
        dispatched.Should().Be(1);
        var light = (await PoleAsync(host, LightPole))!;
        var crowded = (await PoleAsync(host, CrowdedPole))!;
        light.Properties.CustomAttributes.Should().Contain(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OccupancyAssetAttributes.ClassKey] = "Low",
            [OccupancyAssetAttributes.OccupiedCountKey] = "1",
            [OccupancyAssetAttributes.OverCapacityKey] = "false",
            [OccupancyAssetAttributes.UnidentifiedKey] = "false",
        });
        light.Properties.CustomAttributes.Should().Contain("plate", "P-oac-pole-light", "attributes the batch does not name are kept");
        crowded.Properties.CustomAttributes.Should().Contain(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OccupancyAssetAttributes.ClassKey] = "OverCapacity",
            [OccupancyAssetAttributes.OccupiedCountKey] = "7",
            [OccupancyAssetAttributes.OverCapacityKey] = "true",
            [OccupancyAssetAttributes.UnidentifiedKey] = "false",
        });
        await AssertMatchesTheLiveOccupancyAsync(host, light, crowded);

        var (poleType, layers, rules) = await StylingAsync(host);
        LayerResolver.Resolve(light, poleType, layers, rules)!.Id.Should().Be(RuleLayer(rules, "Low"));
        LayerResolver.Resolve(crowded, poleType, layers, rules)!.Id.Should().Be(RuleLayer(rules, "OverCapacity"));
        RuleLayer(rules, "Low").Should().NotBe(DefaultPoleLayerId);

        (await OutboxStatusAsync(host, IntegrationEventTypes.AssetMutationBatchRequestedV1)).Should().Be(OutboxMessageStatus.Dispatched);
        (await OutboxStatusAsync(host, notification)).Should().Be(OutboxMessageStatus.Pending, "with no broker, nothing but asset batches is delivered");
        (await AppliedBatchesAsync(host)).Should().Be(1);

        // Redelivery: the relay hands the same batch over again.
        await MarkBatchPendingAgainAsync(host);
        var redelivered = await Relay(host).ProcessNextBatchAsync(CancellationToken.None);

        redelivered.Should().Be(1, "the redelivered batch is acknowledged");
        foreach (var before in new[] { light, crowded })
        {
            var after = (await PoleAsync(host, before.Id))!;
            after.Properties.UpdatedAt.Should().Be(before.Properties.UpdatedAt, "the redelivered batch wrote nothing");
            after.Properties.CustomAttributes.Should().Equal(before.Properties.CustomAttributes);
        }

        (await AppliedBatchesAsync(host)).Should().Be(1);
        (await OutboxStatusAsync(host, IntegrationEventTypes.AssetMutationBatchRequestedV1)).Should().Be(OutboxMessageStatus.Dispatched);
    }

    [Fact]
    public async Task ABatchThatCannotBeApplied_IsRetriedWithBackoff_ThenLeftFailedInTheOutbox_AndNoPoleIsWritten()
    {
        await using var host = await HostAsync(maxAttempts: 2);
        await SeedAsync(host, [LightPole], features: []);
        await StageAsync(host, LightPole, Add(1));
        await ApproveAsync(host);
        var relay = Relay(host);

        (await relay.ProcessNextBatchAsync(CancellationToken.None)).Should().Be(0);
        var afterFirst = await OutboxRowAsync(host);
        afterFirst.Status.Should().Be(OutboxMessageStatus.Pending, "a failed attempt is retried");
        afterFirst.AttemptCount.Should().Be(1);
        afterFirst.LastError.Should().Contain(LightPole, "the pole is not in the owner's catalog");
        afterFirst.NextRetryAtUtc.Should().BeAfter(afterFirst.CreatedAtUtc, "the retry waits for the relay's backoff");

        await ExpireBackoffAsync(host);
        (await relay.ProcessNextBatchAsync(CancellationToken.None)).Should().Be(0);

        var afterLast = await OutboxRowAsync(host);
        afterLast.Status.Should().Be(OutboxMessageStatus.Failed, "after the last attempt the batch stays in the outbox as failed, not delivered");
        afterLast.AttemptCount.Should().Be(2);
        (await AppliedBatchesAsync(host)).Should().Be(0);
    }

    // ── Host ──────────────────────────────────────────────────────────────────

    private sealed record Host(ServiceProvider Services, string ConnectionString) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            // The catalog was written as the suite's scoped role; drop its pooled connections to this database.
            NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// The registrations of <c>Program.cs</c> that approving and applying need, with no broker
    /// configured, on a fresh database migrated with the production chains; the catalog is
    /// written as the suite's non-owner role, the workflow side as the owner.
    /// </summary>
    private static async Task<Host> HostAsync(int? maxAttempts = null)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"occupancy_apply_{Guid.NewGuid():N}",
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

        var catalogConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
        }.ConnectionString;

        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton(new GeoAssetsMeter("1.0.0"));
        services.AddScoped<ITenantContext>(_ => new FixedTenant(Owner));
        services.AddOrderTypeRegistry(registry => registry.RegisterSharedUseOrderTypes());
        services.AddWorkflowPersistence(o => o.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
        services.AddNeutralOperatorPersistence();
        services.AddServiceOrderRules();
        services.AddServerOrderOccupancyStaging(configuration);
        services.AddServerWorkflowMessaging(configuration);
        services.AddServerOccupancyCommit();
        services.AddPostgresAssetMutationBatchApplier(catalogConnectionString);
        services.AddServerInProcessAssetMutationApply(configuration);
        if (maxAttempts is { } attempts)
            services.Configure<OutboxRelayOptions>(o => o.MaxAttempts = attempts);

        return new Host(services.BuildServiceProvider(validateScopes: true), connectionString);
    }

    private static OutboxRelayProcessor Relay(Host host) =>
        host.Services.GetServices<IHostedService>().OfType<OutboxRelayProcessor>().Single();

    // ── Arrange ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner's poles in the catalog (<paramref name="features"/>, all of <paramref name="poles"/>
    /// when omitted) and a completed pole-work order targeting <paramref name="poles"/>.
    /// </summary>
    private static async Task SeedAsync(Host host, string[] poles, string[]? features = null)
    {
        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(host.ConnectionString);
        await using (var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System))
        {
            catalog.AddRange((features ?? poles).Select(Feature));
        }

        await using var scope = host.Services.CreateAsyncScope();
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
        await scope.ServiceProvider.GetRequiredService<IServiceOrderTargetWriter>().AddRangeAsync(
            Order, [.. poles.Select(Target)], CancellationToken.None);
    }

    private static GeoFeature Feature(string pole) => new()
    {
        Id = pole,
        Geometry = new GeoPoint(-38.5, -12.9),
        Properties = new GeoFeatureProperties
        {
            Name = pole,
            AssetTypeId = PoleType,
            OrganizationId = Owner,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = $"P-{pole}" },
        },
    };

    private static ServiceOrderTarget Target(string pole) => new()
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
    };

    private static OccupancyChange Add(int? slot) => new()
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

    private static Task StageAsync(Host host, string pole, OccupancyChange change) => StageAsync(host, pole, [change]);

    private static async Task StageAsync(Host host, string pole, OccupancyChange[] changes)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var baseline = await scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>().GetForPoleAsync(pole, CancellationToken.None);
        var set = OccupancyChangeSet.Stage(
            Order, OrderTargetSelectionService.TargetId(Order, pole), pole, Owner, changes, baseline, "crew-1", new DateTimeOffset(SeedTime));
        await scope.ServiceProvider.GetRequiredService<IOccupancyChangeSetWriter>().StageAsync(set, CancellationToken.None);
    }

    /// <summary>A message of another type on the same outbox, standing for a dispatch notification.</summary>
    private static async Task<Guid> StageUnrelatedNotificationAsync(Host host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var envelope = IntegrationEnvelope.Create(
            messageType: IntegrationEventTypes.OrderStateChangedV1,
            schemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            partitionKey: Order,
            contentType: "application/x-protobuf",
            payload: new byte[] { 1 },
            timeProvider: TimeProvider.System);
        await scope.ServiceProvider.GetRequiredService<IOutboxWriter>().EnqueueEnvelopeAsync(envelope);
        return envelope.MessageId;
    }

    private static async Task<OccupancyCommitResult> ApproveAsync(Host host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOccupancyCommitService>()
            .ApproveAndCommitAsync(Order, Approver, CancellationToken.None);
    }

    // ── Observe ───────────────────────────────────────────────────────────────

    /// <summary>The pole as its owner's catalog reads it.</summary>
    private static async Task<GeoFeature?> PoleAsync(Host host, string pole)
    {
        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(host.ConnectionString);
        await using var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options, new FixedTenant(Owner)), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        return catalog.GetById(pole);
    }

    /// <summary>The attributes on each pole are what its live occupancy, as committed, says.</summary>
    private static async Task AssertMatchesTheLiveOccupancyAsync(Host host, params GeoFeature[] poles)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var occupancy = scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>();
        foreach (var pole in poles)
        {
            var live = (await occupancy.GetForPoleAsync(pole.Id, CancellationToken.None))!;
            pole.Properties.CustomAttributes.Should().Contain(OccupancyAssetAttributes.For(live));
        }
    }

    private static async Task<(AssetType PoleType, IReadOnlyList<Layer> Layers, IReadOnlyList<LayerRule> Rules)> StylingAsync(Host host)
    {
        var options = SecurityTestContainerFixture.BuildGeoAssetsOptions(host.ConnectionString);
        await using var catalog = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        var poleTypeId = Guid.Parse(PoleType);
        return (catalog.GetAssetTypes().Single(t => t.Id == poleTypeId), catalog.GetLayers(), catalog.GetLayerRules(poleTypeId));
    }

    private static Guid RuleLayer(IReadOnlyList<LayerRule> rules, string occupancyClass) =>
        rules.Single(rule => rule.Conditions.Count == 1
            && string.Equals(rule.Conditions[0].Attribute, OccupancyAssetAttributes.ClassKey, StringComparison.Ordinal)
            && string.Equals(rule.Conditions[0].Value, occupancyClass, StringComparison.Ordinal)).LayerId;

    private sealed record OutboxRow(OutboxMessageStatus Status, int AttemptCount, string? LastError, DateTime CreatedAtUtc, DateTime NextRetryAtUtc);

    private static async Task<OutboxRow> OutboxRowAsync(Host host)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Status", "AttemptCount", "LastError", "CreatedAtUtc", "NextRetryAtUtc"
            FROM "WorkflowOutboxMessages" WHERE "MessageType" = @type
            """;
        command.Parameters.AddWithValue("type", IntegrationEventTypes.AssetMutationBatchRequestedV1);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        var row = new OutboxRow(
            (OutboxMessageStatus)reader.GetInt32(0), reader.GetInt32(1), await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
            reader.GetDateTime(3), reader.GetDateTime(4));
        (await reader.ReadAsync()).Should().BeFalse("one approval stages one batch");
        return row;
    }

    private static async Task<OutboxMessageStatus> OutboxStatusAsync(Host host, string messageType)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Status" FROM "WorkflowOutboxMessages" WHERE "MessageType" = @type""";
        command.Parameters.AddWithValue("type", messageType);
        return (OutboxMessageStatus)(int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<OutboxMessageStatus> OutboxStatusAsync(Host host, Guid messageId)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Status" FROM "WorkflowOutboxMessages" WHERE "Id" = @id""";
        command.Parameters.AddWithValue("id", messageId);
        return (OutboxMessageStatus)(int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> AppliedBatchesAsync(Host host)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM applied_mutation_batches";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task MarkBatchPendingAgainAsync(Host host)
    {
        await using var connection = await OpenAsync(host);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE "WorkflowOutboxMessages" SET "Status" = @pending, "DispatchedAtUtc" = NULL WHERE "MessageType" = @type
            """;
        command.Parameters.AddWithValue("type", IntegrationEventTypes.AssetMutationBatchRequestedV1);
        command.Parameters.AddWithValue("pending", (int)OutboxMessageStatus.Pending);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task ExpireBackoffAsync(Host host)
    {
        await using var connection = await OpenAsync(host);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE "WorkflowOutboxMessages" SET "NextRetryAtUtc" = "CreatedAtUtc" WHERE "MessageType" = @type
            """;
        command.Parameters.AddWithValue("type", IntegrationEventTypes.AssetMutationBatchRequestedV1);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task<NpgsqlConnection> OpenAsync(Host host)
    {
        var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
