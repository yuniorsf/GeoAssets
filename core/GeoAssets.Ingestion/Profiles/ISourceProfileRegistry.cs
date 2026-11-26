namespace GeoAssets.Ingestion.Profiles;

/// <summary>The profiles available to a host, by <see cref="ISourceProfile.Id"/>.</summary>
public interface ISourceProfileRegistry
{
    IReadOnlyList<string> ProfileIds { get; }

    /// <exception cref="KeyNotFoundException">No profile has this id.</exception>
    ISourceProfile Find(string profileId);
}
