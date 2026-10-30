namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Shared deterministic fluent builder entry points for integration messaging,
/// transactional outbox, idempotent inbox, and multi-catalog shard routing tests.
/// </summary>
public static class IntegrationMessagingTestDataBuilders
{
    public static readonly DateTimeOffset DefaultOccurredAtUtc = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid DefaultMessageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static readonly Guid DefaultBatchId = Guid.Parse("22222222-3333-4444-5555-666666666666");
    public static readonly Guid DefaultOrganizationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    public const string DefaultCatalogInstanceId = "catalog-primary";

    public static IntegrationEnvelopeBuilder Envelope() => new();

    public static IntegrationMessageContextBuilder MessageContext() => new();

    public static OrderStateChangedEventBuilder OrderStateChanged() => new();

    public static AssetMutationBatchRequestedEventBuilder AssetMutationBatchRequested() => new();

    public static AssetBatchAppliedEventBuilder AssetBatchApplied() => new();

    public static FeatureCatalogMutatedEventBuilder FeatureCatalogMutated() => new();

    public static TopologyInvalidatedEventBuilder TopologyInvalidated() => new();
}
