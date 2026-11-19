using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using GeoAssets.Workflow.Tests.Selection;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// Everything <see cref="OrderTargetSelectionService"/> works on, wired with test doubles: a
/// fake order store, the in-memory target store, a recording spatial query that answers with
/// whatever features the test gave it, and a fake clock.
/// </summary>
internal sealed class SelectionServiceFixture : IDisposable
{
    public const string OrderId = "ord-1";
    public const string OrderTypeId = "pole-survey";
    public const string PoleType = "pole";
    public const string AliasKey = "plate";

    public static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000308");
    public static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000308");
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Holds every role-based grant, so the order rules never stand in a test's way.</summary>
    public static readonly WorkflowPrincipal Admin = new("admin-1", OrgA.ToString(), ["Administrator"], [], []);

    /// <summary>May view any order and nothing else (see <see cref="Rules"/>).</summary>
    public static readonly WorkflowPrincipal Viewer = new("viewer-1", OrgA.ToString(), ["Viewer"], [], []);

    /// <summary>Authenticated, but related to no order and holding no role.</summary>
    public static readonly WorkflowPrincipal Stranger = new("stranger-1", OrgA.ToString(), [], [], []);

    private readonly FeatureSelectionRegistry _registry;

    public SelectionServiceFixture(
        OrderTargetPolicy? policy = null,
        bool registerOrderType = true,
        int maxMatched = OrderTargetSelectionOptions.DefaultMaxMatched,
        params GeoFeature[] answer)
    {
        Clock = new FakeTimeProvider(Now);
        Orders = new FakeServiceOrderRepository();
        OrderTypes = new OrderTypeRegistry();
        if (registerOrderType)
            OrderTypes.Register(new OrderType { Id = OrderTypeId, DisplayName = "Pole survey", TargetPolicy = policy ?? Policy() });

        Rules = new ServiceOrderRules(
            OrderTypes,
            roleGrants: new Dictionary<string, IReadOnlySet<OrderAction>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Viewer"] = new HashSet<OrderAction> { OrderActionType.View },
            });

        Store = new InMemoryServiceOrderTargetStore(Orders, OrderTypes);
        Query = new RecordingSpatialQuery(answer);
        Options = new OrderTargetSelectionOptions { MaxMatched = maxMatched };
        _registry = new FeatureSelectionRegistry(Clock, "no-such-plugins-directory", typeof(FeatureSelectionRegistry).Assembly);
        Selector = new BoundedFeatureSelector(_registry, Query, assets: null, Clock);
        Sut = Build(Store, Store);
    }

    public FakeTimeProvider Clock { get; }
    public FakeServiceOrderRepository Orders { get; }
    public OrderTypeRegistry OrderTypes { get; }
    public ServiceOrderRules Rules { get; }
    public InMemoryServiceOrderTargetStore Store { get; }
    public RecordingSpatialQuery Query { get; }
    public OrderTargetSelectionOptions Options { get; }
    public BoundedFeatureSelector Selector { get; }
    public OrderTargetSelectionService Sut { get; }

    /// <summary>The same service over other target-store doubles.</summary>
    public OrderTargetSelectionService Build(IServiceOrderTargetReader reader, IServiceOrderTargetWriter writer) =>
        new(Orders, OrderTypes, Rules, Selector, reader, writer, Options, Clock);

    public static OrderTargetPolicy Policy(
        int? selectionLimit = null,
        OrderTargetExclusivity exclusivity = OrderTargetExclusivity.None,
        string? aliasAttributeKey = AliasKey,
        params string[] assetTypeIds) => new()
        {
            TargetAssetTypeIds = assetTypeIds.Length == 0 ? [PoleType] : assetTypeIds,
            AliasAttributeKey = aliasAttributeKey,
            SelectionLimit = selectionLimit,
            Exclusivity = exclusivity,
        };

    public async Task<ServiceOrder> SeedOrderAsync(
        string id = OrderId,
        string orderTypeId = OrderTypeId,
        string status = ServiceOrderStatus.Draft,
        Guid? organizationId = null,
        string? parentOrderId = null)
    {
        var order = new ServiceOrder
        {
            Id = id,
            Title = id,
            OrderTypeId = orderTypeId,
            Status = status,
            CreatedBy = "creator-1",
            OrganizationId = organizationId ?? OrgA,
            ParentOrderId = parentOrderId,
            CreatedAt = Now.UtcDateTime.AddDays(-1),
        };
        await Orders.AddAsync(order);
        return order;
    }

    /// <summary>Stores targets for <paramref name="featureIds"/> directly, as an earlier selection would have.</summary>
    public Task<int> HoldAsync(string orderId, params string[] featureIds) =>
        Store.AddRangeAsync(
            orderId,
            [.. featureIds.Select(featureId => new ServiceOrderTargetBuilder()
                .WithId($"{orderId}:{featureId}")
                .WithOrderId(orderId)
                .WithFeatureId(featureId)
                .WithOrganizationId(OrgA)
                .Build())],
            CancellationToken.None);

    public static GeoFeature Pole(
        string id, string? plate = null, Guid? organizationId = null, string assetTypeId = PoleType, bool withGeometry = true)
    {
        var feature = new GeoFeature
        {
            Id = id,
            Geometry = withGeometry ? new GeoPoint(-38.5, -12.9) : null,
            Properties = new GeoFeatureProperties
            {
                Name = id,
                AssetTypeId = assetTypeId,
                OrganizationId = organizationId ?? OrgA,
            },
        };
        if (plate is not null) feature.Properties.CustomAttributes[AliasKey] = plate;
        return feature;
    }

    /// <summary><paramref name="count"/> poles with ids <c>p-001…</c>, already in id order.</summary>
    public static GeoFeature[] Poles(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Pole($"p-{i:000}", $"P-{i:000}"))];

    /// <summary>A valid closed square fence; the recording query answers regardless of its extent.</summary>
    public static Dictionary<string, object> Fence() => new(StringComparer.Ordinal)
    {
        [PolygonFenceSelectionStrategy.RingsParameter] = new double[][][]
        {
            [[-39, -13], [-38, -13], [-38, -12], [-39, -12], [-39, -13]],
        },
    };

    public static TargetSelectionRequest FenceRequest(
        string orderId = OrderId, bool acceptTruncation = false, int page = 1, int pageSize = 50, Dictionary<string, object>? parameters = null) =>
        new(orderId, PolygonFenceSelectionStrategy.Id, parameters ?? Fence(), page, pageSize, acceptTruncation);

    public static TargetSelectionRequest IdentifierRequest(string? attributeKey, params string[] values)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [IdentifierListSelectionStrategy.ValuesParameter] = values,
        };
        if (attributeKey is not null) parameters[IdentifierListSelectionStrategy.AttributeKeyParameter] = attributeKey;

        return new TargetSelectionRequest(OrderId, IdentifierListSelectionStrategy.Id, parameters);
    }

    /// <summary>The stored target of <paramref name="featureId"/> in <paramref name="orderId"/>.</summary>
    public ServiceOrderTarget Stored(string featureId, string orderId = OrderId) =>
        StoredFor(orderId).Single(t => string.Equals(t.FeatureId, featureId, StringComparison.Ordinal));

    public IReadOnlyList<ServiceOrderTarget> StoredFor(string orderId = OrderId) =>
        [.. Store.Snapshot().Where(t => string.Equals(t.OrderId, orderId, StringComparison.Ordinal))];

    public static ServiceOrderTarget Of(IEnumerable<ServiceOrderTarget> targets, string featureId) =>
        targets.Single(t => string.Equals(t.FeatureId, featureId, StringComparison.Ordinal));

    public void Dispose() => _registry.Dispose();
}
