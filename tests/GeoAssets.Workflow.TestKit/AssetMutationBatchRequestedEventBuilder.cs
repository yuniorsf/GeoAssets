using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.TestKit;

public sealed class AssetMutationBatchRequestedEventBuilder
{
    private Guid _batchId = IntegrationMessagingTestDataBuilders.DefaultBatchId;
    private string _catalogInstanceId = IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId;
    private Guid _organizationId = IntegrationMessagingTestDataBuilders.DefaultOrganizationId;
    private string _requestedByService = "field-operations-service";
    private string _performedByUserId = "operator-1";
    private string? _sourceOrderId = "ord-100";
    private string? _sourceOrderTypeId = "inspection";
    private string? _scopeId = "sector-01";
    private IReadOnlyList<AssetFeatureMutationItem> _mutations =
    [
        new AssetFeatureMutationItem(
            FeatureId: "feat-100",
            MutationKind: AssetMutationKind.UpsertFeature,
            Name: "Node 100",
            AssetTypeId: "network-node",
            Description: "Primary junction node",
            LayerId: "layer-nodes",
            Geometry: new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [-38.5014, -12.9714], [], []),
            TopologyEdges:
            [
                new TopoEdgeSnapshot(
                    TargetId: "feat-101",
                    Kind: "connects_to",
                    Weight: 1.5,
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["phase"] = "ABC" })
            ],
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["material"] = "steel",
                ["installedYear"] = "2024"
            },
            ExpectedRowVersion: [0x01, 0x02, 0x03, 0x04])
    ];
    private DateTimeOffset _requestedAtUtc = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc;
    private string? _correlationId = "corr-batch-100";

    public AssetMutationBatchRequestedEventBuilder WithBatchId(Guid batchId)
    {
        _batchId = batchId;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithCatalogInstanceId(string catalogInstanceId)
    {
        _catalogInstanceId = catalogInstanceId;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithOrganizationId(Guid organizationId)
    {
        _organizationId = organizationId;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithRequestedByService(string requestedByService)
    {
        _requestedByService = requestedByService;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithPerformedByUserId(string performedByUserId)
    {
        _performedByUserId = performedByUserId;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithSourceOrderId(string? sourceOrderId)
    {
        _sourceOrderId = sourceOrderId;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithSourceOrderTypeId(string? sourceOrderTypeId)
    {
        _sourceOrderTypeId = sourceOrderTypeId;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithScopeId(string? scopeId)
    {
        _scopeId = scopeId;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithMutations(IReadOnlyList<AssetFeatureMutationItem> mutations)
    {
        _mutations = mutations;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithRequestedAtUtc(DateTimeOffset requestedAtUtc)
    {
        _requestedAtUtc = requestedAtUtc;
        return this;
    }

    public AssetMutationBatchRequestedEventBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public AssetMutationBatchRequestedEvent Build() =>
        new(
            BatchId: _batchId,
            CatalogInstanceId: _catalogInstanceId,
            OrganizationId: _organizationId,
            RequestedByService: _requestedByService,
            PerformedByUserId: _performedByUserId,
            SourceOrderId: _sourceOrderId,
            SourceOrderTypeId: _sourceOrderTypeId,
            ScopeId: _scopeId,
            Mutations: _mutations,
            RequestedAtUtc: _requestedAtUtc,
            CorrelationId: _correlationId);
}
