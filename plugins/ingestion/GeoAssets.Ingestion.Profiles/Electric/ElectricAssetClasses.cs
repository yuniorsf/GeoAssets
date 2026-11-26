using GeoAssets.Core.Models.Geometry;
using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Profiles.Electric;

/// <summary>The canonical electric-network asset classes the built-in distributor profiles share.</summary>
internal static class ElectricAssetClasses
{
    public const string Pole = "Pole";
    public const string GeographicPoint = "GeographicPoint";
    public const string Tower = "Tower";
    public const string Transformer = "Transformer";
    public const string Breaker = "Breaker";
    public const string Switch = "Switch";
    public const string Recloser = "Recloser";
    public const string CapacitorBank = "CapacitorBank";
    public const string VoltageRegulator = "VoltageRegulator";
    public const string Cubicle = "Cubicle";
    public const string MvDevice = "MvDevice";
    public const string MvSegment = "MvSegment";
    public const string MvJumper = "MvJumper";
    public const string LvSegment = "LvSegment";
    public const string HvSegment = "HvSegment";

    public static IReadOnlyList<AssetClassDefinition> All { get; } =
    [
        Define(Pole, "electric:pole", "5b3d5dde-182f-4725-bd56-e35846c77324", "Poste", GeometryType.Point, "#8d6e63", "occupiable-support"),
        Define(GeographicPoint, "electric:geographic-point", "7e0d0562-11c5-4b20-be50-364b6b894113", "Ponto geográfico", GeometryType.Point, "#9e9e9e"),
        Define(Tower, "electric:tower", "2d369700-1081-4568-8ea5-88cdd78ece31", "Torre", GeometryType.Point, "#6d4c41"),
        Define(Transformer, "electric:transformer", "81f4acdf-6d53-490d-b894-a9b41a71d18c", "Transformador", GeometryType.Point, "#f9a825"),
        Define(Breaker, "electric:breaker", "61628e59-1dc2-4015-962d-f8350111aa12", "Disjuntor", GeometryType.Point, "#c62828"),
        Define(Switch, "electric:switch", "78c9f88d-2e9a-4b8b-b1a5-8840f5bcceac", "Chave", GeometryType.Point, "#1565c0"),
        Define(Recloser, "electric:recloser", "8a6b8277-d0f2-41a5-8d4f-749d987acd35", "Religador", GeometryType.Point, "#6a1b9a"),
        Define(CapacitorBank, "electric:capacitor-bank", "b4707f77-5d43-43af-8601-986561d9e769", "Banco de capacitores", GeometryType.Point, "#00838f"),
        Define(VoltageRegulator, "electric:voltage-regulator", "83105f64-01b7-41de-9082-7a38069a245f", "Regulador de tensão", GeometryType.Point, "#ef6c00"),
        Define(Cubicle, "electric:cubicle", "9addb5f2-bf26-47e0-ac28-659548fe78b1", "Cubículo", GeometryType.Point, "#4e342e"),
        Define(MvDevice, "electric:mv-device", "fd2237c0-be80-4a78-ae8f-6c157772c9d6", "Equipamento MT", GeometryType.Point, "#546e7a"),
        Define(MvSegment, "electric:mv-segment", "fd551c47-7a38-40e0-af03-63c9e93a5942", "Trecho MT", GeometryType.LineString, "#d32f2f"),
        Define(MvJumper, "electric:mv-jumper", "7a3e2e66-b959-41d1-86ad-3a1b26272152", "Jumper MT", GeometryType.LineString, "#e57373"),
        Define(LvSegment, "electric:lv-segment", "ba0efdf2-573c-4771-9868-12d3f93bf649", "Trecho BT", GeometryType.LineString, "#2e7d32"),
        Define(HvSegment, "electric:hv-segment", "70f64a14-97c1-41c1-b454-335290e2b790", "Trecho LT", GeometryType.LineString, "#4a148c"),
    ];

    private static AssetClassDefinition Define(
        string name, string role, string typeId, string displayName, GeometryType geometry, string color, params string[] reuseRoles) =>
        new(name, role, Guid.Parse(typeId), displayName, geometry, color, reuseRoles);
}
