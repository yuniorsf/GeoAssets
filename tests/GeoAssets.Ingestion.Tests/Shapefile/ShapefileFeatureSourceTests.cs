using System.Text;
using FluentAssertions;
using GeoAssets.Ingestion.Shapefile;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Tests.Fakes;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Shapefile;

public sealed class ShapefileFeatureSourceTests : IDisposable
{
    private readonly ShapefileDeliveryBuilder _delivery = new();

    public void Dispose() => _delivery.Dispose();

    private LayerFileSet LayerOf(
        string stem,
        string encoding = "UTF-8",
        bool withTable = true,
        IReadOnlyDictionary<string, string>? aliases = null) => new()
        {
            Layer = stem,
            Region = "",
            GeometryPath = _delivery.PathOf("", stem + ".shp"),
            AttributeTablePath = withTable ? _delivery.PathOf("", stem + ".dbf") : null,
            CrsWkt = ShapefileDeliveryBuilder.Sad69Utm24SWkt,
            Encoding = encoding,
            Verdict = LayerVerdict.Usable,
            FieldAliases = aliases ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

    private static async Task<List<RawRecord>> ReadAllAsync(LayerFileSet layer, CancellationToken cancellationToken = default)
    {
        var records = new List<RawRecord>();
        await foreach (var record in new ShapefileFeatureSource().ReadAsync(layer, cancellationToken))
        {
            records.Add(record);
        }

        return records;
    }

    [Fact]
    public async Task Read_StreamsEveryRecordWithOneBasedRecordNumbersAndGeometry()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(4));

        var records = await ReadAllAsync(LayerOf("POSTE"));

        records.Select(r => r.RecordNumber).Should().Equal(1, 2, 3, 4);
        var first = (Point)records[0].Geometry;
        first.X.Should().BeApproximately(554272.85, 0.001);
        first.Y.Should().BeApproximately(8566173.49, 0.001);
        records.Should().OnlyContain(r => string.Equals(r.Layer.Layer, "POSTE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_WithoutAliases_KeysAttributesByTheDbfFieldName()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(2));

        var records = await ReadAllAsync(LayerOf("POSTE"));

        records[1].Attributes.Keys.Should().BeEquivalentTo(["NU_PG_ID", "CD_TIPO_PG", "DE_NOME"]);
    }

    [Fact]
    public async Task Read_WithAliases_KeysAttributesByTheAliasAndKeepsUnmappedNames()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(1));
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["NU_PG_ID"] = "NU_PG_IDENTIFICADOR" };

        var records = await ReadAllAsync(LayerOf("POSTE", aliases: aliases));

        records[0].Attributes.Keys.Should().BeEquivalentTo(["NU_PG_IDENTIFICADOR", "CD_TIPO_PG", "DE_NOME"]);
    }

    [Fact]
    public async Task Read_ConvertsValuesToLongDoubleDateTimeAndTrimmedStrings()
    {
        var features = new[]
        {
            ShapefileDeliveryBuilder.PointFeature(
                554000, 8566000,
                ("N_INT", 42L), ("N_REAL", 12.5), ("D_DATE", new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Unspecified)), ("S_TEXT", "  padded  "), ("S_BLANK", "   ")),
        };
        _delivery.WriteLayer("", "TYPES", features);

        var attributes = (await ReadAllAsync(LayerOf("TYPES")))[0].Attributes;

        attributes["N_INT"].Should().BeOfType<long>().Which.Should().Be(42);
        attributes["N_REAL"].Should().BeOfType<double>().Which.Should().Be(12.5);
        attributes["D_DATE"].Should().BeOfType<DateTime>().Which.Should().Be(new DateTime(2024, 3, 15));
        attributes["S_TEXT"].Should().Be("padded");
        attributes["S_BLANK"].Should().BeNull();
    }

    [Fact]
    public async Task Read_DecodesTextWithTheLayersEncoding_Utf8()
    {
        _delivery.WriteLayer("", "TXT", [ShapefileDeliveryBuilder.PointFeature(554000, 8566000, ("DE_NOME", "São João da Mata"))], cpg: "UTF-8");

        var records = await ReadAllAsync(LayerOf("TXT", "UTF-8"));

        records[0].Attributes["DE_NOME"].Should().Be("São João da Mata");
    }

    [Fact]
    public async Task Read_DecodesTextWithTheLayersEncoding_Latin1()
    {
        _delivery.WriteLayer("", "TXT", [ShapefileDeliveryBuilder.PointFeature(554000, 8566000, ("DE_NOME", "São João da Mata"))], cpg: "ISO-8859-1");

        var records = await ReadAllAsync(LayerOf("TXT", Encoding.Latin1.WebName));

        records[0].Attributes["DE_NOME"].Should().Be("São João da Mata");
    }

    [Fact]
    public async Task Read_WithoutAnAttributeTable_YieldsGeometryOnlyRecords()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(3), dbf: false);

        var records = await ReadAllAsync(LayerOf("POSTE", withTable: false));

        records.Should().HaveCount(3);
        records.Should().OnlyContain(r => r.Attributes.Count == 0);
        records.Select(r => r.RecordNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Read_LineGeometries_KeepTheirVertices()
    {
        _delivery.WriteLayer("", "REDE_MT", ShapefileTestFeatures.Lines(2));

        var records = await ReadAllAsync(LayerOf("REDE_MT"));

        var line = records[0].Geometry.Should().BeAssignableTo<LineString>().Subject;
        line.NumPoints.Should().Be(2);
        line.StartPoint.X.Should().BeApproximately(554272.0, 0.001);
        line.EndPoint.Y.Should().BeApproximately(8566182.0, 0.001);
    }

    [Fact]
    public async Task Read_IsLazy_StoppingEarlyNeverFailsOnLaterRecords()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(50));
        var taken = new List<long>();

        await foreach (var record in new ShapefileFeatureSource().ReadAsync(LayerOf("POSTE"), CancellationToken.None))
        {
            taken.Add(record.RecordNumber);
            if (taken.Count == 2)
            {
                break;
            }
        }

        taken.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Read_Cancelled_Throws()
    {
        _delivery.WriteLayer("", "POSTE", ShapefileTestFeatures.Poles(5));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => ReadAllAsync(LayerOf("POSTE"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Read_AfterInventory_UsesTheInventorysAliasesAndEncoding()
    {
        _delivery.WriteLayer("", "REDE_MT", ShapefileTestFeatures.Lines(2),
            lineageFieldMap: "NU_TRECHO_MT_ID \"Trecho\" true true false 10 Long 0 10 ,First,#,src,NU_TRECHO_MT_ID,-1,-1;NU_PG_INICIAL_ID \"PG\" true true false 10 Long 0 10 ,First,#,src,NU_PG_INICIAL_ID,-1,-1");
        var inventory = await new ShapefileDeliveryInventory().InspectAsync(_delivery.Root, ShapefileTestProfile.Create(), CancellationToken.None);

        var records = await ReadAllAsync(inventory.Layers.Single());

        records[0].Attributes["NU_TRECHO_MT_ID"].Should().Be(1L);
        records[1].Attributes["NU_PG_INICIAL_ID"].Should().Be(2L);
    }
}
