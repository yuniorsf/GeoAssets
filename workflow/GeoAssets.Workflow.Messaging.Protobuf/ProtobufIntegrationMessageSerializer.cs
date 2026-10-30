using System.Globalization;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Messaging.Protobuf.V1;
using GeoAssets.Workflow.Notifications;
using Google.Protobuf;
using ProtoTimestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace GeoAssets.Workflow.Messaging.Protobuf;

/// <summary>
/// Binary Protocol Buffers implementation of <see cref="IIntegrationMessageSerializer"/>
/// supporting core integration envelopes, workflow state events, and spatial asset catalog events.
/// </summary>
public sealed class ProtobufIntegrationMessageSerializer : IIntegrationMessageSerializer
{
    public const string ProtobufContentType = "application/x-protobuf";

    public string ContentType => ProtobufContentType;

    public byte[] Serialize<TMessage>(TMessage message)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            IntegrationEnvelope envelope => MapEnvelopeToProto(envelope, nameof(message)).ToByteArray(),
            OrderStateChangedEvent evt => MapOrderStateChangedToProto(evt, nameof(message)).ToByteArray(),
            AssetMutationBatchRequestedEvent evt => MapBatchRequestedToProto(evt, nameof(message)).ToByteArray(),
            AssetBatchAppliedEvent evt => MapBatchAppliedToProto(evt, nameof(message)).ToByteArray(),
            FeatureCatalogMutatedEvent evt => MapFeatureMutatedToProto(evt, nameof(message)).ToByteArray(),
            TopologyInvalidatedEvent evt => MapTopologyInvalidatedToProto(evt, nameof(message)).ToByteArray(),
            _ => throw new NotSupportedException(
                $"Message type '{typeof(TMessage).FullName}' is not supported by {nameof(ProtobufIntegrationMessageSerializer)}.")
        };
    }

    public TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> payload)
        where TMessage : notnull
    {
        EnsureSupportedType<TMessage>();

        if (payload.IsEmpty)
        {
            throw new FormatException(
                $"Cannot deserialize an empty Protobuf payload as '{typeof(TMessage).Name}'.");
        }

        try
        {
            object result = typeof(TMessage) switch
            {
                var t when t == typeof(IntegrationEnvelope) =>
                    MapEnvelopeFromProto(IntegrationEnvelopeProto.Parser.ParseFrom(payload.Span), nameof(payload)),
                var t when t == typeof(OrderStateChangedEvent) =>
                    MapOrderStateChangedFromProto(OrderStateChangedEventProto.Parser.ParseFrom(payload.Span)),
                var t when t == typeof(AssetMutationBatchRequestedEvent) =>
                    MapBatchRequestedFromProto(AssetMutationBatchRequestedEventProto.Parser.ParseFrom(payload.Span), nameof(payload)),
                var t when t == typeof(AssetBatchAppliedEvent) =>
                    MapBatchAppliedFromProto(AssetBatchAppliedEventProto.Parser.ParseFrom(payload.Span), nameof(payload)),
                var t when t == typeof(FeatureCatalogMutatedEvent) =>
                    MapFeatureMutatedFromProto(FeatureCatalogMutatedEventProto.Parser.ParseFrom(payload.Span), nameof(payload)),
                var t when t == typeof(TopologyInvalidatedEvent) =>
                    MapTopologyInvalidatedFromProto(TopologyInvalidatedEventProto.Parser.ParseFrom(payload.Span), nameof(payload)),
                _ => throw new NotSupportedException(
                    $"Message type '{typeof(TMessage).FullName}' is not supported by {nameof(ProtobufIntegrationMessageSerializer)}.")
            };

            return (TMessage)result;
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new FormatException(
                $"Failed to decode Protobuf wire payload as '{typeof(TMessage).Name}'.",
                ex);
        }
    }

    private static void EnsureSupportedType<TMessage>()
    {
        var target = typeof(TMessage);
        if (target != typeof(IntegrationEnvelope)
            && target != typeof(OrderStateChangedEvent)
            && target != typeof(AssetMutationBatchRequestedEvent)
            && target != typeof(AssetBatchAppliedEvent)
            && target != typeof(FeatureCatalogMutatedEvent)
            && target != typeof(TopologyInvalidatedEvent))
        {
            throw new NotSupportedException(
                $"Message type '{target.FullName}' is not supported by {nameof(ProtobufIntegrationMessageSerializer)}.");
        }
    }

    private static IntegrationEnvelopeProto MapEnvelopeToProto(IntegrationEnvelope envelope, string paramName)
    {
        if (string.IsNullOrWhiteSpace(envelope.MessageType))
        {
            throw new ArgumentException("IntegrationEnvelope.MessageType must not be empty.", paramName);
        }

        if (string.IsNullOrWhiteSpace(envelope.ContentType))
        {
            throw new ArgumentException("IntegrationEnvelope.ContentType must not be empty.", paramName);
        }

        if (envelope.CatalogInstanceId is not null && string.IsNullOrWhiteSpace(envelope.CatalogInstanceId))
        {
            throw new ArgumentException("IntegrationEnvelope.CatalogInstanceId cannot be whitespace when specified.", paramName);
        }

        var proto = new IntegrationEnvelopeProto
        {
            MessageId = FormatGuid(envelope.MessageId),
            MessageType = envelope.MessageType,
            SchemaVersion = envelope.SchemaVersion,
            PartitionKey = envelope.PartitionKey ?? string.Empty,
            ContentType = envelope.ContentType,
            Payload = ByteString.CopyFrom(envelope.Payload.Span),
            OccurredAtUtc = ToProtoTimestamp(envelope.OccurredAtUtc),
        };

        PopulateOptionalEnvelopeFields(proto, envelope);
        return proto;
    }

    private static void PopulateOptionalEnvelopeFields(
        IntegrationEnvelopeProto proto,
        IntegrationEnvelope envelope)
    {
        if (envelope.CatalogInstanceId is not null)
        {
            proto.CatalogInstanceId = envelope.CatalogInstanceId;
        }

        if (envelope.SourceService is not null)
        {
            proto.SourceService = envelope.SourceService;
        }

        if (envelope.CorrelationId is not null)
        {
            proto.CorrelationId = envelope.CorrelationId;
        }

        if (envelope.CausationId is not null)
        {
            proto.CausationId = envelope.CausationId;
        }

        if (envelope.TraceParent is not null)
        {
            proto.TraceParent = envelope.TraceParent;
        }

        if (envelope.TraceState is not null)
        {
            proto.TraceState = envelope.TraceState;
        }

        if (envelope.Headers is not null)
        {
            proto.HasHeaders = true;
            foreach (var (key, value) in envelope.Headers)
            {
                proto.Headers[key] = value;
            }
        }
    }

    private static IntegrationEnvelope MapEnvelopeFromProto(IntegrationEnvelopeProto proto, string paramName)
    {
        var messageId = ParseGuidOrThrow(proto.MessageId, "message_id");
        if (string.IsNullOrWhiteSpace(proto.MessageType))
        {
            throw new FormatException("IntegrationEnvelopeProto.message_type is required.");
        }

        if (string.IsNullOrWhiteSpace(proto.ContentType))
        {
            throw new FormatException("IntegrationEnvelopeProto.content_type is required.");
        }

        var occurredAtUtc = FromProtoTimestampOrThrow(proto.OccurredAtUtc, "occurred_at_utc");

        string? catalogInstanceId = null;
        if (proto.HasCatalogInstanceId)
        {
            if (string.IsNullOrWhiteSpace(proto.CatalogInstanceId))
            {
                throw new ArgumentException("IntegrationEnvelopeProto.catalog_instance_id cannot be whitespace when present.", paramName);
            }

            catalogInstanceId = proto.CatalogInstanceId;
        }

        Dictionary<string, string>? headers = null;
        if (proto.HasHeaders || proto.Headers.Count > 0)
        {
            headers = CopyMap(proto.Headers);
        }

        return new IntegrationEnvelope(
            MessageId: messageId,
            MessageType: proto.MessageType,
            SchemaVersion: proto.SchemaVersion,
            PartitionKey: proto.PartitionKey,
            ContentType: proto.ContentType,
            Payload: proto.Payload.ToByteArray(),
            OccurredAtUtc: occurredAtUtc,
            CatalogInstanceId: catalogInstanceId,
            SourceService: proto.HasSourceService ? proto.SourceService : null,
            CorrelationId: proto.HasCorrelationId ? proto.CorrelationId : null,
            CausationId: proto.HasCausationId ? proto.CausationId : null,
            TraceParent: proto.HasTraceParent ? proto.TraceParent : null,
            TraceState: proto.HasTraceState ? proto.TraceState : null,
            Headers: headers);
    }

    private static OrderStateChangedEventProto MapOrderStateChangedToProto(OrderStateChangedEvent evt, string paramName)
    {
        if (string.IsNullOrWhiteSpace(evt.OrderId))
        {
            throw new ArgumentException("OrderStateChangedEvent.OrderId must not be empty.", paramName);
        }

        var proto = new OrderStateChangedEventProto
        {
            OrderId = evt.OrderId,
            OrderTypeId = evt.OrderTypeId ?? string.Empty,
            PreviousStatus = evt.PreviousStatus ?? string.Empty,
            NewStatus = evt.NewStatus ?? string.Empty,
            PerformedBy = evt.PerformedBy ?? string.Empty,
            OccurredAt = ToProtoTimestamp(evt.OccurredAt),
        };

        if (evt.CorrelationId is not null)
        {
            proto.CorrelationId = evt.CorrelationId;
        }

        if (evt.Metadata is not null)
        {
            proto.HasMetadata = true;
            foreach (var (key, value) in evt.Metadata)
            {
                proto.Metadata[key] = value;
            }
        }

        return proto;
    }

    private static OrderStateChangedEvent MapOrderStateChangedFromProto(OrderStateChangedEventProto proto)
    {
        if (string.IsNullOrWhiteSpace(proto.OrderId))
        {
            throw new FormatException("OrderStateChangedEventProto.order_id is required.");
        }

        var occurredAt = FromProtoTimestampOrThrow(proto.OccurredAt, "occurred_at");

        Dictionary<string, string>? metadata = null;
        if (proto.HasMetadata || proto.Metadata.Count > 0)
        {
            metadata = CopyMap(proto.Metadata);
        }

        return new OrderStateChangedEvent(
            OrderId: proto.OrderId,
            OrderTypeId: proto.OrderTypeId,
            PreviousStatus: proto.PreviousStatus,
            NewStatus: proto.NewStatus,
            PerformedBy: proto.PerformedBy,
            OccurredAt: occurredAt,
            CorrelationId: proto.HasCorrelationId ? proto.CorrelationId : null,
            Metadata: metadata);
    }

    private static AssetMutationBatchRequestedEventProto MapBatchRequestedToProto(
        AssetMutationBatchRequestedEvent evt,
        string paramName)
    {
        ValidateCatalogInstanceId(evt.CatalogInstanceId, paramName);
        if (evt.Mutations is null)
        {
            throw new ArgumentException("AssetMutationBatchRequestedEvent.Mutations must not be null.", paramName);
        }

        var proto = new AssetMutationBatchRequestedEventProto
        {
            BatchId = FormatGuid(evt.BatchId),
            CatalogInstanceId = evt.CatalogInstanceId,
            OrganizationId = FormatGuid(evt.OrganizationId),
            RequestedByService = evt.RequestedByService ?? string.Empty,
            PerformedByUserId = evt.PerformedByUserId ?? string.Empty,
            RequestedAtUtc = ToProtoTimestamp(evt.RequestedAtUtc),
        };

        if (evt.SourceOrderId is not null)
        {
            proto.SourceOrderId = evt.SourceOrderId;
        }

        if (evt.SourceOrderTypeId is not null)
        {
            proto.SourceOrderTypeId = evt.SourceOrderTypeId;
        }

        if (evt.ScopeId is not null)
        {
            proto.ScopeId = evt.ScopeId;
        }

        if (evt.CorrelationId is not null)
        {
            proto.CorrelationId = evt.CorrelationId;
        }

        foreach (var item in evt.Mutations)
        {
            proto.Mutations.Add(MapMutationItemToProto(item, paramName));
        }

        return proto;
    }

    private static AssetMutationBatchRequestedEvent MapBatchRequestedFromProto(
        AssetMutationBatchRequestedEventProto proto,
        string paramName)
    {
        ValidateCatalogInstanceId(proto.CatalogInstanceId, paramName);
        var batchId = ParseGuidOrThrow(proto.BatchId, "batch_id");
        var organizationId = ParseGuidOrThrow(proto.OrganizationId, "organization_id");
        var requestedAtUtc = FromProtoTimestampOrThrow(proto.RequestedAtUtc, "requested_at_utc");

        var mutations = new List<AssetFeatureMutationItem>(proto.Mutations.Count);
        foreach (var itemProto in proto.Mutations)
        {
            mutations.Add(MapMutationItemFromProto(itemProto, paramName));
        }

        return new AssetMutationBatchRequestedEvent(
            BatchId: batchId,
            CatalogInstanceId: proto.CatalogInstanceId,
            OrganizationId: organizationId,
            RequestedByService: proto.RequestedByService,
            PerformedByUserId: proto.PerformedByUserId,
            SourceOrderId: proto.HasSourceOrderId ? proto.SourceOrderId : null,
            SourceOrderTypeId: proto.HasSourceOrderTypeId ? proto.SourceOrderTypeId : null,
            ScopeId: proto.HasScopeId ? proto.ScopeId : null,
            Mutations: mutations,
            RequestedAtUtc: requestedAtUtc,
            CorrelationId: proto.HasCorrelationId ? proto.CorrelationId : null);
    }

    private static AssetFeatureMutationItemProto MapMutationItemToProto(AssetFeatureMutationItem item, string paramName)
    {
        if (item is null)
        {
            throw new ArgumentException("Mutation item cannot be null.", paramName);
        }

        if (string.IsNullOrWhiteSpace(item.FeatureId))
        {
            throw new ArgumentException("AssetFeatureMutationItem.FeatureId must not be empty.", paramName);
        }

        var proto = new AssetFeatureMutationItemProto
        {
            FeatureId = item.FeatureId,
            MutationKind = MapMutationKindToProto(item.MutationKind, paramName),
            Name = item.Name ?? string.Empty,
            AssetTypeId = item.AssetTypeId ?? string.Empty,
            Description = item.Description ?? string.Empty,
            LayerId = item.LayerId ?? string.Empty,
        };

        if (item.Geometry is not null)
        {
            proto.Geometry = MapGeometryToProto(item.Geometry, paramName);
        }

        if (item.TopologyEdges is not null)
        {
            foreach (var edge in item.TopologyEdges)
            {
                proto.TopologyEdges.Add(MapTopoEdgeToProto(edge, paramName));
            }
        }

        if (item.CustomAttributes is not null)
        {
            foreach (var (key, value) in item.CustomAttributes)
            {
                proto.CustomAttributes[key] = value;
            }
        }

        if (item.ExpectedRowVersion is not null)
        {
            proto.ExpectedRowVersion = ByteString.CopyFrom(item.ExpectedRowVersion);
        }

        return proto;
    }

    private static AssetFeatureMutationItem MapMutationItemFromProto(AssetFeatureMutationItemProto proto, string paramName)
    {
        if (string.IsNullOrWhiteSpace(proto.FeatureId))
        {
            throw new FormatException("AssetFeatureMutationItemProto.feature_id is required.");
        }

        var edges = new List<TopoEdgeSnapshot>(proto.TopologyEdges.Count);
        foreach (var edgeProto in proto.TopologyEdges)
        {
            edges.Add(MapTopoEdgeFromProto(edgeProto));
        }

        return new AssetFeatureMutationItem(
            FeatureId: proto.FeatureId,
            MutationKind: MapMutationKindFromProto(proto.MutationKind, paramName),
            Name: proto.Name,
            AssetTypeId: proto.AssetTypeId,
            Description: proto.Description,
            LayerId: proto.LayerId,
            Geometry: proto.Geometry is null ? null : MapGeometryFromProto(proto.Geometry, paramName),
            TopologyEdges: edges,
            CustomAttributes: CopyMap(proto.CustomAttributes),
            ExpectedRowVersion: proto.HasExpectedRowVersion ? proto.ExpectedRowVersion.ToByteArray() : null);
    }

    private static TopoEdgeSnapshotProto MapTopoEdgeToProto(TopoEdgeSnapshot edge, string paramName)
    {
        if (edge is null)
        {
            throw new ArgumentException("Topology edge snapshot cannot be null.", paramName);
        }

        if (!double.IsFinite(edge.Weight))
        {
            throw new ArgumentOutOfRangeException(paramName, edge.Weight, "Topology edge weight must be finite.");
        }

        var proto = new TopoEdgeSnapshotProto
        {
            TargetId = edge.TargetId ?? string.Empty,
            Kind = edge.Kind ?? string.Empty,
            Weight = edge.Weight,
        };

        if (edge.Metadata is not null)
        {
            proto.HasMetadata = true;
            foreach (var (key, value) in edge.Metadata)
            {
                proto.Metadata[key] = value;
            }
        }

        return proto;
    }

    private static TopoEdgeSnapshot MapTopoEdgeFromProto(TopoEdgeSnapshotProto proto)
    {
        Dictionary<string, string>? metadata = null;
        if (proto.HasMetadata || proto.Metadata.Count > 0)
        {
            metadata = CopyMap(proto.Metadata);
        }

        return new TopoEdgeSnapshot(
            TargetId: proto.TargetId,
            Kind: proto.Kind,
            Weight: proto.Weight,
            Metadata: metadata);
    }

    private static AssetBatchAppliedEventProto MapBatchAppliedToProto(AssetBatchAppliedEvent evt, string paramName)
    {
        ValidateCatalogInstanceId(evt.CatalogInstanceId, paramName);
        if (evt.AppliedItems is null)
        {
            throw new ArgumentException("AssetBatchAppliedItems must not be null.", paramName);
        }

        var proto = new AssetBatchAppliedEventProto
        {
            BatchId = FormatGuid(evt.BatchId),
            CatalogInstanceId = evt.CatalogInstanceId,
            OrganizationId = FormatGuid(evt.OrganizationId),
            RequestedByService = evt.RequestedByService ?? string.Empty,
            AppliedByUserId = evt.AppliedByUserId ?? string.Empty,
            AppliedAtUtc = ToProtoTimestamp(evt.AppliedAtUtc),
        };

        if (evt.SourceOrderId is not null)
        {
            proto.SourceOrderId = evt.SourceOrderId;
        }

        if (evt.SourceOrderTypeId is not null)
        {
            proto.SourceOrderTypeId = evt.SourceOrderTypeId;
        }

        if (evt.ScopeId is not null)
        {
            proto.ScopeId = evt.ScopeId;
        }

        if (evt.AffectedBounds is not null)
        {
            proto.AffectedBounds = MapBoundingBoxToProto(evt.AffectedBounds, paramName);
        }

        if (evt.CorrelationId is not null)
        {
            proto.CorrelationId = evt.CorrelationId;
        }

        foreach (var appliedItem in evt.AppliedItems)
        {
            proto.AppliedItems.Add(MapBatchAppliedItemToProto(appliedItem, paramName));
        }

        return proto;
    }

    private static AssetBatchAppliedItemProto MapBatchAppliedItemToProto(
        AssetBatchAppliedItem? appliedItem,
        string paramName)
    {
        if (appliedItem is null)
        {
            throw new ArgumentException("Applied item cannot be null.", paramName);
        }

        return new AssetBatchAppliedItemProto
        {
            FeatureId = appliedItem.FeatureId ?? string.Empty,
            MutationKind = MapMutationKindToProto(appliedItem.MutationKind, paramName),
            AssetTypeId = appliedItem.AssetTypeId ?? string.Empty,
            RowVersion = ByteString.CopyFrom(appliedItem.RowVersion ?? []),
        };
    }

    private static AssetBatchAppliedEvent MapBatchAppliedFromProto(
        AssetBatchAppliedEventProto proto,
        string paramName)
    {
        ValidateCatalogInstanceId(proto.CatalogInstanceId, paramName);
        var batchId = ParseGuidOrThrow(proto.BatchId, "batch_id");
        var organizationId = ParseGuidOrThrow(proto.OrganizationId, "organization_id");
        var appliedAtUtc = FromProtoTimestampOrThrow(proto.AppliedAtUtc, "applied_at_utc");

        var appliedItems = new List<AssetBatchAppliedItem>(proto.AppliedItems.Count);
        foreach (var itemProto in proto.AppliedItems)
        {
            appliedItems.Add(new AssetBatchAppliedItem(
                FeatureId: itemProto.FeatureId,
                MutationKind: MapMutationKindFromProto(itemProto.MutationKind, paramName),
                AssetTypeId: itemProto.AssetTypeId,
                RowVersion: itemProto.RowVersion.ToByteArray()));
        }

        return new AssetBatchAppliedEvent(
            BatchId: batchId,
            CatalogInstanceId: proto.CatalogInstanceId,
            OrganizationId: organizationId,
            RequestedByService: proto.RequestedByService,
            AppliedByUserId: proto.AppliedByUserId,
            SourceOrderId: proto.HasSourceOrderId ? proto.SourceOrderId : null,
            SourceOrderTypeId: proto.HasSourceOrderTypeId ? proto.SourceOrderTypeId : null,
            ScopeId: proto.HasScopeId ? proto.ScopeId : null,
            AppliedItems: appliedItems,
            AffectedBounds: proto.AffectedBounds is null ? null : MapBoundingBoxFromProto(proto.AffectedBounds, paramName),
            AppliedAtUtc: appliedAtUtc,
            CorrelationId: proto.HasCorrelationId ? proto.CorrelationId : null);
    }

    private static FeatureCatalogMutatedEventProto MapFeatureMutatedToProto(
        FeatureCatalogMutatedEvent evt,
        string paramName)
    {
        ValidateCatalogInstanceId(evt.CatalogInstanceId, paramName);
        if (string.IsNullOrWhiteSpace(evt.FeatureId))
        {
            throw new ArgumentException("FeatureCatalogMutatedEvent.FeatureId must not be empty.", paramName);
        }

        var proto = new FeatureCatalogMutatedEventProto
        {
            CatalogInstanceId = evt.CatalogInstanceId,
            OrganizationId = FormatGuid(evt.OrganizationId),
            FeatureId = evt.FeatureId,
            MutationKind = MapMutationKindToProto(evt.MutationKind, paramName),
            Name = evt.Name ?? string.Empty,
            AssetTypeId = evt.AssetTypeId ?? string.Empty,
            LayerId = evt.LayerId ?? string.Empty,
            RowVersion = ByteString.CopyFrom(evt.RowVersion ?? []),
            MutatedAtUtc = ToProtoTimestamp(evt.MutatedAtUtc),
        };

        if (evt.ScopeId is not null)
        {
            proto.ScopeId = evt.ScopeId;
        }

        if (evt.Geometry is not null)
        {
            proto.Geometry = MapGeometryToProto(evt.Geometry, paramName);
        }

        if (evt.CustomAttributes is not null)
        {
            foreach (var (key, value) in evt.CustomAttributes)
            {
                proto.CustomAttributes[key] = value;
            }
        }

        if (evt.AffectedBounds is not null)
        {
            proto.AffectedBounds = MapBoundingBoxToProto(evt.AffectedBounds, paramName);
        }

        if (evt.CorrelationId is not null)
        {
            proto.CorrelationId = evt.CorrelationId;
        }

        return proto;
    }

    private static FeatureCatalogMutatedEvent MapFeatureMutatedFromProto(
        FeatureCatalogMutatedEventProto proto,
        string paramName)
    {
        ValidateCatalogInstanceId(proto.CatalogInstanceId, paramName);
        var organizationId = ParseGuidOrThrow(proto.OrganizationId, "organization_id");
        if (string.IsNullOrWhiteSpace(proto.FeatureId))
        {
            throw new FormatException("FeatureCatalogMutatedEventProto.feature_id is required.");
        }

        var mutatedAtUtc = FromProtoTimestampOrThrow(proto.MutatedAtUtc, "mutated_at_utc");

        return new FeatureCatalogMutatedEvent(
            CatalogInstanceId: proto.CatalogInstanceId,
            OrganizationId: organizationId,
            ScopeId: proto.HasScopeId ? proto.ScopeId : null,
            FeatureId: proto.FeatureId,
            MutationKind: MapMutationKindFromProto(proto.MutationKind, paramName),
            Name: proto.Name,
            AssetTypeId: proto.AssetTypeId,
            LayerId: proto.LayerId,
            Geometry: proto.Geometry is null ? null : MapGeometryFromProto(proto.Geometry, paramName),
            CustomAttributes: CopyMap(proto.CustomAttributes),
            AffectedBounds: proto.AffectedBounds is null ? null : MapBoundingBoxFromProto(proto.AffectedBounds, paramName),
            RowVersion: proto.RowVersion.ToByteArray(),
            MutatedAtUtc: mutatedAtUtc,
            CorrelationId: proto.HasCorrelationId ? proto.CorrelationId : null);
    }

    private static TopologyInvalidatedEventProto MapTopologyInvalidatedToProto(
        TopologyInvalidatedEvent evt,
        string paramName)
    {
        ValidateCatalogInstanceId(evt.CatalogInstanceId, paramName);

        var proto = new TopologyInvalidatedEventProto
        {
            CatalogInstanceId = evt.CatalogInstanceId,
            OrganizationId = FormatGuid(evt.OrganizationId),
            Reason = evt.Reason ?? string.Empty,
            InvalidatedAtUtc = ToProtoTimestamp(evt.InvalidatedAtUtc),
        };

        if (evt.ScopeId is not null)
        {
            proto.ScopeId = evt.ScopeId;
        }

        if (evt.AffectedFeatureIds is not null)
        {
            proto.AffectedFeatureIds.AddRange(evt.AffectedFeatureIds);
        }

        if (evt.AffectedEdgeKinds is not null)
        {
            proto.AffectedEdgeKinds.AddRange(evt.AffectedEdgeKinds);
        }

        if (evt.AffectedBounds is not null)
        {
            proto.AffectedBounds = MapBoundingBoxToProto(evt.AffectedBounds, paramName);
        }

        if (evt.CorrelationId is not null)
        {
            proto.CorrelationId = evt.CorrelationId;
        }

        return proto;
    }

    private static TopologyInvalidatedEvent MapTopologyInvalidatedFromProto(
        TopologyInvalidatedEventProto proto,
        string paramName)
    {
        ValidateCatalogInstanceId(proto.CatalogInstanceId, paramName);
        var organizationId = ParseGuidOrThrow(proto.OrganizationId, "organization_id");
        var invalidatedAtUtc = FromProtoTimestampOrThrow(proto.InvalidatedAtUtc, "invalidated_at_utc");

        return new TopologyInvalidatedEvent(
            CatalogInstanceId: proto.CatalogInstanceId,
            OrganizationId: organizationId,
            ScopeId: proto.HasScopeId ? proto.ScopeId : null,
            AffectedFeatureIds: [.. proto.AffectedFeatureIds],
            AffectedEdgeKinds: [.. proto.AffectedEdgeKinds],
            AffectedBounds: proto.AffectedBounds is null ? null : MapBoundingBoxFromProto(proto.AffectedBounds, paramName),
            Reason: proto.Reason,
            InvalidatedAtUtc: invalidatedAtUtc,
            CorrelationId: proto.HasCorrelationId ? proto.CorrelationId : null);
    }

    private static AssetGeometrySnapshotProto MapGeometryToProto(AssetGeometrySnapshot geometry, string paramName)
    {
        ValidateGeometry(
            geometry.Kind,
            geometry.Srid,
            geometry.PointCoordinates,
            geometry.LineCoordinates,
            geometry.PolygonRings,
            paramName);

        var proto = new AssetGeometrySnapshotProto
        {
            Kind = MapGeometryKindToProto(geometry.Kind, paramName),
            Srid = geometry.Srid,
        };

        switch (geometry.Kind)
        {
            case WireGeometryKind.Point:
                proto.PointCoordinates.AddRange(geometry.PointCoordinates);
                break;

            case WireGeometryKind.LineString:
                foreach (var position in geometry.LineCoordinates)
                {
                    var posProto = new CoordinatePositionProto();
                    posProto.Values.AddRange(position);
                    proto.LineCoordinates.Add(posProto);
                }

                break;

            case WireGeometryKind.Polygon:
                foreach (var ring in geometry.PolygonRings)
                {
                    var ringProto = new CoordinateRingProto();
                    foreach (var position in ring)
                    {
                        var posProto = new CoordinatePositionProto();
                        posProto.Values.AddRange(position);
                        ringProto.Positions.Add(posProto);
                    }

                    proto.PolygonRings.Add(ringProto);
                }

                break;
        }

        return proto;
    }

    private static AssetGeometrySnapshot MapGeometryFromProto(AssetGeometrySnapshotProto proto, string paramName)
    {
        var kind = MapGeometryKindFromProto(proto.Kind, paramName);

        var pointCoords = new List<double>(proto.PointCoordinates.Count);
        pointCoords.AddRange(proto.PointCoordinates);

        var lineCoords = new List<IReadOnlyList<double>>(proto.LineCoordinates.Count);
        foreach (var posProto in proto.LineCoordinates)
        {
            lineCoords.Add([.. posProto.Values]);
        }

        var polygonRings = new List<IReadOnlyList<IReadOnlyList<double>>>(proto.PolygonRings.Count);
        foreach (var ringProto in proto.PolygonRings)
        {
            var ring = new List<IReadOnlyList<double>>(ringProto.Positions.Count);
            foreach (var posProto in ringProto.Positions)
            {
                ring.Add([.. posProto.Values]);
            }

            polygonRings.Add(ring);
        }

        ValidateGeometry(kind, proto.Srid, pointCoords, lineCoords, polygonRings, paramName);

        return new AssetGeometrySnapshot(
            Kind: kind,
            Srid: proto.Srid,
            PointCoordinates: pointCoords,
            LineCoordinates: lineCoords,
            PolygonRings: polygonRings);
    }

    private static Rfc7946BoundingBoxProto MapBoundingBoxToProto(Rfc7946BoundingBox bounds, string paramName)
    {
        ValidateBoundingBox(
            bounds.MinLongitude,
            bounds.MinLatitude,
            bounds.MaxLongitude,
            bounds.MaxLatitude,
            paramName);

        return new Rfc7946BoundingBoxProto
        {
            MinLongitude = bounds.MinLongitude,
            MinLatitude = bounds.MinLatitude,
            MaxLongitude = bounds.MaxLongitude,
            MaxLatitude = bounds.MaxLatitude,
        };
    }

    private static Rfc7946BoundingBox MapBoundingBoxFromProto(Rfc7946BoundingBoxProto proto, string paramName)
    {
        ValidateBoundingBox(
            proto.MinLongitude,
            proto.MinLatitude,
            proto.MaxLongitude,
            proto.MaxLatitude,
            paramName);

        return new Rfc7946BoundingBox(
            MinLongitude: proto.MinLongitude,
            MinLatitude: proto.MinLatitude,
            MaxLongitude: proto.MaxLongitude,
            MaxLatitude: proto.MaxLatitude);
    }

    private static void ValidateCatalogInstanceId(string? catalogInstanceId, string paramName)
    {
        if (string.IsNullOrWhiteSpace(catalogInstanceId))
        {
            throw new ArgumentException("CatalogInstanceId must be a non-empty identifier.", paramName);
        }
    }

    private static void ValidateGeometry(
        WireGeometryKind kind,
        int srid,
        IReadOnlyList<double> pointCoordinates,
        IReadOnlyList<IReadOnlyList<double>> lineCoordinates,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> polygonRings,
        string paramName)
    {
        if (srid <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, srid, "Geometry SRID must be positive.");
        }

        switch (kind)
        {
            case WireGeometryKind.Point:
                ValidateRfc7946Position(pointCoordinates, srid, paramName);
                break;

            case WireGeometryKind.LineString:
                if (lineCoordinates is null || lineCoordinates.Count < 2)
                {
                    throw new ArgumentException("LineString geometry requires at least two positions.", paramName);
                }

                foreach (var position in lineCoordinates)
                {
                    ValidateRfc7946Position(position, srid, paramName);
                }

                break;

            case WireGeometryKind.Polygon:
                ValidatePolygonRings(polygonRings, srid, paramName);
                break;

            default:
                throw new ArgumentException($"Unsupported or unspecified geometry kind '{kind}'.", paramName);
        }
    }

    private static void ValidatePolygonRings(
        IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>>? polygonRings,
        int srid,
        string paramName)
    {
        if (polygonRings is null || polygonRings.Count < 1)
        {
            throw new ArgumentException("Polygon geometry requires at least one linear ring.", paramName);
        }

        foreach (var ring in polygonRings)
        {
            if (ring is null || ring.Count < 4)
            {
                throw new ArgumentException(
                    "RFC 7946 Polygon linear ring requires at least four positions.",
                    paramName);
            }

            foreach (var position in ring)
            {
                ValidateRfc7946Position(position, srid, paramName);
            }
        }
    }

    private static void ValidateRfc7946Position(IReadOnlyList<double>? position, int srid, string paramName)
    {
        if (position is null || position.Count is < 2 or > 3)
        {
            throw new ArgumentException(
                "RFC 7946 position must contain 2 ([lon, lat]) or 3 ([lon, lat, alt]) coordinates.",
                paramName);
        }

        var xOrLon = position[0];
        var yOrLat = position[1];

        if (!double.IsFinite(xOrLon) || !double.IsFinite(yOrLat))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "Coordinate values must be finite IEEE-754 floating-point numbers.");
        }

        if (position.Count == 3 && !double.IsFinite(position[2]))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                position[2],
                "Altitude coordinate must be a finite IEEE-754 floating-point number.");
        }

        if (srid == 4326)
        {
            if (xOrLon is < -180.0 or > 180.0)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    xOrLon,
                    "RFC 7946 longitude (position[0]) must be within [-180.0, 180.0].");
            }

            if (yOrLat is < -90.0 or > 90.0)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    yOrLat,
                    "RFC 7946 latitude (position[1]) must be within [-90.0, 90.0].");
            }
        }
    }

    private static void ValidateBoundingBox(
        double minLongitude,
        double minLatitude,
        double maxLongitude,
        double maxLatitude,
        string paramName)
    {
        if (!double.IsFinite(minLongitude)
            || !double.IsFinite(minLatitude)
            || !double.IsFinite(maxLongitude)
            || !double.IsFinite(maxLatitude))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "RFC 7946 bounding box coordinates must be finite IEEE-754 numbers.");
        }

        if (minLongitude is < -180.0 or > 180.0 || maxLongitude is < -180.0 or > 180.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "RFC 7946 bounding box longitudes must be within [-180.0, 180.0].");
        }

        if (minLatitude is < -90.0 or > 90.0 || maxLatitude is < -90.0 or > 90.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "RFC 7946 bounding box latitudes must be within [-90.0, 90.0].");
        }

        if (minLongitude > maxLongitude || minLatitude > maxLatitude)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "RFC 7946 bounding box minimum coordinates must not exceed maximum coordinates.");
        }
    }

    private static AssetMutationKindProto MapMutationKindToProto(AssetMutationKind kind, string paramName) =>
        kind switch
        {
            AssetMutationKind.UpsertFeature => AssetMutationKindProto.UpsertFeature,
            AssetMutationKind.UpdateAttributes => AssetMutationKindProto.UpdateAttributes,
            AssetMutationKind.AddTopologyEdge => AssetMutationKindProto.AddTopologyEdge,
            AssetMutationKind.RemoveTopologyEdge => AssetMutationKindProto.RemoveTopologyEdge,
            AssetMutationKind.Decommission => AssetMutationKindProto.Decommission,
            AssetMutationKind.Unspecified => AssetMutationKindProto.Unspecified,
            _ => throw new ArgumentOutOfRangeException(paramName, kind, $"Unknown AssetMutationKind '{kind}'.")
        };

    private static AssetMutationKind MapMutationKindFromProto(AssetMutationKindProto kind, string paramName) =>
        kind switch
        {
            AssetMutationKindProto.UpsertFeature => AssetMutationKind.UpsertFeature,
            AssetMutationKindProto.UpdateAttributes => AssetMutationKind.UpdateAttributes,
            AssetMutationKindProto.AddTopologyEdge => AssetMutationKind.AddTopologyEdge,
            AssetMutationKindProto.RemoveTopologyEdge => AssetMutationKind.RemoveTopologyEdge,
            AssetMutationKindProto.Decommission => AssetMutationKind.Decommission,
            AssetMutationKindProto.Unspecified => AssetMutationKind.Unspecified,
            _ => throw new ArgumentOutOfRangeException(paramName, kind, $"Unknown AssetMutationKindProto '{kind}'.")
        };

    private static WireGeometryKindProto MapGeometryKindToProto(WireGeometryKind kind, string paramName) =>
        kind switch
        {
            WireGeometryKind.Point => WireGeometryKindProto.Point,
            WireGeometryKind.LineString => WireGeometryKindProto.LineString,
            WireGeometryKind.Polygon => WireGeometryKindProto.Polygon,
            _ => throw new ArgumentOutOfRangeException(paramName, kind, $"Unsupported WireGeometryKind '{kind}'.")
        };

    private static WireGeometryKind MapGeometryKindFromProto(WireGeometryKindProto kind, string paramName) =>
        kind switch
        {
            WireGeometryKindProto.Point => WireGeometryKind.Point,
            WireGeometryKindProto.LineString => WireGeometryKind.LineString,
            WireGeometryKindProto.Polygon => WireGeometryKind.Polygon,
            _ => throw new ArgumentOutOfRangeException(paramName, kind, $"Unsupported WireGeometryKindProto '{kind}'.")
        };

    private static string FormatGuid(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    private static Guid ParseGuidOrThrow(string raw, string fieldName)
    {
        if (!Guid.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new FormatException($"Protobuf field '{fieldName}' does not contain a valid GUID: '{raw}'.");
        }

        return parsed;
    }

    private static ProtoTimestamp ToProtoTimestamp(DateTimeOffset value) =>
        ProtoTimestamp.FromDateTimeOffset(value.ToUniversalTime());

    private static DateTimeOffset FromProtoTimestampOrThrow(ProtoTimestamp? value, string fieldName)
    {
        if (value is null)
        {
            throw new FormatException($"Protobuf timestamp field '{fieldName}' is required.");
        }

        return value.ToDateTimeOffset();
    }

    private static Dictionary<string, string> CopyMap(Google.Protobuf.Collections.MapField<string, string> source)
    {
        var copy = new Dictionary<string, string>(source.Count, StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            copy[key] = value;
        }

        return copy;
    }
}

