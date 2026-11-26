using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal static class ShapefileTestProfile
{
    public static readonly IReadOnlyDictionary<string, string> Fallback =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NU_PG_INIC"] = "NU_PG_INICIAL_ID",
            ["CD_TIPO_PG"] = "CD_TIPO_PG",
        };

    public static FakeSourceProfile Create() => new(
        [
            new LayerDefinition { Name = "POSTE", FileStem = "POSTE", Kind = ElementKind.Structure },
            new LayerDefinition { Name = "REDE_MT", FileStem = "REDE_MT", Kind = ElementKind.Segment },
        ],
        TestData.AssetClasses,
        new ScriptedLayerMapper(),
        Fallback);
}
