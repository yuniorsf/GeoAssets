using System.Text.RegularExpressions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>Validates option values; an unusable value throws <see cref="NetworkImportInputException"/>.</summary>
internal static partial class NetworkImportInputs
{
    public static ISourceProfile Profile(string? profileId)
    {
        var registry = BuiltInProfiles.CreateRegistry();
        try
        {
            return registry.Find(profileId ?? string.Empty);
        }
        catch (KeyNotFoundException exception)
        {
            throw new NetworkImportInputException($"Unknown profile '{profileId}' (known: {string.Join(", ", registry.ProfileIds)}).", exception);
        }
    }

    public static string Root(string? root) =>
        !string.IsNullOrWhiteSpace(root) && Directory.Exists(root)
            ? root
            : throw new NetworkImportInputException($"The delivery folder '{root}' does not exist.");

    public static Guid Organization(string? organization) =>
        Guid.TryParse(organization, out var id) && id != Guid.Empty
            ? id
            : throw new NetworkImportInputException($"--organization must be a non-empty organization id (GUID), not '{organization}'.");

    public static Uri KernelUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
            throw new NetworkImportInputException($"--kernel-url must be an absolute http(s) URL, not '{url}'.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    public static string ImportId(string importId) =>
        ImportIdPattern().IsMatch(importId)
            ? importId
            : throw new NetworkImportInputException(
                $"--import-id must be 1 to {FeatureImportLimits.MaxImportIdLength} letters, digits, '.', '_' or '-', not '{importId}'.");

    public static string? ProjDataDirectory(string? directory) =>
        directory is null || Directory.Exists(directory)
            ? directory
            : throw new NetworkImportInputException($"The PROJ data folder '{directory}' does not exist.");

    public static string FeaturesFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? path
            : throw new NetworkImportInputException($"The features file '{path}' does not exist.");

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ImportIdPattern();
}
