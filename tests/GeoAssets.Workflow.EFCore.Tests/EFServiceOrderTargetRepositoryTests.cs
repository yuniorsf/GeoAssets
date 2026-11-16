using System.Data.Common;
using FluentAssertions;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// What <see cref="EFServiceOrderTargetRepository"/> does beyond the shared
/// <see cref="ServiceOrderTargetStoreContractTests"/> contract: its tenancy rules, the release
/// stamp it writes, how the database itself guards the table, and its DI registration.
/// </summary>
public sealed class EFServiceOrderTargetRepositoryTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    // ── Tenancy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task OperationsAddressedToOneOrder_ReachItsTargetsFromAnotherOrganizationsScope_BecauseTheCallerAuthorizedTheOrder()
    {
        using var fixture = new SqliteFixture(new FakeTenantContext(TenantA));
        await SeedOrderAsync(fixture.Context, "ord-a", TenantA);
        var target = Target("ord-a", "feat-1", TenantA).WithPosition(-38.5, -12.9).Build();
        await new EFServiceOrderTargetRepository(fixture.Context).AddRangeAsync("ord-a", [target], CancellationToken.None);

        // A grantee: its own organization scope, working on an order of TenantA it was granted.
        using var granteeContext = fixture.NewContext(new FakeTenantContext(TenantB));
        var grantee = new EFServiceOrderTargetRepository(granteeContext);

        (await grantee.GetPageAsync(new OrderTargetQuery("ord-a"), CancellationToken.None)).Items.Should().ContainSingle();
        (await grantee.CountAsync("ord-a", CancellationToken.None)).Should().Be(1);
        (await grantee.GetExtentAsync("ord-a", CancellationToken.None)).Should().NotBeNull();
        await grantee.UpdateAsync(target with { Note = "edited by the grantee" }, CancellationToken.None);
        (await grantee.AddRangeAsync("ord-a", [Target("ord-a", "feat-2", TenantA).Build()], CancellationToken.None)).Should().Be(1);
        (await grantee.RemoveAsync("ord-a", [target.Id], CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task TenantQueryFilter_HidesOtherOrganizationsTargets_FromAnyQueryThatDoesNotOptOut()
    {
        using var fixture = new SqliteFixture(new FakeTenantContext(TenantA));
        await SeedOrderAsync(fixture.Context, "ord-a", TenantA);
        await SeedOrderAsync(fixture.Context, "ord-b", TenantB);
        var repository = new EFServiceOrderTargetRepository(fixture.Context);
        await repository.AddRangeAsync("ord-a", [Target("ord-a", "feat-1", TenantA).Build()], CancellationToken.None);
        await repository.AddRangeAsync("ord-b", [Target("ord-b", "feat-1", TenantB).Build()], CancellationToken.None);

        using var contextA = fixture.NewContext(new FakeTenantContext(TenantA));
        using var contextB = fixture.NewContext(new FakeTenantContext(TenantB));

        (await contextA.ServiceOrderTargets.Select(t => t.ServiceOrderId).ToListAsync()).Should().Equal("ord-a");
        (await contextB.ServiceOrderTargets.Select(t => t.ServiceOrderId).ToListAsync()).Should().Equal("ord-b");
    }

    [Fact]
    public async Task FindOpenUsagesAsync_ForAGranteeWorkingOnAnotherOrganizationsOrder_SearchesThatOrdersOrganization()
    {
        using var fixture = new SqliteFixture(new FakeTenantContext(TenantA));
        await SeedOrderAsync(fixture.Context, "ord-a1", TenantA);
        await SeedOrderAsync(fixture.Context, "ord-a2", TenantA);
        await SeedOrderAsync(fixture.Context, "ord-b", TenantB);
        var repository = new EFServiceOrderTargetRepository(fixture.Context);
        await repository.AddRangeAsync("ord-a2", [Target("ord-a2", "feat-1", TenantA).Build()], CancellationToken.None);
        await repository.AddRangeAsync("ord-b", [Target("ord-b", "feat-1", TenantB).Build()], CancellationToken.None);
        using var granteeContext = fixture.NewContext(new FakeTenantContext(TenantB));
        var grantee = new EFServiceOrderTargetRepository(granteeContext);

        var onGrantedOrder = await grantee.FindOpenUsagesAsync(["feat-1"], "ord-a1", CancellationToken.None);
        var withNoOrderYet = await grantee.FindOpenUsagesAsync(["feat-1"], "ord-not-created-yet", CancellationToken.None);

        onGrantedOrder.Select(u => u.OrderId).Should().Equal("ord-a2");
        withNoOrderYet.Select(u => u.OrderId).Should().Equal("ord-b");
    }

    // ── Release stamp written at insert ───────────────────────────────────────

    [Fact]
    public async Task AddRangeAsync_ToAnOpenOrder_LeavesReleasedAtNull_AndToAClosedOrder_StampsItFromTheClock()
    {
        using var fixture = new SqliteFixture();
        var clock = new FakeTimeProvider(Now);
        await SeedOrderAsync(fixture.Context, "ord-open", Guid.Empty);
        await SeedOrderAsync(fixture.Context, "ord-closed", Guid.Empty, ServiceOrderStatus.Completed);
        var repository = new EFServiceOrderTargetRepository(fixture.Context, timeProvider: clock);

        await repository.AddRangeAsync("ord-open", [Target("ord-open", "feat-1", Guid.Empty).Build()], CancellationToken.None);
        await repository.AddRangeAsync("ord-closed", [Target("ord-closed", "feat-1", Guid.Empty).Build()], CancellationToken.None);

        var releasedAt = await ReleasedAtByOrderAsync(fixture);
        releasedAt["ord-open"].Should().BeNull();
        releasedAt["ord-closed"].Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task AddRangeAsync_WhatCountsAsClosed_ComesFromTheOrderTypesOwnGraphWhenRegistered()
    {
        using var fixture = new SqliteFixture();
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id = "custom",
            DisplayName = "Custom",
            States = [new WorkflowState(ServiceOrderStatus.Completed, "Completed"), new WorkflowState("Signed", "Signed")],
            Transitions = [new WorkflowTransition(ServiceOrderStatus.Completed, "Signed")],
        });
        await SeedOrderAsync(fixture.Context, "ord-1", Guid.Empty, ServiceOrderStatus.Completed, "custom");
        var repository = new EFServiceOrderTargetRepository(fixture.Context, registry, new FakeTimeProvider(Now));

        await repository.AddRangeAsync("ord-1", [Target("ord-1", "feat-1", Guid.Empty).Build()], CancellationToken.None);

        (await ReleasedAtByOrderAsync(fixture))["ord-1"].Should().BeNull("Completed still has a way forward in this type's graph");
    }

    [Fact]
    public async Task UpdateAsync_OnAReleasedTarget_KeepsItsReleaseStamp()
    {
        using var fixture = new SqliteFixture();
        await SeedOrderAsync(fixture.Context, "ord-closed", Guid.Empty, ServiceOrderStatus.Cancelled);
        var repository = new EFServiceOrderTargetRepository(fixture.Context, timeProvider: new FakeTimeProvider(Now));
        var target = Target("ord-closed", "feat-1", Guid.Empty).Build();
        await repository.AddRangeAsync("ord-closed", [target], CancellationToken.None);

        await repository.UpdateAsync(target with { Note = "late remark" }, CancellationToken.None);

        (await ReleasedAtByOrderAsync(fixture))["ord-closed"].Should().Be(Now.UtcDateTime);
    }

    // ── Atomicity and database-level guards ───────────────────────────────────

    [Fact]
    public async Task AddRangeAsync_FailingInALaterInsertStatement_StoresNothingFromTheCall()
    {
        using var fixture = new SqliteFixture();
        await SeedOrderAsync(fixture.Context, "ord-1", Guid.Empty);
        var repository = new EFServiceOrderTargetRepository(fixture.Context);
        await repository.AddRangeAsync("ord-1", [Target("ord-1", "feat-held", Guid.Empty).WithId("tgt-taken").Build()], CancellationToken.None);
        // 150 targets span two insert statements; the last one reuses a stored target's primary
        // key for a different feature, which no (order, feature) conflict rule can skip.
        var batch = Enumerable.Range(0, EFServiceOrderTargetRepository.InsertRowsPerStatement + 49)
            .Select(i => Target("ord-1", $"feat-{i:D3}", Guid.Empty).Build())
            .Append(Target("ord-1", "feat-last", Guid.Empty).WithId("tgt-taken").Build())
            .ToList();

        var act = () => repository.AddRangeAsync("ord-1", batch, CancellationToken.None);

        await act.Should().ThrowAsync<DbException>();
        (await repository.CountAsync("ord-1", CancellationToken.None)).Should().Be(1, "the first statement's rows were rolled back with the failed one");
    }

    [Fact]
    public async Task AddRangeAsync_InsideTheCallersTransaction_IsRolledBackWithIt()
    {
        using var fixture = new SqliteFixture();
        await SeedOrderAsync(fixture.Context, "ord-1", Guid.Empty);
        var repository = new EFServiceOrderTargetRepository(fixture.Context);
        var batch = Enumerable.Range(0, EFServiceOrderTargetRepository.InsertRowsPerStatement * 2)
            .Select(i => Target("ord-1", $"feat-{i:D3}", Guid.Empty).Build())
            .ToList();

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            (await repository.AddRangeAsync("ord-1", batch, CancellationToken.None)).Should().Be(batch.Count);
            await transaction.RollbackAsync();
        }

        (await repository.CountAsync("ord-1", CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task AddRangeAsync_NullElementInTargets_ThrowsArgumentException()
    {
        using var fixture = new SqliteFixture();
        await SeedOrderAsync(fixture.Context, "ord-1", Guid.Empty);
        var repository = new EFServiceOrderTargetRepository(fixture.Context);

        var act = () => repository.AddRangeAsync("ord-1", [Target("ord-1", "feat-1", Guid.Empty).Build(), null!], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        (await repository.CountAsync("ord-1", CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task UniqueIndex_RejectsASecondRowForTheSameOrderAndFeature_EvenWhenWrittenAroundTheRepository()
    {
        using var fixture = new SqliteFixture();
        await SeedOrderAsync(fixture.Context, "ord-1", Guid.Empty);
        fixture.Context.ServiceOrderTargets.Add(ServiceOrderTargetMapper.ToRecord(Target("ord-1", "feat-1", Guid.Empty).WithId("tgt-1").Build()));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ServiceOrderTargets.Add(ServiceOrderTargetMapper.ToRecord(Target("ord-1", "feat-1", Guid.Empty).WithId("tgt-2").Build()));

        var act = () => fixture.Context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ForeignKey_RejectsATargetOfAnOrderThatDoesNotExist()
    {
        using var fixture = new SqliteFixture();
        fixture.Context.ServiceOrderTargets.Add(ServiceOrderTargetMapper.ToRecord(Target("ord-missing", "feat-1", Guid.Empty).Build()));

        var act = () => fixture.Context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingAnOrder_DeletesItsTargets_AndOnlyIts()
    {
        using var fixture = new SqliteFixture();
        await SeedOrderAsync(fixture.Context, "ord-1", Guid.Empty);
        await SeedOrderAsync(fixture.Context, "ord-2", Guid.Empty);
        var repository = new EFServiceOrderTargetRepository(fixture.Context);
        await repository.AddRangeAsync("ord-1", [Target("ord-1", "feat-1", Guid.Empty).Build(), Target("ord-1", "feat-2", Guid.Empty).Build()], CancellationToken.None);
        await repository.AddRangeAsync("ord-2", [Target("ord-2", "feat-1", Guid.Empty).Build()], CancellationToken.None);

        await new EFServiceOrderRepository(fixture.Context).DeleteAsync("ord-1");

        (await repository.CountAsync("ord-1", CancellationToken.None)).Should().Be(0);
        (await repository.CountAsync("ord-2", CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        var act = () => new EFServiceOrderTargetRepository(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── DI registration ───────────────────────────────────────────────────────

    [Fact]
    public void AddWorkflowPersistence_RegistersBothTargetPorts_AsOneScopedRepositoryPerScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<GeoAssetsActivitySource>();
        services.AddSingleton<GeoAssetsMeter>();
        services.AddWorkflowPersistence(options => options.UseSqlite("Data Source=:memory:"));
        using var provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IServiceOrderTargetReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IServiceOrderTargetWriter>();

        reader.Should().BeOfType<EFServiceOrderTargetRepository>();
        writer.Should().BeSameAs(reader);
        otherScope.ServiceProvider.GetRequiredService<IServiceOrderTargetReader>().Should().NotBeSameAs(reader);
    }

    [Fact]
    public void AddServiceOrderTargetPersistence_CalledTwice_KeepsASingleRegistrationPerPort()
    {
        var services = new ServiceCollection();

        services.AddServiceOrderTargetPersistence().AddServiceOrderTargetPersistence();

        services.Count(d => d.ServiceType == typeof(IServiceOrderTargetReader)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IServiceOrderTargetWriter)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(EFServiceOrderTargetRepository)).Should().Be(1);
        services.Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddServiceOrderTargetPersistence_NullServices_ThrowsArgumentNullException()
    {
        var act = () => WorkflowEFCoreServiceExtensions.AddServiceOrderTargetPersistence(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Task SeedOrderAsync(
        ServiceOrderDbContext context,
        string id,
        Guid organizationId,
        string status = ServiceOrderStatus.Draft,
        string orderTypeId = "inspection") =>
        new EFServiceOrderRepository(context).AddAsync(new ServiceOrder
        {
            Id = id,
            Title = id,
            OrderTypeId = orderTypeId,
            Status = status,
            CreatedBy = "u1",
            OrganizationId = organizationId,
            CreatedAt = Now.UtcDateTime,
        });

    private static ServiceOrderTargetBuilder Target(string orderId, string featureId, Guid organizationId) =>
        new ServiceOrderTargetBuilder()
            .WithId($"{orderId}:{featureId}")
            .WithOrderId(orderId)
            .WithFeatureId(featureId)
            .WithOrganizationId(organizationId);

    private static async Task<Dictionary<string, DateTime?>> ReleasedAtByOrderAsync(SqliteFixture fixture)
    {
        using var context = fixture.NewContext();
        return await context.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(t => t.ServiceOrderId, t => t.ReleasedAt, StringComparer.Ordinal);
    }
}
