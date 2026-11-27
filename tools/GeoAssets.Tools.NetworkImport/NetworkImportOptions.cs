namespace GeoAssets.Tools.NetworkImport;

/// <summary>The parsed command line (values not yet validated beyond presence).</summary>
internal sealed record NetworkImportOptions
{
    public const int DefaultSample = 50;

    public required NetworkImportVerb Verb { get; init; }
    public string? Root { get; init; }
    public string? Profile { get; init; }
    public string OutputDirectory { get; init; } = ".";
    public string? ScopePrefix { get; init; }
    public IReadOnlyList<string> Regions { get; init; } = [];
    public IReadOnlyList<string> Layers { get; init; } = [];
    public bool IncludeGeometryOnly { get; init; }
    public string? ProjDataDirectory { get; init; }
    public string? KernelUrl { get; init; }
    public string? Organization { get; init; }
    public string? TenantDomain { get; init; }
    public string? ImportId { get; init; }
    public bool ReplaceScope { get; init; }
    public string? CheckpointPath { get; init; }
    public string? FeaturesPath { get; init; }
    public int Sample { get; init; } = DefaultSample;
    public string? Export { get; init; }
    public string? MinZoom { get; init; }
    public string? MaxZoom { get; init; }
}
