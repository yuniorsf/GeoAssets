using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Builds a <see cref="TargetLedgerService"/> over the supplied store and unit of work, with a selection service whose
/// in-process collaborators fail the test if reached (the ledger path never touches them) and a selector answering from
/// <see cref="CatalogSpatialQuery"/>.
/// </summary>
public static class TargetLedgerServiceHarness
{
    public static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public static OrderTargetSelectionService CreateSelection(IReadOnlyCollection<GeoFeature> catalog, TimeProvider clock)
    {
        var registry = new FeatureSelectionRegistry(clock, "no-such-plugins-directory", typeof(FeatureSelectionRegistry).Assembly);
        return CreateSelection(new BoundedFeatureSelector(registry, new CatalogSpatialQuery(catalog), assets: null, clock), clock);
    }

    /// <summary>The ledger-only selection service over <paramref name="selector"/>; its in-process collaborators fail the test if reached.</summary>
    public static OrderTargetSelectionService CreateSelection(BoundedFeatureSelector selector, TimeProvider clock)
    {
        var orderTypes = new OrderTypeRegistry();

        return new OrderTargetSelectionService(
            NeverCalledService.Of<IServiceOrderRepository>(),
            orderTypes,
            new ServiceOrderRules(orderTypes),
            selector,
            NeverCalledService.Of<IServiceOrderTargetReader>(),
            NeverCalledService.Of<IServiceOrderTargetWriter>(),
            new OrderTargetSelectionOptions(),
            clock);
    }

    public static TargetLedgerService Create(
        ITargetLedgerStore store,
        IWorkflowUnitOfWork unitOfWork,
        IOutboxWriter outbox,
        IIdempotentInbox inbox,
        IOrderTypeDescriptorRegistry descriptors,
        IReadOnlyCollection<GeoFeature> catalog,
        TimeProvider? clock = null,
        ITenantOrganizationScope? scope = null)
    {
        clock ??= new FakeTimeProvider(Now);
        return new TargetLedgerService(
            CreateSelection(catalog, clock), store, unitOfWork, outbox, inbox, descriptors, clock, scope ?? new TenantOrganizationScope());
    }

    /// <summary>A pole feature of <paramref name="organizationId"/> whose id is its identifier.</summary>
    public static GeoFeature Feature(string id, Guid organizationId, string assetTypeId = "pole")
    {
        var feature = new GeoFeature
        {
            Id = id,
            Geometry = new Core.Models.Geometry.GeoPoint(-38.5, -12.9),
            Properties = new GeoFeatureProperties { Name = id, AssetTypeId = assetTypeId, OrganizationId = organizationId },
        };
        return feature;
    }

    /// <summary>The body of an <c>identifier-list</c> selection of feature ids.</summary>
    public static TargetSelectionRequest ByIds(string orderId, params string[] featureIds) =>
        new(orderId, "identifier-list", new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["attributeKey"] = "id",
            ["values"] = featureIds,
        });
}
