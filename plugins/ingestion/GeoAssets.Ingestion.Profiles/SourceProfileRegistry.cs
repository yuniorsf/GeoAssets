namespace GeoAssets.Ingestion.Profiles;

/// <summary>A fixed set of profiles, found by id (case-insensitive).</summary>
internal sealed class SourceProfileRegistry : ISourceProfileRegistry
{
    private readonly Dictionary<string, ISourceProfile> _profiles;

    public SourceProfileRegistry(IEnumerable<ISourceProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);
        ProfileIds = [.. _profiles.Keys.Order(StringComparer.Ordinal)];
    }

    public IReadOnlyList<string> ProfileIds { get; }

    public ISourceProfile Find(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return _profiles.TryGetValue(profileId, out var profile)
            ? profile
            : throw new KeyNotFoundException($"Unknown source profile '{profileId}' (known: {string.Join(", ", ProfileIds)}).");
    }
}
