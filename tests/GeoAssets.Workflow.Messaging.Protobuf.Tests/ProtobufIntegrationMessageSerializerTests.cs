using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Messaging.Protobuf.V1;
using GeoAssets.Workflow.Notifications;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ProtoTimestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace GeoAssets.Workflow.Messaging.Protobuf.Tests;

public sealed class ProtobufIntegrationMessageSerializerTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SampleMessageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid SampleBatchId = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid SampleOrgId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ProtobufIntegrationMessageSerializer _serializer = new();

    [Fact]
    public void ContentType_ReturnsApplicationXProtobuf()
    {
        _serializer.ContentType.Should().Be("application/x-protobuf");
        _serializer.ContentType.Should().Be(ProtobufIntegrationMessageSerializer.ProtobufContentType);
    }

    [Fact]
    public void IntegrationEnvelope_FullMetadataAndCatalogInstanceId_RoundTrips()
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-region"] = "northeast",
            ["x-priority"] = "1"
        };
        var innerPayload = new byte[] { 0x08, 0x96, 0x01, 0x12, 0x04, 0x6E, 0x6F, 0x64, 0x65 };
        var expected = new IntegrationEnvelope(
            MessageId: SampleMessageId,
            MessageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            SchemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            PartitionKey: "catalog-alpha:feat-100",
            ContentType: ProtobufIntegrationMessageSerializer.ProtobufContentType,
            Payload: innerPayload,
            OccurredAtUtc: FixedUtc,
            CatalogInstanceId: "catalog-alpha",
            SourceService: "geoassets-core",
            CorrelationId: "corr-01",
            CausationId: "caus-01",
            TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            TraceState: "vendor=trace",
            Headers: headers);

        var bytes = _serializer.Serialize(expected);
        var actual = _serializer.Deserialize<IntegrationEnvelope>(bytes);

        actual.MessageId.Should().Be(expected.MessageId);
        actual.MessageType.Should().Be(expected.MessageType);
        actual.SchemaVersion.Should().Be(expected.SchemaVersion);
        actual.PartitionKey.Should().Be(expected.PartitionKey);
        actual.ContentType.Should().Be(expected.ContentType);
        actual.Payload.ToArray().Should().Equal(innerPayload);
        actual.OccurredAtUtc.Should().Be(FixedUtc);
        actual.CatalogInstanceId.Should().Be("catalog-alpha");
        actual.SourceService.Should().Be("geoassets-core");
        actual.CorrelationId.Should().Be("corr-01");
        actual.CausationId.Should().Be("caus-01");
        actual.TraceParent.Should().Be(expected.TraceParent);
        actual.TraceState.Should().Be(expected.TraceState);
        actual.Headers.Should().NotBeNull();
        actual.Headers.Should().Equal(headers);
    }

    [Fact]
    public void IntegrationEnvelope_NullOptionalFieldsVsEmptyHeaders_PreservesPresence()
    {
        var withNullHeaders = new IntegrationEnvelope(
            MessageId: SampleMessageId,
            MessageType: IntegrationEventTypes.OrderStateChangedV1,
            SchemaVersion: 1,
            PartitionKey: "ord-1",
            ContentType: ProtobufIntegrationMessageSerializer.ProtobufContentType,
            Payload: new byte[] { 0x01 },
            OccurredAtUtc: FixedUtc,
            CatalogInstanceId: null,
            Headers: null);

        var withEmptyHeaders = withNullHeaders with
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var decodedNull = _serializer.Deserialize<IntegrationEnvelope>(_serializer.Serialize(withNullHeaders));
        var decodedEmpty = _serializer.Deserialize<IntegrationEnvelope>(_serializer.Serialize(withEmptyHeaders));

        decodedNull.CatalogInstanceId.Should().BeNull();
        decodedNull.SourceService.Should().BeNull();
        decodedNull.CorrelationId.Should().BeNull();
        decodedNull.CausationId.Should().BeNull();
        decodedNull.TraceParent.Should().BeNull();
        decodedNull.TraceState.Should().BeNull();
        decodedNull.Headers.Should().BeNull();

        decodedEmpty.Headers.Should().NotBeNull();
        decodedEmpty.Headers.Should().BeEmpty();
    }

    [Fact]
    public void OrderStateChangedEvent_WithMetadataAndCorrelationId_RoundTrips()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recipients"] = "crew-1,crew-2",
            ["channel"] = "dispatch"
        };
        var expected = new OrderStateChangedEvent(
            OrderId: "ord-2026-01",
            OrderTypeId: "inspection",
            PreviousStatus: "Pending",
            NewStatus: "InProgress",
            PerformedBy: "operator-7",
            OccurredAt: FixedUtc,
            CorrelationId: "corr-order-7",
            Metadata: metadata);

        var bytes = _serializer.Serialize(expected);
        var actual = _serializer.Deserialize<OrderStateChangedEvent>(bytes);

        actual.OrderId.Should().Be(expected.OrderId);
        actual.OrderTypeId.Should().Be(expected.OrderTypeId);
        actual.PreviousStatus.Should().Be(expected.PreviousStatus);
        actual.NewStatus.Should().Be(expected.NewStatus);
        actual.PerformedBy.Should().Be(expected.PerformedBy);
        actual.OccurredAt.Should().Be(FixedUtc);
        actual.CorrelationId.Should().Be("corr-order-7");
        actual.Metadata.Should().NotBeNull();
        actual.Metadata.Should().Equal(metadata);
    }

    [Fact]
    public void OrderStateChangedEvent_NullMetadataVsEmptyMetadata_PreservesPresence()
    {
        var nullMeta = new OrderStateChangedEvent(
            OrderId: "ord-1",
            OrderTypeId: "inspection",
            PreviousStatus: "Draft",
            NewStatus: "Pending",
            PerformedBy: "user-1",
            OccurredAt: FixedUtc,
            CorrelationId: null,
            Metadata: null);

        var emptyMeta = nullMeta with
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var decodedNull = _serializer.Deserialize<OrderStateChangedEvent>(_serializer.Serialize(nullMeta));
        var decodedEmpty = _serializer.Deserialize<OrderStateChangedEvent>(_serializer.Serialize(emptyMeta));

        decodedNull.CorrelationId.Should().BeNull();
        decodedNull.Metadata.Should().BeNull();

        decodedEmpty.Metadata.Should().NotBeNull();
        decodedEmpty.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void AssetMutationBatchRequestedEvent_WithPointTopologyAndRowVersion_RoundTrips()
    {
        var expectedRowVersion = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var edgeMeta = new Dictionary<string, string>(StringComparer.Ordinal) { ["circuit"] = "C1" };
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal) { ["material"] = "composite" };

        var expected = new AssetMutationBatchRequestedEvent(
            BatchId: SampleBatchId,
            CatalogInstanceId: "catalog-north",
            OrganizationId: SampleOrgId,
            RequestedByService: "workflow-worker",
            PerformedByUserId: "engineer-1",
            SourceOrderId: "ord-55",
            SourceOrderTypeId: "installation",
            ScopeId: "zone-4",
            Mutations:
            [
                new AssetFeatureMutationItem(
                    FeatureId: "feat-1",
                    MutationKind: AssetMutationKind.UpsertFeature,
                    Name: "Junction 1",
                    AssetTypeId: "junction-node",
                    Description: "Primary junction",
                    LayerId: "layer-primary",
                    Geometry: new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [-38.5014, -12.9714], [], []),
                    TopologyEdges: [new TopoEdgeSnapshot("feat-2", "connects_to", 2.5, edgeMeta)],
                    CustomAttributes: attrs,
                    ExpectedRowVersion: expectedRowVersion),
                new AssetFeatureMutationItem(
                    FeatureId: "feat-old",
                    MutationKind: AssetMutationKind.Decommission,
                    Name: "Retired Node",
                    AssetTypeId: "junction-node",
                    Description: string.Empty,
                    LayerId: "layer-primary",
                    Geometry: null,
                    TopologyEdges: [],
                    CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal),
                    ExpectedRowVersion: null)
            ],
            RequestedAtUtc: FixedUtc,
            CorrelationId: "corr-batch-1");

        var bytes = _serializer.Serialize(expected);
        var actual = _serializer.Deserialize<AssetMutationBatchRequestedEvent>(bytes);

        actual.BatchId.Should().Be(SampleBatchId);
        actual.CatalogInstanceId.Should().Be("catalog-north");
        actual.OrganizationId.Should().Be(SampleOrgId);
        actual.RequestedByService.Should().Be("workflow-worker");
        actual.PerformedByUserId.Should().Be("engineer-1");
        actual.SourceOrderId.Should().Be("ord-55");
        actual.SourceOrderTypeId.Should().Be("installation");
        actual.ScopeId.Should().Be("zone-4");
        actual.RequestedAtUtc.Should().Be(FixedUtc);
        actual.CorrelationId.Should().Be("corr-batch-1");
        actual.Mutations.Should().HaveCount(2);

        var first = actual.Mutations[0];
        first.FeatureId.Should().Be("feat-1");
        first.MutationKind.Should().Be(AssetMutationKind.UpsertFeature);
        first.Geometry.Should().NotBeNull();
        first.Geometry!.PointCoordinates.Should().Equal(-38.5014, -12.9714);
        first.TopologyEdges.Should().ContainSingle();
        first.TopologyEdges[0].TargetId.Should().Be("feat-2");
        first.TopologyEdges[0].Weight.Should().Be(2.5);
        first.TopologyEdges[0].Metadata.Should().Equal(edgeMeta);
        first.CustomAttributes.Should().Equal(attrs);
        first.ExpectedRowVersion.Should().Equal(expectedRowVersion);

        var second = actual.Mutations[1];
        second.MutationKind.Should().Be(AssetMutationKind.Decommission);
        second.Geometry.Should().BeNull();
        second.ExpectedRowVersion.Should().BeNull();
    }

    [Fact]
    public void AssetBatchAppliedEvent_WithAppliedItemsAndBoundingBox_RoundTrips()
    {
        var rowVersion1 = new byte[] { 0x00, 0x00, 0x00, 0x01 };
        var rowVersion2 = new byte[] { 0x00, 0x00, 0x00, 0x02 };
        var expected = new AssetBatchAppliedEvent(
            BatchId: SampleBatchId,
            CatalogInstanceId: "catalog-south",
            OrganizationId: SampleOrgId,
            RequestedByService: "workflow-worker",
            AppliedByUserId: "system-applier",
            SourceOrderId: "ord-77",
            SourceOrderTypeId: "maintenance",
            ScopeId: "zone-9",
            AppliedItems:
            [
                new AssetBatchAppliedItem("feat-1", AssetMutationKind.UpsertFeature, "node", rowVersion1),
                new AssetBatchAppliedItem("feat-2", AssetMutationKind.UpdateAttributes, "segment", rowVersion2)
            ],
            AffectedBounds: new Rfc7946BoundingBox(-46.70, -23.60, -46.60, -23.50),
            AppliedAtUtc: FixedUtc,
            CorrelationId: "corr-applied-77");

        var bytes = _serializer.Serialize(expected);
        var actual = _serializer.Deserialize<AssetBatchAppliedEvent>(bytes);

        actual.BatchId.Should().Be(SampleBatchId);
        actual.CatalogInstanceId.Should().Be("catalog-south");
        actual.OrganizationId.Should().Be(SampleOrgId);
        actual.AppliedByUserId.Should().Be("system-applier");
        actual.AppliedItems.Should().HaveCount(2);
        actual.AppliedItems[0].RowVersion.Should().Equal(rowVersion1);
        actual.AppliedItems[1].RowVersion.Should().Equal(rowVersion2);
        actual.AffectedBounds.Should().Be(new Rfc7946BoundingBox(-46.70, -23.60, -46.60, -23.50));
        actual.AppliedAtUtc.Should().Be(FixedUtc);
        actual.CorrelationId.Should().Be("corr-applied-77");
    }

    [Fact]
    public void FeatureCatalogMutatedEvent_With2DPointAndRowVersion_RoundTrips()
    {
        var rowVersion = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["capacity"] = "150",
            ["tier"] = "backbone"
        };
        var expected = new FeatureCatalogMutatedEvent(
            CatalogInstanceId: "catalog-west",
            OrganizationId: SampleOrgId,
            ScopeId: "scope-west-1",
            FeatureId: "feat-900",
            MutationKind: AssetMutationKind.UpdateAttributes,
            Name: "Station 900",
            AssetTypeId: "station",
            LayerId: "stations",
            Geometry: new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [-43.1729, -22.9068], [], []),
            CustomAttributes: attrs,
            AffectedBounds: new Rfc7946BoundingBox(-43.18, -22.91, -43.17, -22.90),
            RowVersion: rowVersion,
            MutatedAtUtc: FixedUtc,
            CorrelationId: "corr-mut-900");

        var bytes = _serializer.Serialize(expected);
        var actual = _serializer.Deserialize<FeatureCatalogMutatedEvent>(bytes);

        actual.CatalogInstanceId.Should().Be("catalog-west");
        actual.OrganizationId.Should().Be(SampleOrgId);
        actual.ScopeId.Should().Be("scope-west-1");
        actual.FeatureId.Should().Be("feat-900");
        actual.MutationKind.Should().Be(AssetMutationKind.UpdateAttributes);
        actual.Name.Should().Be("Station 900");
        actual.Geometry.Should().NotBeNull();
        actual.Geometry!.PointCoordinates.Should().Equal(-43.1729, -22.9068);
        actual.CustomAttributes.Should().Equal(attrs);
        actual.AffectedBounds.Should().Be(expected.AffectedBounds);
        actual.RowVersion.Should().Equal(rowVersion);
        actual.MutatedAtUtc.Should().Be(FixedUtc);
    }

    [Fact]
    public void TopologyInvalidatedEvent_RoundTripsAllFields()
    {
        var expected = new TopologyInvalidatedEvent(
            CatalogInstanceId: "catalog-central",
            OrganizationId: SampleOrgId,
            ScopeId: null,
            AffectedFeatureIds: ["feat-10", "feat-11", "feat-12"],
            AffectedEdgeKinds: ["connects_to", "feeds"],
            AffectedBounds: new Rfc7946BoundingBox(-38.55, -13.00, -38.45, -12.90),
            Reason: "SubgraphReconfigured",
            InvalidatedAtUtc: FixedUtc,
            CorrelationId: null);

        var bytes = _serializer.Serialize(expected);
        var actual = _serializer.Deserialize<TopologyInvalidatedEvent>(bytes);

        actual.CatalogInstanceId.Should().Be("catalog-central");
        actual.OrganizationId.Should().Be(SampleOrgId);
        actual.ScopeId.Should().BeNull();
        actual.AffectedFeatureIds.Should().Equal("feat-10", "feat-11", "feat-12");
        actual.AffectedEdgeKinds.Should().Equal("connects_to", "feeds");
        actual.AffectedBounds.Should().Be(expected.AffectedBounds);
        actual.Reason.Should().Be("SubgraphReconfigured");
        actual.InvalidatedAtUtc.Should().Be(FixedUtc);
        actual.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void Geometry_3DPointWithAltitude_RoundTrips()
    {
        var geometry3D = new AssetGeometrySnapshot(
            WireGeometryKind.Point,
            4326,
            [-38.5014, -12.9714, 78.25],
            [],
            []);
        var evt = CreateFeatureMutatedEvent(geometry: geometry3D);

        var decoded = _serializer.Deserialize<FeatureCatalogMutatedEvent>(_serializer.Serialize(evt));

        decoded.Geometry.Should().NotBeNull();
        decoded.Geometry!.Kind.Should().Be(WireGeometryKind.Point);
        decoded.Geometry.PointCoordinates.Should().Equal(-38.5014, -12.9714, 78.25);
    }

    [Fact]
    public void Geometry_3DLineString_RoundTrips()
    {
        IReadOnlyList<IReadOnlyList<double>> vertices =
        [
            [-38.52, -12.98, 10.0],
            [-38.51, -12.97, 14.5],
            [-38.50, -12.96, 19.0]
        ];
        var lineGeometry = new AssetGeometrySnapshot(WireGeometryKind.LineString, 4326, [], vertices, []);
        var evt = CreateFeatureMutatedEvent(geometry: lineGeometry);

        var decoded = _serializer.Deserialize<FeatureCatalogMutatedEvent>(_serializer.Serialize(evt));

        decoded.Geometry.Should().NotBeNull();
        decoded.Geometry!.Kind.Should().Be(WireGeometryKind.LineString);
        decoded.Geometry.LineCoordinates.Should().HaveCount(3);
        decoded.Geometry.LineCoordinates[0].Should().Equal(-38.52, -12.98, 10.0);
        decoded.Geometry.LineCoordinates[1].Should().Equal(-38.51, -12.97, 14.5);
        decoded.Geometry.LineCoordinates[2].Should().Equal(-38.50, -12.96, 19.0);
    }

    [Fact]
    public void Geometry_PolygonWithExteriorAndInteriorHoleRings_RoundTrips()
    {
        IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> rings =
        [
            [
                [-38.60, -13.00],
                [-38.40, -13.00],
                [-38.40, -12.80],
                [-38.60, -12.80],
                [-38.60, -13.00]
            ],
            [
                [-38.55, -12.95],
                [-38.55, -12.85],
                [-38.45, -12.85],
                [-38.45, -12.95],
                [-38.55, -12.95]
            ]
        ];
        var polygon = new AssetGeometrySnapshot(WireGeometryKind.Polygon, 4326, [], [], rings);
        var evt = CreateFeatureMutatedEvent(geometry: polygon);

        var decoded = _serializer.Deserialize<FeatureCatalogMutatedEvent>(_serializer.Serialize(evt));

        decoded.Geometry.Should().NotBeNull();
        decoded.Geometry!.Kind.Should().Be(WireGeometryKind.Polygon);
        decoded.Geometry.PolygonRings.Should().HaveCount(2);
        decoded.Geometry.PolygonRings[0].Should().HaveCount(5);
        decoded.Geometry.PolygonRings[1].Should().HaveCount(5);
    }

    [Theory]
    [InlineData(37.7749, -122.4194)] // Swapped [lat, lon] where -122.4194 violates lat in [-90, 90]
    [InlineData(35.6762, 139.6503)]  // Swapped [lat, lon] where 139.6503 violates lat in [-90, 90]
    [InlineData(-180.001, 0.0)]
    [InlineData(180.001, 0.0)]
    [InlineData(0.0, -90.001)]
    [InlineData(0.0, 90.001)]
    public void Serialize_OutOfBoundsOrSwappedRfc7946PointCoordinates_ThrowsArgumentException(
        double lonSlot,
        double latSlot)
    {
        var act = () =>
        {
            var invalid = new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [lonSlot, latSlot], [], []);
            var evt = CreateFeatureMutatedEvent(geometry: invalid);
            _ = _serializer.Serialize(evt);
        };

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(double.NaN, -12.97)]
    [InlineData(-38.50, double.PositiveInfinity)]
    [InlineData(-38.50, double.NegativeInfinity)]
    public void Serialize_NonFiniteCoordinates_ThrowsArgumentException(double lon, double lat)
    {
        var act = () =>
        {
            var invalid = new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [lon, lat], [], []);
            var evt = CreateFeatureMutatedEvent(geometry: invalid);
            _ = _serializer.Serialize(evt);
        };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deserialize_WirePayloadWithSwappedLatLon_ThrowsArgumentOutOfRangeException()
    {
        var proto = new FeatureCatalogMutatedEventProto
        {
            CatalogInstanceId = "catalog-alpha",
            OrganizationId = SampleOrgId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            FeatureId = "feat-swapped",
            MutationKind = AssetMutationKindProto.UpsertFeature,
            Name = "Swapped",
            AssetTypeId = "node",
            LayerId = "layer-1",
            Geometry = new AssetGeometrySnapshotProto
            {
                Kind = WireGeometryKindProto.Point,
                Srid = 4326,
                PointCoordinates = { 37.7749, -122.4194 }
            },
            RowVersion = ByteString.CopyFrom([0x01]),
            MutatedAtUtc = ProtoTimestamp.FromDateTimeOffset(FixedUtc)
        };

        var wireBytes = proto.ToByteArray();
        var act = () => _serializer.Deserialize<FeatureCatalogMutatedEvent>(wireBytes);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-38.40, -12.98, -38.50, -12.90)] // minLon > maxLon
    [InlineData(-38.50, -12.80, -38.40, -12.90)] // minLat > maxLat
    [InlineData(-190.0, -12.98, -38.40, -12.90)] // minLon < -180
    [InlineData(-38.50, -12.98, -38.40, 95.0)]   // maxLat > 90
    public void Serialize_InvalidRfc7946BoundingBox_ThrowsArgumentException(
        double minLon,
        double minLat,
        double maxLon,
        double maxLat)
    {
        var act = () =>
        {
            var bbox = new Rfc7946BoundingBox(minLon, minLat, maxLon, maxLat);
            var evt = CreateFeatureMutatedEvent(bounds: bbox);
            _ = _serializer.Serialize(evt);
        };

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Serialize_BlankCatalogInstanceId_ThrowsArgumentException(string blankCatalogId)
    {
        var act = () =>
        {
            var evt = CreateFeatureMutatedEvent(catalogInstanceId: blankCatalogId);
            _ = _serializer.Serialize(evt);
        };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deserialize_WirePayloadMissingCatalogInstanceId_ThrowsArgumentException()
    {
        var proto = new TopologyInvalidatedEventProto
        {
            CatalogInstanceId = "   ",
            OrganizationId = SampleOrgId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            Reason = "Test",
            InvalidatedAtUtc = ProtoTimestamp.FromDateTimeOffset(FixedUtc)
        };

        var wireBytes = proto.ToByteArray();
        var act = () => _serializer.Deserialize<TopologyInvalidatedEvent>(wireBytes);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForwardCompatibility_UnknownFieldTag99AppendedViaCodedOutputStream_IsIgnoredCleanly()
    {
        var original = CreateFeatureMutatedEvent();
        var baseBytes = _serializer.Serialize(original);

        using var buffer = new MemoryStream();
        buffer.Write(baseBytes, 0, baseBytes.Length);
        using (var coded = new CodedOutputStream(buffer, leaveOpen: true))
        {
            // Field number 99, wire type 2 (LengthDelimited): (99 << 3) | 2 = 794
            coded.WriteTag(99, WireFormat.WireType.LengthDelimited);
            coded.WriteString("future-schema-v2-payload");
            coded.Flush();
        }

        var extendedBytes = buffer.ToArray();
        var decoded = _serializer.Deserialize<FeatureCatalogMutatedEvent>(extendedBytes);

        decoded.CatalogInstanceId.Should().Be(original.CatalogInstanceId);
        decoded.FeatureId.Should().Be(original.FeatureId);
        decoded.RowVersion.Should().Equal(original.RowVersion);
        decoded.Geometry!.PointCoordinates.Should().Equal(-38.5014, -12.9714);
    }

    [Fact]
    public void Deserialize_CorruptProtobufWireBytes_ThrowsFormatExceptionWrappingInvalidProtocolBufferException()
    {
        var corruptBytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0x0B };

        var act = () => _serializer.Deserialize<FeatureCatalogMutatedEvent>(corruptBytes);

        act.Should().Throw<FormatException>()
            .WithInnerException<InvalidProtocolBufferException>();
    }

    [Fact]
    public void Deserialize_EmptyPayload_ThrowsFormatException()
    {
        var act = () => _serializer.Deserialize<OrderStateChangedEvent>(ReadOnlyMemory<byte>.Empty);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Deserialize_WirePayloadWithInvalidGuid_ThrowsFormatException()
    {
        var proto = new IntegrationEnvelopeProto
        {
            MessageId = "not-a-valid-guid",
            MessageType = IntegrationEventTypes.OrderStateChangedV1,
            SchemaVersion = 1,
            ContentType = ProtobufIntegrationMessageSerializer.ProtobufContentType,
            OccurredAtUtc = ProtoTimestamp.FromDateTimeOffset(FixedUtc)
        };

        var act = () => _serializer.Deserialize<IntegrationEnvelope>(proto.ToByteArray());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void SerializeAndDeserialize_UnsupportedTypeOrNull_ThrowsExpectedExceptions()
    {
        var nullAct = () => _serializer.Serialize<OrderStateChangedEvent>(null!);
        nullAct.Should().Throw<ArgumentNullException>();

        var unsupportedSerialize = () => _serializer.Serialize("raw-string");
        unsupportedSerialize.Should().Throw<NotSupportedException>();

        var unsupportedDeserialize = () => _serializer.Deserialize<string>(new byte[] { 0x08, 0x01 });
        unsupportedDeserialize.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void AddWorkflowProtobufMessaging_RegistersSingletonSerializer_AndValidatesNullServices()
    {
        IServiceCollection? nullServices = null;
        var nullAct = () => nullServices!.AddWorkflowProtobufMessaging();
        nullAct.Should().Throw<ArgumentNullException>();

        var services = new ServiceCollection();
        services.AddWorkflowProtobufMessaging();
        services.AddWorkflowProtobufMessaging();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IIntegrationMessageSerializer>();
        var second = provider.GetRequiredService<IIntegrationMessageSerializer>();

        first.Should().BeOfType<ProtobufIntegrationMessageSerializer>();
        first.Should().BeSameAs(second);
        provider.GetServices<IIntegrationMessageSerializer>().Should().ContainSingle();
    }

    private static FeatureCatalogMutatedEvent CreateFeatureMutatedEvent(
        string catalogInstanceId = "catalog-alpha",
        AssetGeometrySnapshot? geometry = null,
        Rfc7946BoundingBox? bounds = null) =>
        new(
            CatalogInstanceId: catalogInstanceId,
            OrganizationId: SampleOrgId,
            ScopeId: "scope-1",
            FeatureId: "feat-100",
            MutationKind: AssetMutationKind.UpsertFeature,
            Name: "Node 100",
            AssetTypeId: "network-node",
            LayerId: "layer-nodes",
            Geometry: geometry ?? new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [-38.5014, -12.9714], [], []),
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" },
            AffectedBounds: bounds ?? new Rfc7946BoundingBox(-38.51, -12.98, -38.50, -12.97),
            RowVersion: [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08],
            MutatedAtUtc: FixedUtc,
            CorrelationId: "corr-100");
}

