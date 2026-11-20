using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Messaging.Protobuf.V1;
using Google.Protobuf;
using Xunit;
using ProtoTimestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace GeoAssets.Workflow.Messaging.Protobuf.Tests;

/// <summary>
/// The wire contract of the approval saga's messages (XD01-383): the kernel's <c>Applied</c> and <c>Rejected</c> answers and the
/// requester's optional <c>SourceVerticalId</c> round-trip, and a batch from before the field existed still reads.
/// </summary>
public sealed class AssetMutationSagaProtobufTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid BatchId = Guid.Parse("33333333-4444-5555-6666-777777777777");
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ProtobufIntegrationMessageSerializer _serializer = new();

    // ── Rejected ──────────────────────────────────────────────────────────────

    [Fact]
    public void AssetMutationBatchRejectedEvent_WithEveryField_RoundTrips()
    {
        var expected = new AssetMutationBatchRejectedEvent(
            BatchId: BatchId,
            CatalogInstanceId: "catalog-north",
            OrganizationId: OrganizationId,
            RequestedByService: "GeoAssets.Workflow.OccupancyCommit",
            SourceOrderId: "ord-1",
            SourceOrderTypeId: "shared-use-pole-work",
            ScopeId: "zone-4",
            ReasonCode: AssetMutationBatchRejectionCodes.FeatureNotFound,
            Reason: "Feature 'pole-9' not found for UpdateAttributes.",
            ConflictingFeatureIds: ["pole-9", "pole-12"],
            RejectedAtUtc: FixedUtc,
            CorrelationId: "corr-1",
            SourceVerticalId: "neutral-operator");

        var actual = _serializer.Deserialize<AssetMutationBatchRejectedEvent>(_serializer.Serialize(expected));

        actual.Should().BeEquivalentTo(expected);
        actual.ConflictingFeatureIds.Should().Equal("pole-9", "pole-12");
    }

    [Fact]
    public void AssetMutationBatchRejectedEvent_WithOnlyTheRequiredFields_ReadsBackTheOptionalOnesAsNull_AndNoConflictingFeatures()
    {
        var expected = new AssetMutationBatchRejectedEvent(
            BatchId, "catalog-north", OrganizationId, "tests", null, null, null,
            AssetMutationBatchRejectionCodes.InvalidBatch, "malformed", [], FixedUtc);

        var actual = _serializer.Deserialize<AssetMutationBatchRejectedEvent>(_serializer.Serialize(expected));

        actual.SourceOrderId.Should().BeNull();
        actual.SourceOrderTypeId.Should().BeNull();
        actual.ScopeId.Should().BeNull();
        actual.CorrelationId.Should().BeNull();
        actual.SourceVerticalId.Should().BeNull();
        actual.ConflictingFeatureIds.Should().BeEmpty();
        actual.RejectedAtUtc.Should().Be(FixedUtc);
    }

    [Theory]
    [InlineData(AssetMutationBatchRejectionCodes.FeatureNotFound)]
    [InlineData(AssetMutationBatchRejectionCodes.ValidationFailed)]
    [InlineData(AssetMutationBatchRejectionCodes.Unsupported)]
    [InlineData(AssetMutationBatchRejectionCodes.InvalidBatch)]
    public void AssetMutationBatchRejectedEvent_EveryReasonCode_RoundTrips(string code)
    {
        var expected = new AssetMutationBatchRejectedEvent(
            BatchId, "catalog-north", OrganizationId, "tests", "ord-1", null, null, code, "why", [], FixedUtc);

        _serializer.Deserialize<AssetMutationBatchRejectedEvent>(_serializer.Serialize(expected)).ReasonCode.Should().Be(code);
    }

    [Fact]
    public void AssetMutationBatchRejectedEvent_ReasonCodeIsOnTheWireAsFieldEight_AndTheVerticalAsThirteen()
    {
        AssetMutationBatchRejectedEventProto.Descriptor.FindFieldByName("reason_code").FieldNumber.Should().Be(8);
        AssetMutationBatchRejectedEventProto.Descriptor.FindFieldByName("conflicting_feature_ids").FieldNumber.Should().Be(10);
        AssetMutationBatchRejectedEventProto.Descriptor.FindFieldByName("source_vertical_id").FieldNumber.Should().Be(13);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Serialize_ARejectionWithABlankReasonCode_ThrowsArgumentException(string blank)
    {
        var rejected = new AssetMutationBatchRejectedEvent(
            BatchId, "catalog-north", OrganizationId, "tests", null, null, null, blank, "why", [], FixedUtc);

        var act = () => _serializer.Serialize(rejected);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Serialize_ARejectionWithABlankCatalogInstanceId_ThrowsArgumentException()
    {
        var rejected = new AssetMutationBatchRejectedEvent(
            BatchId, " ", OrganizationId, "tests", null, null, null, AssetMutationBatchRejectionCodes.InvalidBatch, "why", [], FixedUtc);

        var act = () => _serializer.Serialize(rejected);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deserialize_ARejectionPayloadWithoutAReasonCode_ThrowsFormatException()
    {
        var proto = new AssetMutationBatchRejectedEventProto
        {
            BatchId = BatchId.ToString("D"),
            CatalogInstanceId = "catalog-north",
            OrganizationId = OrganizationId.ToString("D"),
            RejectedAtUtc = ProtoTimestamp.FromDateTimeOffset(FixedUtc),
        };

        var act = () => _serializer.Deserialize<AssetMutationBatchRejectedEvent>(proto.ToByteArray());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Deserialize_ARejectionPayloadWithAnInvalidBatchId_ThrowsFormatException()
    {
        var proto = new AssetMutationBatchRejectedEventProto
        {
            BatchId = "not-a-guid",
            CatalogInstanceId = "catalog-north",
            OrganizationId = OrganizationId.ToString("D"),
            ReasonCode = AssetMutationBatchRejectionCodes.InvalidBatch,
            RejectedAtUtc = ProtoTimestamp.FromDateTimeOffset(FixedUtc),
        };

        var act = () => _serializer.Deserialize<AssetMutationBatchRejectedEvent>(proto.ToByteArray());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Deserialize_ARejectionPayloadWithAnUnknownFutureField_IsReadCleanly()
    {
        var expected = new AssetMutationBatchRejectedEvent(
            BatchId, "catalog-north", OrganizationId, "tests", "ord-1", null, null, AssetMutationBatchRejectionCodes.Unsupported, "no", ["pole-1"], FixedUtc);
        using var buffer = new MemoryStream();
        var bytes = _serializer.Serialize(expected);
        buffer.Write(bytes, 0, bytes.Length);
        using (var coded = new CodedOutputStream(buffer, leaveOpen: true))
        {
            coded.WriteTag(99, WireFormat.WireType.LengthDelimited);
            coded.WriteString("future");
            coded.Flush();
        }

        var actual = _serializer.Deserialize<AssetMutationBatchRejectedEvent>(buffer.ToArray());

        actual.ReasonCode.Should().Be(AssetMutationBatchRejectionCodes.Unsupported);
        actual.ConflictingFeatureIds.Should().Equal("pole-1");
    }

    // ── Applied ───────────────────────────────────────────────────────────────

    [Fact]
    public void AssetBatchAppliedEvent_WithTheSourceVerticalId_RoundTripsIt()
    {
        var expected = AppliedEvent("neutral-operator");

        var actual = _serializer.Deserialize<AssetBatchAppliedEvent>(_serializer.Serialize(expected));

        actual.SourceVerticalId.Should().Be("neutral-operator");
        actual.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void AssetBatchAppliedEvent_WithoutTheSourceVerticalId_ReadsItBackAsNull()
    {
        var actual = _serializer.Deserialize<AssetBatchAppliedEvent>(_serializer.Serialize(AppliedEvent(null)));

        actual.SourceVerticalId.Should().BeNull();
    }

    [Fact]
    public void AssetBatchAppliedEvent_AnOlderPayloadWithoutFieldThirteen_StillReads_WithANullVertical()
    {
        var older = new AssetBatchAppliedEventProto
        {
            BatchId = BatchId.ToString("D"),
            CatalogInstanceId = "catalog-north",
            OrganizationId = OrganizationId.ToString("D"),
            RequestedByService = "workflow-worker",
            AppliedByUserId = "kernel",
            SourceOrderId = "ord-1",
            AppliedAtUtc = ProtoTimestamp.FromDateTimeOffset(FixedUtc),
            CorrelationId = "corr-1",
        };

        var actual = _serializer.Deserialize<AssetBatchAppliedEvent>(older.ToByteArray());

        actual.SourceVerticalId.Should().BeNull();
        actual.SourceOrderId.Should().Be("ord-1");
        actual.CorrelationId.Should().Be("corr-1");
        AssetBatchAppliedEventProto.Descriptor.FindFieldByName("source_vertical_id").FieldNumber.Should().Be(13);
    }

    // ── Requested ─────────────────────────────────────────────────────────────

    [Fact]
    public void AssetMutationBatchRequestedEvent_WithTheSourceVerticalId_RoundTripsIt()
    {
        var expected = RequestedEvent("neutral-operator");

        var actual = _serializer.Deserialize<AssetMutationBatchRequestedEvent>(_serializer.Serialize(expected));

        actual.SourceVerticalId.Should().Be("neutral-operator");
        actual.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void AssetMutationBatchRequestedEvent_WithoutTheSourceVerticalId_ReadsItBackAsNull()
    {
        var actual = _serializer.Deserialize<AssetMutationBatchRequestedEvent>(_serializer.Serialize(RequestedEvent(null)));

        actual.SourceVerticalId.Should().BeNull();
    }

    [Fact]
    public void AssetMutationBatchRequestedEvent_AnOlderPayloadWithoutFieldTwelve_StillReads_WithANullVertical()
    {
        var older = new AssetMutationBatchRequestedEventProto
        {
            BatchId = BatchId.ToString("D"),
            CatalogInstanceId = "catalog-north",
            OrganizationId = OrganizationId.ToString("D"),
            RequestedByService = "workflow-worker",
            PerformedByUserId = "engineer-1",
            SourceOrderId = "ord-1",
            RequestedAtUtc = ProtoTimestamp.FromDateTimeOffset(FixedUtc),
        };
        older.Mutations.Add(new AssetFeatureMutationItemProto
        {
            FeatureId = "pole-1",
            MutationKind = AssetMutationKindProto.UpdateAttributes,
            AssetTypeId = "pole",
        });

        var actual = _serializer.Deserialize<AssetMutationBatchRequestedEvent>(older.ToByteArray());

        actual.SourceVerticalId.Should().BeNull("a requester from before the field names no vertical");
        actual.SourceOrderId.Should().Be("ord-1");
        actual.Mutations.Should().ContainSingle().Which.FeatureId.Should().Be("pole-1");
        AssetMutationBatchRequestedEventProto.Descriptor.FindFieldByName("source_vertical_id").FieldNumber.Should().Be(12);
    }

    // ── Event types ───────────────────────────────────────────────────────────

    [Fact]
    public void TheSagaMessageTypes_AreVersionedNames_DistinctFromEachOther()
    {
        IntegrationEventTypes.AssetMutationBatchAppliedV1.Should().Be("geoassets.assets.mutation-batch-applied.v1");
        IntegrationEventTypes.AssetMutationBatchRejectedV1.Should().Be("geoassets.assets.mutation-batch-rejected.v1");
        new[] { IntegrationEventTypes.AssetMutationBatchRequestedV1, IntegrationEventTypes.AssetMutationBatchAppliedV1, IntegrationEventTypes.AssetMutationBatchRejectedV1 }
            .Distinct(StringComparer.Ordinal).Should().HaveCount(3);
    }

    [Fact]
    public void ARejectionIsNotReadableAsAnAppliedEvent_TheTwoPayloadsAreDistinctTypes()
    {
        var rejection = _serializer.Serialize(new AssetMutationBatchRejectedEvent(
            BatchId, "catalog-north", OrganizationId, "tests", null, null, null, AssetMutationBatchRejectionCodes.InvalidBatch, "x", [], FixedUtc));

        var act = () => _serializer.Deserialize<AssetBatchAppliedEvent>(rejection);

        act.Should().Throw<Exception>("the reason code field of a rejection is not an applied-by-user");
    }

    private static AssetBatchAppliedEvent AppliedEvent(string? vertical) => new(
        BatchId, "catalog-north", OrganizationId, "workflow-worker", "kernel", "ord-1", "shared-use-pole-work", "zone-4",
        [new AssetBatchAppliedItem("pole-1", AssetMutationKind.UpdateAttributes, "pole", [1, 2, 3, 4])],
        new Rfc7946BoundingBox(-38.52, -12.98, -38.49, -12.96), FixedUtc, "corr-1", vertical);

    private static AssetMutationBatchRequestedEvent RequestedEvent(string? vertical) => new(
        BatchId, "catalog-north", OrganizationId, "workflow-worker", "engineer-1", "ord-1", "shared-use-pole-work", "zone-4",
        [new AssetFeatureMutationItem("pole-1", AssetMutationKind.UpdateAttributes, string.Empty, "pole", string.Empty, string.Empty, null, [], new Dictionary<string, string>(StringComparer.Ordinal) { ["occupancyClass"] = "Low" })],
        FixedUtc, "corr-1", vertical);
}
