namespace GeoAssets.Core.FeatureImport;

/// <summary>Limits shared by the kernel endpoint and its callers.</summary>
public static class FeatureImportLimits
{
    /// <summary>Most features accepted in one chunk.</summary>
    public const int MaxChunkSize = 5000;

    /// <summary>Longest accepted import id; ids match <c>^[A-Za-z0-9._-]{1,64}$</c>.</summary>
    public const int MaxImportIdLength = 64;

    /// <summary>Longest accepted scope.</summary>
    public const int ScopeMaxLength = 256;

    /// <summary>Longest accepted feature id (the <c>geo_entity.Id</c> column is <c>varchar(36)</c>).</summary>
    public const int MaxFeatureIdLength = 36;
}
