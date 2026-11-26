using FluentAssertions;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Shapefile;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Tests.Fakes;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Shapefile;

public sealed class ShapefileDeliveryInventoryTests : IDisposable
{
    private readonly ShapefileDeliveryBuilder _delivery = new();
    private readonly FakeSourceProfile _profile = ShapefileTestProfile.Create();

    public void Dispose() => _delivery.Dispose();

    private Task<DeliveryInventory> InspectAsync() =>
        new ShapefileDeliveryInventory().InspectAsync(_delivery.Root, _profile, CancellationToken.None);

    private static LayerFileSet Layer(DeliveryInventory inventory, string region, string layer) =>
        inventory.Layers.Single(l => string.Equals(l.Region, region, StringComparison.Ordinal) && string.Equals(l.Layer, layer, StringComparison.Ordinal));

    [Fact]
    public async Task Inspect_EveryFolderWithFiles_IsARegionNamedByItsRelativePath()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(2));
        _delivery.WriteLayer("Norte", "POSTE", ShapefileTestFeatures.Poles(3, 555000));
        _delivery.WriteLayer("Norte/Sub", "POSTE", ShapefileTestFeatures.Poles(4, 556000));

        var inventory = await InspectAsync();

        inventory.Layers.Select(l => l.Region).Should().BeEquivalentTo(["", "Norte", "Norte/Sub"]);
        inventory.ProfileId.Should().Be("test-profile");
        inventory.RootPath.Should().Be(_delivery.Root);
    }

    [Fact]
    public async Task Inspect_CompleteLayer_IsUsableWithPathsCrsEncodingAndRecordCount()
    {
        _delivery.WriteLayer("Norte", "POSTE", ShapefileTestFeatures.Poles(7));

        var layer = Layer(await InspectAsync(), "Norte", "POSTE");

        layer.Verdict.Should().Be(LayerVerdict.Usable);
        layer.RecordCount.Should().Be(7);
        layer.GeometryPath.Should().Be(_delivery.PathOf("Norte", "POSTE.shp"));
        layer.AttributeTablePath.Should().Be(_delivery.PathOf("Norte", "POSTE.dbf"));
        layer.CrsWkt.Should().Be(ShapefileDeliveryBuilder.Sad69Utm24SWkt);
        layer.Encoding.Should().Be("UTF-8");
    }

    [Fact]
    public async Task Inspect_MissingCpg_DefaultsToIso88591()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(1), cpg: null);

        Layer(await InspectAsync(), "", "POSTE").Encoding.Should().Be("ISO-8859-1");
    }

    [Fact]
    public async Task Inspect_MissingPrj_LeavesTheCrsUnset()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(1), prj: false);

        Layer(await InspectAsync(), "", "POSTE").CrsWkt.Should().BeNull();
    }

    [Fact]
    public async Task Inspect_FilesThatMatchNoProfileLayer_AreNotLayers()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(1));
        _delivery.WriteLayer("", "UNRELATED", ShapefileTestFeatures.Poles(1));

        var inventory = await InspectAsync();

        inventory.Layers.Select(l => l.Layer).Should().BeEquivalentTo(["POSTE"]);
    }

    [Fact]
    public async Task Inspect_MissingDbfWithoutAnOrphan_IsGeometryOnlyWithAWarning()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(3), dbf: false);

        var inventory = await InspectAsync();

        var layer = Layer(inventory, "", "POSTE");
        layer.Verdict.Should().Be(LayerVerdict.GeometryOnly);
        layer.AttributeTablePath.Should().BeNull();
        layer.RecordCount.Should().Be(3);
        inventory.Issues.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.MissingSidecar, StringComparison.Ordinal) && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public async Task Inspect_MissingDbfWithAnOrphanOfTheSameRecordCount_PairsItWithAnInfoIssue()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(3), dbf: false);
        var orphan = _delivery.WriteOrphanTable("", "POSTE_ATTRS", ShapefileTestFeatures.Poles(3));

        var inventory = await InspectAsync();

        var layer = Layer(inventory, "", "POSTE");
        layer.Verdict.Should().Be(LayerVerdict.Usable);
        layer.AttributeTablePath.Should().Be(orphan);
        inventory.Issues.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.OrphanAttributeTable, StringComparison.Ordinal) && i.Severity == IssueSeverity.Info);
    }

    [Fact]
    public async Task Inspect_OrphanWithADifferentRecordCount_IsNotPaired()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(3), dbf: false);
        _delivery.WriteOrphanTable("", "POSTE_ATTRS", ShapefileTestFeatures.Poles(5));

        var layer = Layer(await InspectAsync(), "", "POSTE");

        layer.Verdict.Should().Be(LayerVerdict.GeometryOnly);
        layer.AttributeTablePath.Should().BeNull();
    }

    [Fact]
    public async Task Inspect_OrphanAlreadyPairedToAnotherLayer_IsNotPairedTwice()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(3), dbf: false);
        _delivery.WriteLayer("", "REDE_MT", ShapefileTestFeatures.Lines(3), dbf: false);
        _delivery.WriteOrphanTable("", "ATTRS", ShapefileTestFeatures.Poles(3));

        var inventory = await InspectAsync();

        inventory.Layers.Count(l => l.AttributeTablePath is not null).Should().Be(1);
        inventory.Layers.Count(l => l.Verdict == LayerVerdict.GeometryOnly).Should().Be(1);
    }

    [Fact]
    public async Task Inspect_AttributeTableWithoutGeometry_IsUnusableWithAMissingSidecarIssue()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(3));
        File.Delete(_delivery.PathOf("", "POSTE.shp"));
        File.Delete(_delivery.PathOf("", "POSTE.shx"));

        var inventory = await InspectAsync();

        Layer(inventory, "", "POSTE").Verdict.Should().Be(LayerVerdict.Unusable);
        inventory.Issues.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.MissingSidecar, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_IdenticalShapeContentInAnotherRegion_MakesTheLaterOneUnusable()
    {
        _delivery.WriteLayer("A", "POSTE", ShapefileTestFeatures.Poles(3));
        _delivery.CopyGeometry("A", "POSTE", "B");

        var inventory = await InspectAsync();

        Layer(inventory, "A", "POSTE").Verdict.Should().Be(LayerVerdict.Usable);
        Layer(inventory, "B", "POSTE").Verdict.Should().Be(LayerVerdict.Unusable);
        inventory.Issues.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.DuplicateFile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_DifferentShapeContentInAnotherRegion_IsNotADuplicate()
    {
        _delivery.WriteLayer("A", "POSTE", ShapefileTestFeatures.Poles(3));
        _delivery.WriteLayer("B", "POSTE", ShapefileTestFeatures.Poles(3, 600000));

        var inventory = await InspectAsync();

        inventory.Layers.Should().OnlyContain(l => l.Verdict == LayerVerdict.Usable);
        inventory.Issues.Should().NotContain(i => string.Equals(i.Code, ImportIssueCodes.DuplicateFile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_SentinelCoordinates_MakeTheLayerUnusableAsCorruptGeometry()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.PointsWithSentinels(100, 10));

        var inventory = await InspectAsync();

        Layer(inventory, "", "POSTE").Verdict.Should().Be(LayerVerdict.Unusable);
        inventory.Issues.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.CorruptGeometry, StringComparison.Ordinal) && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Inspect_AFewSentinelCoordinatesUnderOnePercent_AreTolerated()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.PointsWithSentinels(1000, 5));

        Layer(await InspectAsync(), "", "POSTE").Verdict.Should().Be(LayerVerdict.Usable);
    }

    [Fact]
    public async Task Inspect_QuantizedXCoordinates_MakeTheLayerUnusableAsCorruptGeometry()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.QuantizedPoints(1000));

        var inventory = await InspectAsync();

        Layer(inventory, "", "POSTE").Verdict.Should().Be(LayerVerdict.Unusable);
        inventory.Issues.Should().Contain(i => string.Equals(i.Code, ImportIssueCodes.CorruptGeometry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_FewRepeatedXCoordinates_AreNotCorruptBelowOneThousandShapes()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.QuantizedPoints(100));

        Layer(await InspectAsync(), "", "POSTE").Verdict.Should().Be(LayerVerdict.Usable);
    }

    [Fact]
    public async Task Inspect_LineageFieldMap_GivesDbfNamesTheirFullNames()
    {
        _delivery.WriteLayer("", "REDE_MT", ShapefileTestFeatures.Lines(2),
            lineageFieldMap: "NU_TRECHO_MT_ID \"Trecho\" true true false 10 Long 0 10 ,First,#,src,NU_TRECHO_MT_ID,-1,-1;NU_PG_INICIAL_ID \"PG inicial\" true true false 10 Long 0 10 ,First,#,src,NU_PG_INICIAL_ID,-1,-1");

        var layer = Layer(await InspectAsync(), "", "REDE_MT");

        layer.FieldAliases["NU_TRECHO_M"].Should().Be("NU_TRECHO_MT_ID");
        layer.FieldAliases["NU_PG_INIC"].Should().Be("NU_PG_INICIAL_ID");
    }

    [Fact]
    public async Task Inspect_LineageWithADifferentFieldCount_FallsBackToTheProfileAliases()
    {
        _delivery.WriteLayer("", "REDE_MT", ShapefileTestFeatures.Lines(2),
            lineageFieldMap: "ONLY_ONE \"Only\" true true false 10 Long 0 10 ,First,#,src,ONLY_ONE,-1,-1");

        var layer = Layer(await InspectAsync(), "", "REDE_MT");

        layer.FieldAliases["NU_PG_INIC"].Should().Be("NU_PG_INICIAL_ID");
        layer.FieldAliases.Should().NotContainKey("ONLY_ONE");
    }

    [Fact]
    public async Task Inspect_WithoutALineageFile_UsesTheProfileFallbackAliases()
    {
        _delivery.WriteLayer("", "REDE_MT", ShapefileTestFeatures.Lines(2));

        var layer = Layer(await InspectAsync(), "", "REDE_MT");

        layer.FieldAliases["NU_PG_INIC"].Should().Be("NU_PG_INICIAL_ID");
    }

    [Fact]
    public async Task Inspect_Cancelled_Throws()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(1));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => new ShapefileDeliveryInventory().InspectAsync(_delivery.Root, _profile, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
