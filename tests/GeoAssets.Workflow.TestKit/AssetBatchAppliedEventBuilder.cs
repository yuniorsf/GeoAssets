using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.TestKit;

public sealed class AssetBatchAppliedEventBuilder
{
    private Guid _batchId = IntegrationMessagingTestDataBuilders.DefaultBatchId;
    private string _catalogInstanceId = IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId;
    private Guid _organizationId = IntegrationMessagingTestDataBuilders.DefaultOrganizationId;
    private string _requestedByService = "field-operations-service";
    private string _appliedByUserId = "operator-1";
    private string? _sourceOrderId = "ord-100";
    private string? _sourceOrderTypeId = "inspection";
    private string? _scopeId = "sector-01";
    private IReadOnlyList<AssetBatchAppliedItem> _appliedItems =
    [
        new AssetBatchAppliedItem(
            FeatureId: "feat-100",
            MutationKind: AssetMutationKind.UpsertFeature,
            AssetTypeId: "network-node",
            RowVersion: [0x0A, 0x0B, 0x0C, 0x0D])
    ];
    private Rfc7946BoundingBox? _affectedBounds = new(-38.52, -12.98, -38.49, -12.96);
    private DateTimeOffset _appliedAtUtc = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc;
    private string? _correlationId = "corr-batch-100";

    public AssetBatchAppliedEventBuilder WithBatchId(Guid batchId)
    {
        _batchId = batchId;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithCatalogInstanceId(string catalogInstanceId)
    {
        _catalogInstanceId = catalogInstanceId;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithOrganizationId(Guid organizationId)
    {
        _organizationId = organizationId;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithRequestedByService(string requestedByService)
    {
        _requestedByService = requestedByService;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithAppliedByUserId(string appliedByUserId)
    {
        _appliedByUserId = appliedByUserId;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithSourceOrderId(string? sourceOrderId)
    {
        _sourceOrderId = sourceOrderId;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithSourceOrderTypeId(string? sourceOrderTypeId)
    {
        _sourceOrderTypeId = sourceOrderTypeId;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithScopeId(string? scopeId)
    {
        _scopeId = scopeId;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithAppliedItems(IReadOnlyList<AssetBatchAppliedItem> appliedItems)
    {
        _appliedItems = appliedItems;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithAffectedBounds(Rfc7946BoundingBox? affectedBounds)
    {
        _affectedBounds = affectedBounds;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithAppliedAtUtc(DateTimeOffset appliedAtUtc)
    {
        _appliedAtUtc = appliedAtUtc;
        return this;
    }

    public AssetBatchAppliedEventBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public AssetBatchAppliedEvent Build() =>
        new(
            BatchId: _batchId,
            CatalogInstanceId: _catalogInstanceId,
            OrganizationId: _organizationId,
            RequestedByService: _requestedByService,
            AppliedByUserId: _appliedByUserId,
            SourceOrderId: _sourceOrderId,
            SourceOrderTypeId: _sourceOrderTypeId,
            ScopeId: _scopeId,
            AppliedItems: _appliedItems,
            AffectedBounds: _affectedBounds,
            AppliedAtUtc: _appliedAtUtc,
            CorrelationId: _correlationId);
}
