using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary>
/// Everything <see cref="OrderOccupancyStagingService"/> works on, wired with the TestKit's
/// fakes — the stores it would get from EF Core, scoped to the pole owner (organization A) the
/// way a request of one of its members is — and a fake clock.
/// </summary>
internal sealed class OccupancyStagingFixture
{
    public const string OrderId = "ord-1";
    public const string OrderTypeId = "pole-survey";
    public const string PoleType = "pole";
    public const string Pole = "pole-1";

    public static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000315");
    public static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000315");
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A member of the pole owner holding every role-based grant.</summary>
    public static readonly WorkflowPrincipal Admin = new("admin-1", OrgA.ToString(), ["Administrator"], [], []);

    /// <summary>A member of the pole owner who may view any order and nothing else.</summary>
    public static readonly WorkflowPrincipal Viewer = new("viewer-1", OrgA.ToString(), ["Viewer"], [], []);

    /// <summary>A member of the pole owner related to no order and holding no role.</summary>
    public static readonly WorkflowPrincipal Stranger = new("stranger-1", OrgA.ToString(), [], [], []);

    /// <summary>An administrator of another organization with no grant: outside the organization gate.</summary>
    public static readonly WorkflowPrincipal Outsider = new("admin-b", OrgB.ToString(), ["Administrator"], [], []);

    /// <summary>An administrator of another organization holding a grant on organization A's orders: inside the gate, allowed by the rules, yet not the pole owner.</summary>
    public static readonly WorkflowPrincipal Grantee = new("grantee-b", OrgB.ToString(), ["Administrator"], [], [])
    {
        OrgGrants = [new WorkflowOrgGrant(OrgA.ToString(), "ServiceOrder", ["serviceorders:view", "serviceorders:annotate"], RequiredRole: null)],
    };

    public OccupancyStagingFixture(params string[] poleAssetTypeIds)
    {
        Clock = new FakeTimeProvider(Now);
        Orders = new FakeServiceOrderRepository();
        OrderTypes = new OrderTypeRegistry();
        OrderTypes.Register(new OrderType
        {
            Id = OrderTypeId,
            DisplayName = "Pole survey",
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = [PoleType, "manhole"] },
        });
        Rules = new ServiceOrderRules(
            OrderTypes,
            roleGrants: new Dictionary<string, IReadOnlySet<OrderAction>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Viewer"] = new HashSet<OrderAction> { OrderActionType.View },
            });

        var tenant = new FixedTenant(OrgA);
        Targets = new InMemoryServiceOrderTargetStore(Orders, OrderTypes, tenant);
        Occupancy = new InMemoryPoleOccupancyStore(tenant, Clock);
        ChangeSets = new InMemoryOccupancyChangeSetStore(Targets, Orders, OrderTypes, tenant, Clock);
        Options = new OrderOccupancyOptions();
        foreach (var assetTypeId in poleAssetTypeIds.Length == 0 ? [PoleType] : poleAssetTypeIds)
            Options.PoleAssetTypeIds.Add(assetTypeId);

        Sut = Build(Occupancy, ChangeSets, ChangeSets);
    }

    public FakeTimeProvider Clock { get; }
    public FakeServiceOrderRepository Orders { get; }
    public OrderTypeRegistry OrderTypes { get; }
    public ServiceOrderRules Rules { get; }
    public InMemoryServiceOrderTargetStore Targets { get; }
    public InMemoryPoleOccupancyStore Occupancy { get; }
    public InMemoryOccupancyChangeSetStore ChangeSets { get; }
    public OrderOccupancyOptions Options { get; }
    public OrderOccupancyStagingService Sut { get; }

    /// <summary>The same service over other occupancy and change-set doubles.</summary>
    public OrderOccupancyStagingService Build(
        IPoleOccupancyReader occupancy, IOccupancyChangeSetReader changeSets, IOccupancyChangeSetWriter changeSetWriter,
        IServiceOrderTargetReader? targets = null) =>
        new(Orders, OrderTypes, Rules, targets ?? Targets, occupancy, changeSets, changeSetWriter, Options, Clock);

    public static string TargetId(string featureId = Pole, string orderId = OrderId) => $"{orderId}:{featureId}";

    /// <summary>An order of organization A with one target per feature id, all of asset type <paramref name="assetTypeId"/>.</summary>
    public async Task<ServiceOrder> SeedOrderAsync(
        string orderId = OrderId, string status = ServiceOrderStatus.Draft, string assetTypeId = PoleType, params string[] featureIds)
    {
        var order = new ServiceOrder
        {
            Id = orderId,
            Title = orderId,
            OrderTypeId = OrderTypeId,
            Status = status,
            CreatedBy = "creator-1",
            OrganizationId = OrgA,
            CreatedAt = Now.UtcDateTime.AddDays(-1),
        };
        await Orders.AddAsync(order);
        await Targets.AddRangeAsync(
            orderId,
            [.. (featureIds.Length == 0 ? [Pole] : featureIds).Select(featureId => new ServiceOrderTargetBuilder()
                .WithId(TargetId(featureId, orderId))
                .WithOrderId(orderId)
                .WithFeatureId(featureId)
                .WithAssetTypeId(assetTypeId)
                .WithOrganizationId(OrgA)
                .Build())],
            CancellationToken.None);
        return order;
    }

    /// <summary>Writes the live occupancy of a pole: <paramref name="regulated"/> contracted slots and <paramref name="beyond"/> entries beyond them.</summary>
    public async Task<PoleOccupancy> SeedOccupancyAsync(string pole = Pole, int regulated = 2, int beyond = 0)
    {
        var current = await Occupancy.GetForPoleAsync(pole, CancellationToken.None);
        var entries = Enumerable.Range(1, regulated)
            .Select(slot => new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(OrgA).OnSlot(slot).Build())
            .Concat(Enumerable.Range(0, beyond).Select(_ => new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(OrgA).BeyondRegulatedSlots().Build()))
            .ToList();
        return await Occupancy.ReplaceEntriesAsync(pole, current?.Version ?? 0, entries, CancellationToken.None);
    }

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
