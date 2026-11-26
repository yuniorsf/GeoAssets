namespace GeoAssets.Tools.NetworkImport;

/// <summary>The last chunk the kernel acknowledged for an import (persisted as <c>{importId, lastSequence}</c>).</summary>
internal sealed record ImportCheckpoint(string ImportId, int LastSequence);
