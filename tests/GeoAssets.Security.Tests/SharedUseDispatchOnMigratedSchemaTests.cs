using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Server;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The shared-use dispatch (XD01-316) resolved from the registrations <c>GeoAssets.Server</c>'s
/// <c>Program.cs</c> makes for it — persistence, outbox, dispatch service with its unit of work
/// and decorator — and run against a PostgreSQL database built by the production migrations.
/// What only this combination shows: that the registrations form a graph that resolves, and
/// that a dispatch, which writes its parent order twice in one unit, works against the real
/// row-version trigger and loses cleanly to a concurrent dispatch.
///
/// Each test gets a database of its own in the suite's container.
/// </summary>
public sealed class SharedUseDispatchOnMigratedSchemaTests
{
    private const string Order = "sud-parent";
    private const string PoleType = "00000000-0000-0000-0000-000000000004";

    private static readonly Guid Owner = new("f1f1f1f1-0000-4000-8000-000000000316");
    private static readonly Guid OccupantOne = new("f2f2f2f2-0000-4000-8000-000000000316");
    private static readonly Guid OccupantTwo = new("f3f3f3f3-0000-4000-8000-000000000316");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly WorkflowPrincipal Dispatcher = new("dispatcher-1", Owner.ToString(), ["Supervisor"], [], []);

    [Fact]
    public async Task Dispatch_ThroughTheHostsRegistrations_OnTheMigratedSchema_WritesChildrenTargetsDispatchesAndOutboxTogether_AndAReplayWritesNothing()
    {
        await using var provider = await HostAsync();
        await SeedAsync(provider);

        var result = await DispatchAsync(provider, await RowVersionAsync(provider));
        var afterFirst = await CountsAsync(provider);
        var replay = await DispatchAsync(provider, await RowVersionAsync(provider));

        result.ChildOrdersCreated.Should().Be(2);
        result.ParentStatus.Should().Be(ServiceOrderStatus.Pending);
        afterFirst.Should().Be("children=2 dispatches=2 childTargets=3 outbox=2 parentLog=1 parentStatus=Pending");
        replay.ChildOrdersCreated.Should().Be(0);
        replay.ChildOrdersAlreadyDispatched.Should().Be(2);
        (await CountsAsync(provider)).Should().Be(afterFirst, "a replay finds every child in place and writes nothing");
    }

    [Fact]
    public async Task Dispatch_ResolvesAsTheDecoratedService_BesideThePlanReaderAndTheEfUnitOfWork()
    {
        await using var provider = await HostAsync();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<ISharedUseDispatchService>().Should().BeOfType<ObservableSharedUseDispatchService>();
        scope.ServiceProvider.GetRequiredService<ISharedUseDispatchPlanReader>().Should().BeOfType<SharedUseDispatchService>();
        scope.ServiceProvider.GetRequiredService<IWorkflowUnitOfWork>().Should().BeOfType<EFWorkflowUnitOfWork>();
        scope.ServiceProvider.GetRequiredService<OrderTypeRegistry>().Get(SharedUseOrderTypes.PoleWorkId).TargetPolicy!.FanOut.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispatch_WithTheRowVersionOfAnEarlierRead_IsRefusedAsAConcurrencyConflict_AndWritesNothing()
    {
        await using var provider = await HostAsync();
        await SeedAsync(provider);
        var stale = await RowVersionAsync(provider);
        await using (var scope = provider.CreateAsyncScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();
            var current = (ServiceOrder)(await orders.GetByIdAsync(Order))!;
            current.Title = "Renamed meanwhile";
            await orders.UpdateAsync(current);
        }

        var act = async () => await DispatchAsync(provider, stale);

        (await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be(Order);
        (await CountsAsync(provider)).Should().Be("children=0 dispatches=0 childTargets=0 outbox=0 parentLog=0 parentStatus=Draft");
    }

    [Fact]
    public async Task Dispatch_SeveralCallersAtOnceWithTheSameRowVersion_CreateEachChildOnce_AndEveryOtherCallerLosesAsAConcurrencyConflict()
    {
        const int Callers = 5;
        await using var provider = await HostAsync();
        await SeedAsync(provider);
        var rowVersion = await RowVersionAsync(provider);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = Enumerable.Range(0, Callers).Select(async _ =>
        {
            await start.Task;
            try
            {
                return (Result: await DispatchAsync(provider, rowVersion), Failure: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Result: (SharedUseDispatchResult?)null, Failure: ex);
            }
        }).ToList();
        start.SetResult();
        var outcomes = await Task.WhenAll(calls);

        outcomes.Where(o => o.Failure is not null).Select(o => o.Failure!)
            .Should().AllBeOfType<ServiceOrderConcurrencyException>("a caller that lost the parent's row gets the typed conflict and nothing else");
        outcomes.Where(o => o.Result is not null).Sum(o => o.Result!.ChildOrdersCreated).Should().Be(2);
        outcomes.Count(o => o.Result is { ChildOrdersCreated: > 0 }).Should().Be(1, "the row-version trigger lets exactly one dispatch through per read");
        (await CountsAsync(provider)).Should().Be("children=2 dispatches=2 childTargets=3 outbox=2 parentLog=1 parentStatus=Pending");
    }

    // ── Host ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The registrations of <c>Program.cs</c> that the dispatch needs, on a fresh database
    /// migrated with the production chains, with the request's tenant fixed to the pole owner.
    /// </summary>
    private static async Task<ServiceProvider> HostAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"shared_use_dispatch_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
            await identity.Database.MigrateAsync();
        await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
            await serviceOrders.Database.MigrateAsync();

        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton(new GeoAssetsMeter("1.0.0"));
        services.AddScoped<ITenantContext>(_ => new FixedTenant(Owner));
        services.AddOrderTypeRegistry(registry => registry.RegisterSharedUseOrderTypes());
        services.AddWorkflowPersistence(o => o.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
        services.AddServiceOrderRules();
        services.AddServerWorkflowMessaging(configuration);
        services.AddServerSharedUseDispatch();
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>A draft pole-work order on two poles: occupant one on both, occupant two on the second, beyond the regulated slots.</summary>
    private static async Task SeedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().AddAsync(new ServiceOrder
        {
            Id = Order,
            Title = "Shared use",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            CreatedBy = "planner-1",
            OrganizationId = Owner,
            CreatedAt = SeedTime,
        });
        await scope.ServiceProvider.GetRequiredService<IServiceOrderTargetWriter>().AddRangeAsync(
            Order, [Target("sud-pole-1"), Target("sud-pole-2")], CancellationToken.None);

        var occupancy = scope.ServiceProvider.GetRequiredService<IPoleOccupancyWriter>();
        await occupancy.ReplaceEntriesAsync("sud-pole-1", PoleOccupancy.UnsavedVersion, [Entry("sud-pole-1", 1, OccupantOne)], CancellationToken.None);
        await occupancy.ReplaceEntriesAsync(
            "sud-pole-2", PoleOccupancy.UnsavedVersion,
            [Entry("sud-pole-2", 1, OccupantOne), Entry("sud-pole-2", slot: null, OccupantTwo)], CancellationToken.None);
    }

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

    private static PoleOccupancyEntry Entry(string pole, int? slot, Guid occupant) => new()
    {
        Id = Guid.NewGuid(),
        PoleFeatureId = pole,
        PoleOwnerOrganizationId = Owner,
        SlotIndex = slot,
        Status = PoleAttachmentOccupantStatus.AuthorizedContracted,
        OccupantOrganizationId = occupant,
        CableDiameterMm = 12.5m,
    };

    private static async Task<byte[]> RowVersionAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().GetByIdAsync(Order))!.RowVersion;
    }

    /// <summary>One dispatch in a scope of its own, as one request would run it.</summary>
    private static async Task<SharedUseDispatchResult> DispatchAsync(ServiceProvider provider, byte[] rowVersion)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISharedUseDispatchService>()
            .DispatchAsync(Order, rowVersion, Dispatcher, CancellationToken.None);
    }

    /// <summary>What the database holds for the order's fan-out, in one line.</summary>
    private static async Task<string> CountsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
        return await db.Database
            .SqlQuery<string>($"""
                SELECT ('children=' || (SELECT count(*) FROM "ServiceOrders" WHERE "ParentOrderId" = {Order})
                     || ' dispatches=' || (SELECT count(*) FROM "OrderDispatches" d JOIN "ServiceOrders" o ON o."Id" = d."ServiceOrderId" WHERE o."ParentOrderId" = {Order} AND d."TargetType" = 2)
                     || ' childTargets=' || (SELECT count(*) FROM "ServiceOrderTargets" t JOIN "ServiceOrders" o ON o."Id" = t."ServiceOrderId" WHERE o."ParentOrderId" = {Order})
                     || ' outbox=' || (SELECT count(*) FROM "WorkflowOutboxMessages" WHERE "CorrelationId" = {Order})
                     || ' parentLog=' || (SELECT count(*) FROM "OrderActionLogs" WHERE "ServiceOrderId" = {Order})
                     || ' parentStatus=' || (SELECT "Status" FROM "ServiceOrders" WHERE "Id" = {Order}))::text AS "Value"
                """)
            .SingleAsync();
    }

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
