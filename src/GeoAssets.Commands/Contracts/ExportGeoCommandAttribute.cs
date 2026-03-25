using System.ComponentModel.Composition;

namespace GeoAssets.Commands.Contracts;

/// <summary>
/// Combined [Export] + [ExportMetadata] attribute.
/// Apply to any class that implements <see cref="IGeoCommandHandler"/>
/// to make it discoverable by MEF.
/// </summary>
[MetadataAttribute]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ExportGeoCommandAttribute(string name)
    : ExportAttribute(typeof(IGeoCommandHandler)), IGeoCommandMetadata
{
    public string Name        { get; } = name;
    public string Category    { get; init; } = "General";
    public string Description { get; init; } = string.Empty;
}
