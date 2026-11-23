namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>An organization whose assets the caller may show as a reference layer (XD01-331), with the name to show.</summary>
public sealed record ReferenceOrganization(Guid Id, string Name);
