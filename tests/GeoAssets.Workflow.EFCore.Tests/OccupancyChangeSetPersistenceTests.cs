using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Configurations;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// What is particular to the EF store of staged occupancy changes (XD01-315), beyond the shared
/// contract: the table as the model describes it, the mapper, how closing an order discards
/// what is staged (and how a failed close does not), the caller's transaction, and the DI
/// registration.
/// </summary>
public sealed class OccupancyChangeSetPersistenceTests : IDisposable
{
    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteFixture _fixture = new(new FakeTenantContext(Owner));
    private readonly ServiceOrderDbContext _postgres = new(
        new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql("Host=never-connected").Options);
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly OrderTypeRegistry _orderTypes = new();

    public void Dispose()
    {
        _postgres.Dispose();
        _fixture.Dispose();
    }

    public static TheoryData<bool> Providers => new() { { true }, { false } };

    private IEntityType Entity(bool postgres) =>
        (postgres ? _postgres : _fixture.Context).GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OccupancyChangeSetRecord))!;

    private EFOccupancyChangeSetRepository Store(ServiceOrderDbContext? context = null) => new(context ?? _fixture.Context, _clock);

    private async Task<string> SeedTargetAsync(string orderId = "ord-1", string pole = "pole-1")
    {
        var orders = new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _orderTypes, timeProvider: _clock);
        if (await orders.GetByIdAsync(orderId) is null)
            await orders.AddAsync(new ServiceOrder { Id = orderId, Title = orderId, OrderTypeId = "inspection", CreatedBy = "op", OrganizationId = Owner });

        var target = new ServiceOrderTargetBuilder().WithId($"{orderId}:{pole}").WithOrderId(orderId).WithFeatureId(pole).WithOrganizationId(Owner).Build();
        await new EFServiceOrderTargetRepository(_fixture.Context, _orderTypes, _clock).AddRangeAsync(orderId, [target], CancellationToken.None);
        return target.Id;
    }

    private OccupancyChangeSet NewSet(string orderId = "ord-1", string pole = "pole-1", params OccupancyChange[] changes) =>
        OccupancyChangeSet.Stage(
            orderId, $"{orderId}:{pole}", pole, Owner, changes.Length == 0 ? [OccupancyChangeBuilder.Add(2)] : changes, null, "crew-1", _clock.GetUtcNow());

    // ── Model ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Providers))]
    public void Table_IsServiceOrderOccupancyChangeSets_KeyedByAnApplicationAssignedId_CascadingFromItsTarget(bool postgres)
    {
        var entity = Entity(postgres);

        entity.GetTableName().Should().Be("ServiceOrderOccupancyChangeSets");
        entity.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().Equal("Id");
        entity.FindProperty("Id")!.ValueGenerated.Should().Be(ValueGenerated.Never);
        var foreignKey = entity.GetForeignKeys().Should().ContainSingle().Subject;
        foreignKey.PrincipalEntityType.ClrType.Should().Be<ServiceOrderTargetRecord>();
        foreignKey.Properties.Select(p => p.Name).Should().Equal("TargetId");
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Indexes_AreOneStagedSetPerTarget_PlusTarget_OrderWithStatus_AndOrganization(bool postgres)
    {
        var indexes = Entity(postgres).GetIndexes()
            .Select(index => (string.Join(',', index.Properties.Select(p => p.Name)), index.IsUnique, index.GetFilter()))
            .ToList();

        indexes.Should().BeEquivalentTo(new (string, bool, string?)[]
        {
            ("TargetId", true, OccupancyChangeSetRecordConfiguration.StagedFilter),
            ("TargetId", false, null),
            ("ServiceOrderId,Status", false, null),
            ("OrganizationId", false, null),
        });
        OccupancyChangeSetRecordConfiguration.StagedFilter.Should().Be("\"Status\" = 'Staged'");
        OccupancyChangeSetRecordConfiguration.StagedFilter.Should().Contain(
            OccupancyChangeSetMapper.ToStatusText(OccupancyChangeSetStatus.Staged), "the index must select what the repository writes");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Columns_HaveTheLengthsTheDomainAllows_AndOnlyPostgresGetsTheOrdinalCollation(bool postgres)
    {
        var entity = Entity(postgres);

        entity.FindProperty("ServiceOrderId")!.GetMaxLength().Should().Be(OccupancyChangeSet.MaxOrderKeyLength);
        entity.FindProperty("TargetId")!.GetMaxLength().Should().Be(OccupancyChangeSet.MaxOrderKeyLength);
        entity.FindProperty("PoleFeatureId")!.GetMaxLength().Should().Be(PoleOccupancyEntry.MaxFeatureIdLength);
        entity.FindProperty("StagedBy")!.GetMaxLength().Should().Be(OccupancyChangeSet.MaxStagedByLength);
        entity.FindProperty("ChangesJson")!.GetMaxLength().Should().BeNull();
        entity.FindProperty("ClosedAt")!.IsNullable.Should().BeTrue();
        entity.FindProperty("PoleFeatureId")!.GetCollation().Should().Be(postgres ? "C" : null);
        entity.FindProperty("TargetId")!.GetCollation().Should().Be(postgres ? "C" : null);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void InsertColumns_CoverEveryMappedProperty(bool postgres)
    {
        EFOccupancyChangeSetRepository.Columns.Select(column => column.Property)
            .Should().BeEquivalentTo(Entity(postgres).GetProperties().Select(p => p.Name));
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    [Fact]
    public void Mapper_RoundTripsASetInEveryStatus_WithEveryKindOfChange()
    {
        var entryId = Guid.NewGuid();
        var staged = OccupancyChangeSet.Stage(
            "ord-1", "tgt-1", "pole-1", Owner,
            [
                OccupancyChangeBuilder.Add(3, status: PoleAttachmentOccupantStatus.ActiveReservation) with
                {
                    OccupantOrganizationId = null, OperatorCnpj = null, AnatelLicenseNumber = "SCM-1",
                    CableDiameterMm = 12.345m, CableLoadDaN = 0.001m, AttachedCableFeatureId = "cable-1",
                },
                OccupancyChangeBuilder.AddBeyondRegulatedSlots(entryId),
                OccupancyChangeBuilder.RemoveSlot(1),
                OccupancyChangeBuilder.ModifySlot(2, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
                OccupancyChangeBuilder.MarkSlotUnidentified(6),
            ],
            baseline: null, "crew-1", Now);
        var discarded = staged.Discard(Now.AddHours(1));

        OccupancyChangeSetMapper.ToDomain(OccupancyChangeSetMapper.ToRecord(staged)).Should().BeEquivalentTo(staged);
        OccupancyChangeSetMapper.ToDomain(OccupancyChangeSetMapper.ToRecord(discarded)).Should().BeEquivalentTo(discarded);
        OccupancyChangeSetMapper.ToRecord(discarded).Should().BeEquivalentTo(new
        {
            ServiceOrderId = "ord-1", TargetId = "tgt-1", PoleFeatureId = "pole-1", OrganizationId = Owner, Status = "Discarded",
            StagedAt = Now.UtcDateTime, ClosedAt = (DateTime?)Now.AddHours(1).UtcDateTime,
        });
    }

    [Fact]
    public void Mapper_WritesEnumsByName_LeavesOutNullsAndComputedMembers_AndReadsItBack()
    {
        var json = OccupancyChangeSetMapper.ToChangesJson([OccupancyChangeBuilder.RemoveSlot(4), OccupancyChangeBuilder.Add(null, new Guid("0e0e0e0e-0000-4000-8000-000000000001"))]);

        json.Should().Be(
            """[{"kind":"Remove","slotIndex":4,"cableDiameterMm":0,"cableLoadDaN":0},"""
            + """{"kind":"Add","entryId":"0e0e0e0e-0000-4000-8000-000000000001","status":"AuthorizedContracted","occupantOrganizationId":"0c0c0c0c-0000-4000-8000-000000000002","operatorCnpj":"98.765.432/0001-10","cableDiameterMm":9.5,"cableLoadDaN":80}]""");
        OccupancyChangeSetMapper.FromChangesJson(json).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""[{"slotIndex":1}]""")]
    [InlineData("""[{"kind":"Vanish","slotIndex":1}]""")]
    [InlineData("""[{"kind":1,"slotIndex":1}]""")]
    public void Mapper_StoredChangesThatAreNotAChangeList_FailTheReadLoudly(string json)
    {
        var act = () => OccupancyChangeSetMapper.FromChangesJson(json);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Mapper_StatusText_IsTheMemberName_AndAnUnknownOneFails()
    {
        foreach (var status in Enum.GetValues<OccupancyChangeSetStatus>())
        {
            OccupancyChangeSetMapper.ToStatusText(status).Should().Be(status.ToString());
            OccupancyChangeSetMapper.FromStatusText(status.ToString()).Should().Be(status);
        }

        ((Action)(() => OccupancyChangeSetMapper.ToStatusText((OccupancyChangeSetStatus)9))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => OccupancyChangeSetMapper.FromStatusText("staged"))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task GetStagedAsync_ARowThatNoLongerFormsAValidSet_FailsTheRead()
    {
        var targetId = await SeedTargetAsync();
        await _fixture.Context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "OrganizationId")
            VALUES ({Guid.NewGuid()}, 'ord-1', {targetId}, 'pole-1', '[]', 0, 'Staged', 'crew-1', '2026-06-01 09:00:00', {Owner})
            """);

        var act = () => Store().GetStagedAsync("ord-1", targetId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>("a set without changes is not a set");
    }

    // ── The database itself ───────────────────────────────────────────────────

    [Fact]
    public async Task TheSchema_RefusesASecondStagedRowForATarget_AndARowWithoutATarget()
    {
        var targetId = await SeedTargetAsync();
        Task<int> InsertAsync(string target, string status) => _fixture.Context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "OrganizationId")
            VALUES ({Guid.NewGuid()}, 'ord-1', {target}, 'pole-1', '[]', 0, {status}, 'crew-1', '2026-06-01 09:00:00', {Owner})
            """);

        await InsertAsync(targetId, "Staged");
        await InsertAsync(targetId, "Discarded");
        await InsertAsync(targetId, "Discarded");

        await ((Func<Task>)(() => InsertAsync(targetId, "Staged"))).Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();
        await ((Func<Task>)(() => InsertAsync("tgt-missing", "Staged"))).Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();
    }

    // ── Closing the order ─────────────────────────────────────────────────────

    [Fact]
    public async Task ClosingTheOrder_DiscardsItsStagedSetsInTheSameSaveChangesAsTheStatus()
    {
        await SeedTargetAsync("ord-1", "pole-1");
        await SeedTargetAsync("ord-1", "pole-2");
        await Store().StageAsync(NewSet("ord-1", "pole-1"), CancellationToken.None);
        await Store().StageAsync(NewSet("ord-1", "pole-2"), CancellationToken.None);
        var orders = new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _orderTypes, timeProvider: _clock, hooks: [new OccupancyReleaseLifecycleHook(Store())]);
        var saves = new List<List<string>>();
        _fixture.Context.SavingChanges += (_, _) => saves.Add(
            [.. _fixture.Context.ChangeTracker.Entries().Where(e => e.State == EntityState.Modified).Select(e => e.Metadata.ClrType.Name).Order(StringComparer.Ordinal)]);
        _clock.Advance(TimeSpan.FromHours(3));

        var order = (ServiceOrder)(await orders.GetByIdAsync("ord-1"))!;
        order.Status = ServiceOrderStatus.Cancelled;
        await orders.UpdateAsync(order);

        saves.Should().ContainSingle().Which.Should().Equal(
            nameof(OccupancyChangeSetRecord), nameof(OccupancyChangeSetRecord), nameof(ServiceOrderRecord),
            nameof(ServiceOrderTargetRecord), nameof(ServiceOrderTargetRecord));
        var rows = await _fixture.Context.OccupancyChangeSets.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(row => row.Status == "Discarded" && row.ClosedAt == Now.AddHours(3).UtcDateTime);
    }

    [Fact]
    public async Task ClosingTheOrderThroughAnActionLogEntry_DiscardsItsStagedSetsToo()
    {
        var targetId = await SeedTargetAsync();
        await Store().StageAsync(NewSet(), CancellationToken.None);
        var orders = new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _orderTypes, timeProvider: _clock, hooks: [new OccupancyReleaseLifecycleHook(Store())]);

        await orders.AppendActionAsync("ord-1", new OrderActionLog(OrderActionType.Cancel, "op", _clock.GetUtcNow().UtcDateTime, ResultingStatus: ServiceOrderStatus.Cancelled));

        (await Store().GetStagedAsync("ord-1", targetId, CancellationToken.None)).Should().BeNull();
        (await Store().GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None)).Items
            .Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Discarded);
    }

    [Fact]
    public async Task AClosingThatLosesTheConcurrencyRace_LeavesTheStagedSetsStaged_AndTheChangeTrackerClean()
    {
        var targetId = await SeedTargetAsync();
        var staged = await Store().StageAsync(NewSet(), CancellationToken.None);
        var orders = new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _orderTypes, timeProvider: _clock, hooks: [new OccupancyReleaseLifecycleHook(Store())]);
        var stale = (ServiceOrder)(await orders.GetByIdAsync("ord-1"))!;
        await using (var other = _fixture.NewContext())
        {
            // Another writer touches the order first, so the token read above is stale.
            var winner = new EFServiceOrderRepository(other, orderTypeRegistry: _orderTypes, timeProvider: _clock);
            var theirs = (ServiceOrder)(await winner.GetByIdAsync("ord-1"))!;
            theirs.Title = "changed by someone else";
            await winner.UpdateAsync(theirs);
        }

        stale.Status = ServiceOrderStatus.Cancelled;
        var act = () => orders.UpdateAsync(stale);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        _fixture.Context.ChangeTracker.Entries<OccupancyChangeSetRecord>()
            .Should().ContainSingle().Which.Should().Match<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<OccupancyChangeSetRecord>>(
                entry => entry.State == EntityState.Unchanged && entry.Entity.Status == "Staged" && entry.Entity.ClosedAt == null,
                "a discard left pending in the change tracker would be written by this context's next save, on an order that never closed");
        (await Store().GetStagedAsync("ord-1", targetId, CancellationToken.None))!.Id.Should().Be(staged.Id);
    }

    [Fact]
    public async Task ClosingAnOrder_NeverTouchesAnotherOrdersSets_NorACommittedSet()
    {
        var targetId = await SeedTargetAsync("ord-1", "pole-1");
        var otherTargetId = await SeedTargetAsync("ord-2", "pole-1");
        var other = await Store().StageAsync(NewSet("ord-2", "pole-1"), CancellationToken.None);
        const string CommittedChanges = """[{"kind":"Remove","slotIndex":1}]""";
        await _fixture.Context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
            VALUES ({Guid.NewGuid()}, 'ord-1', {targetId}, 'pole-1', {CommittedChanges}, 2, 'Committed', 'crew-1', '2026-06-01 09:00:00', '2026-06-01 10:00:00', {Owner})
            """);
        var orders = new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _orderTypes, timeProvider: _clock, hooks: [new OccupancyReleaseLifecycleHook(Store())]);
        _clock.Advance(TimeSpan.FromDays(1));

        var order = (ServiceOrder)(await orders.GetByIdAsync("ord-1"))!;
        order.Status = ServiceOrderStatus.Cancelled;
        await orders.UpdateAsync(order);

        var committed = (await Store().GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None)).Items.Should().ContainSingle().Subject;
        committed.Status.Should().Be(OccupancyChangeSetStatus.Committed);
        committed.ClosedAt.Should().Be(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        (await Store().GetStagedAsync("ord-2", otherTargetId, CancellationToken.None))!.Id.Should().Be(other.Id);
    }

    // ── Transactions and the change tracker ───────────────────────────────────

    [Fact]
    public async Task StageAsync_InsideTheCallersTransaction_IsRolledBackWithIt_AndCommittedWithIt()
    {
        var targetId = await SeedTargetAsync();
        var before = await Store().StageAsync(NewSet(), CancellationToken.None);

        await using (var rolledBack = await _fixture.Context.Database.BeginTransactionAsync())
        {
            await Store().StageAsync(NewSet(), CancellationToken.None);
            await rolledBack.RollbackAsync();
        }

        (await Store().GetStagedAsync("ord-1", targetId, CancellationToken.None))!.Id.Should().Be(before.Id, "the replacement was rolled back, the delete included");

        OccupancyChangeSet after;
        await using (var committed = await _fixture.Context.Database.BeginTransactionAsync())
        {
            after = await Store().StageAsync(NewSet(), CancellationToken.None);
            await committed.CommitAsync();
        }

        (await Store().GetStagedAsync("ord-1", targetId, CancellationToken.None))!.Id.Should().Be(after.Id);
    }

    [Fact]
    public async Task Writes_NeitherTrackAnything_NorFlushWhatElseTheSharedContextTracks()
    {
        var targetId = await SeedTargetAsync();
        _fixture.Context.ChangeTracker.Clear();
        var unrelated = await _fixture.Context.ServiceOrders.IgnoreQueryFilters().SingleAsync(o => o.Id == "ord-1");
        unrelated.Title = "pending edit";

        await Store().StageAsync(NewSet(), CancellationToken.None);
        await Store().DiscardAsync("ord-1", targetId, CancellationToken.None);
        await Store().StageAsync(NewSet(), CancellationToken.None);
        await Store().DiscardAllAsync("ord-1", CancellationToken.None);

        _fixture.Context.ChangeTracker.Entries<OccupancyChangeSetRecord>().Should().BeEmpty();
        _fixture.Context.Entry(unrelated).State.Should().Be(EntityState.Modified, "the pending edit is still pending");
        await using var verify = _fixture.NewContext();
        (await verify.ServiceOrders.IgnoreQueryFilters().SingleAsync(o => o.Id == "ord-1")).Title.Should().Be("ord-1");
    }

    [Fact]
    public async Task ATenantFilter_HidesAnotherOrganizationsRows_EvenOnTheSameTargetId()
    {
        var targetId = await SeedTargetAsync();
        var staged = await Store().StageAsync(NewSet(), CancellationToken.None);
        await using var outsider = _fixture.NewContext(new FakeTenantContext(Guid.NewGuid()));

        (await Store(outsider).GetStagedAsync("ord-1", targetId, CancellationToken.None)).Should().BeNull();
        (await outsider.OccupancyChangeSets.CountAsync()).Should().Be(0);
        (await outsider.OccupancyChangeSets.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await Store().GetStagedAsync("ord-1", targetId, CancellationToken.None))!.Id.Should().Be(staged.Id);
    }

    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        var act = () => new EFOccupancyChangeSetRepository(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── DI ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddPoleOccupancyPersistence_RegistersBothChangeSetPorts_AsOneScopedRepositoryPerScope()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _fixture.NewContext());
        services.AddSingleton<TimeProvider>(_clock);
        services.AddPoleOccupancyPersistence().AddPoleOccupancyPersistence();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<IOccupancyChangeSetReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IOccupancyChangeSetWriter>();

        reader.Should().BeOfType<EFOccupancyChangeSetRepository>();
        writer.Should().BeSameAs(reader);
        otherScope.ServiceProvider.GetRequiredService<IOccupancyChangeSetReader>().Should().NotBeSameAs(reader);
        services.Count(d => d.ServiceType == typeof(IOccupancyChangeSetReader)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IOccupancyChangeSetWriter)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(EFOccupancyChangeSetRepository)).Should().Be(1);
    }
}
