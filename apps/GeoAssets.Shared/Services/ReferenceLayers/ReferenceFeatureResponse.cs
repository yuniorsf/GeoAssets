namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>
/// One answer of <see cref="IReferenceFeatureClient.GetAsync"/>: the features as the server sent them, or the server's
/// refusal (<c>403</c>) — the one failure that means "this organization is no longer open to you", not "try again".
/// </summary>
/// <param name="IsForbidden">The server refused the caller.</param>
/// <param name="FeaturesJson">The response body, a JSON array of reference features (<c>[]</c> when forbidden).</param>
public sealed record ReferenceFeatureResponse(bool IsForbidden, string FeaturesJson)
{
    /// <summary>The server refused the caller.</summary>
    public static ReferenceFeatureResponse Forbidden { get; } = new(true, "[]");

    /// <summary>The server answered <paramref name="featuresJson"/>.</summary>
    public static ReferenceFeatureResponse Granted(string featuresJson)
    {
        ArgumentNullException.ThrowIfNull(featuresJson);
        return new(false, featuresJson);
    }
}
