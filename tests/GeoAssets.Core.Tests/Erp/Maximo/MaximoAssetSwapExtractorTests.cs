using FluentAssertions;
using GeoAssets.Core.Erp.Maximo;
using GeoAssets.Core.Messaging.Events;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Core.Tests.Erp.Maximo;

public class MaximoAssetSwapExtractorTests
{
    private static readonly Guid Org = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly FakeTimeProvider Clock = new();

    private static AssetFeatureMutationItem Item(
        string featureId, AssetMutationKind kind, string? serial = "SN-DEFAULT") => new(
        FeatureId: featureId,
        MutationKind: kind,
        Name: "feature",
        AssetTypeId: "pole",
        Description: "",
        LayerId: "",
        Geometry: null,
        TopologyEdges: [],
        CustomAttributes: serial is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["serialNumber"] = serial });

    private static AssetMutationBatchRequestedEvent Batch(IReadOnlyList<AssetFeatureMutationItem> mutations) =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "catalog-primary",
            organizationId: Org,
            requestedByService: "GeoAssets.FieldService.Server",
            performedByUserId: "inspector-1",
            mutations: mutations,
            timeProvider: Clock,
            sourceOrderId: "order-1",
            sourceOrderTypeId: "asset-swap");

    [Fact]
    public void TryExtractSwap_OneDecommissionAndOneUpsertWithSerials_ReturnsUpdate()
    {
        var oldItem = Item("feature-old", AssetMutationKind.Decommission, "SN-OLD-1");
        var newItem = Item("feature-new", AssetMutationKind.UpsertFeature, "SN-NEW-1");
        var batch = Batch([oldItem, newItem]);

        var result = MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock);

        result.Should().NotBeNull();
        result!.BatchId.Should().Be(batch.BatchId);
        result.OldSerial.Should().Be("SN-OLD-1");
        result.NewSerial.Should().Be("SN-NEW-1");
        result.LocationFeatureId.Should().Be("feature-new");
        result.OrganizationId.Should().Be(Org);
        result.SourceOrderId.Should().Be("order-1");
    }

    [Fact]
    public void TryExtractSwap_EmptyMutations_ReturnsNull()
    {
        MaximoAssetSwapExtractor.TryExtractSwap(Batch([]), Clock).Should().BeNull();
    }

    [Fact]
    public void TryExtractSwap_OnlyDecommission_ReturnsNull()
    {
        var batch = Batch([Item("feature-old", AssetMutationKind.Decommission)]);
        MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock).Should().BeNull();
    }

    [Fact]
    public void TryExtractSwap_OnlyUpsertFeature_ReturnsNull()
    {
        var batch = Batch([Item("feature-new", AssetMutationKind.UpsertFeature)]);
        MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock).Should().BeNull();
    }

    [Fact]
    public void TryExtractSwap_TwoDecommissionItems_ReturnsNull()
    {
        var batch = Batch([
            Item("feature-old-1", AssetMutationKind.Decommission),
            Item("feature-old-2", AssetMutationKind.Decommission),
            Item("feature-new", AssetMutationKind.UpsertFeature),
        ]);

        MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock).Should().BeNull();
    }

    [Fact]
    public void TryExtractSwap_TwoUpsertFeatureItems_ReturnsNull()
    {
        var batch = Batch([
            Item("feature-old", AssetMutationKind.Decommission),
            Item("feature-new-1", AssetMutationKind.UpsertFeature),
            Item("feature-new-2", AssetMutationKind.UpsertFeature),
        ]);

        MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryExtractSwap_DecommissionMissingSerial_ReturnsNull(string? serial)
    {
        var batch = Batch([
            Item("feature-old", AssetMutationKind.Decommission, serial),
            Item("feature-new", AssetMutationKind.UpsertFeature, "SN-NEW-1"),
        ]);

        MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryExtractSwap_UpsertFeatureMissingSerial_ReturnsNull(string? serial)
    {
        var batch = Batch([
            Item("feature-old", AssetMutationKind.Decommission, "SN-OLD-1"),
            Item("feature-new", AssetMutationKind.UpsertFeature, serial),
        ]);

        MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock).Should().BeNull();
    }

    [Fact]
    public void TryExtractSwap_OtherMutationKindsMixedIn_AreIgnoredAndPairIsStillExtracted()
    {
        var batch = Batch([
            Item("feature-attr", AssetMutationKind.UpdateAttributes),
            Item("feature-old", AssetMutationKind.Decommission, "SN-OLD-9"),
            Item("feature-edge-add", AssetMutationKind.AddTopologyEdge),
            Item("feature-new", AssetMutationKind.UpsertFeature, "SN-NEW-9"),
            Item("feature-edge-remove", AssetMutationKind.RemoveTopologyEdge),
        ]);

        var result = MaximoAssetSwapExtractor.TryExtractSwap(batch, Clock);

        result.Should().NotBeNull();
        result!.OldSerial.Should().Be("SN-OLD-9");
        result.NewSerial.Should().Be("SN-NEW-9");
    }
}
