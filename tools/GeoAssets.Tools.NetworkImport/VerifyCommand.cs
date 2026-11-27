using System.Globalization;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// <c>verify</c>: reads the first <see cref="NetworkImportOptions.Sample"/> features of a plan's NDJSON and checks
/// each exists in the kernel, as the organization sees it, with the asset type import gave it — the plan's class type, or
/// the kernel's existing type import reused for it (<see cref="AssetTypeResolution"/>). Mismatches are printed and the
/// run exits with <see cref="NetworkImportExitCodes.KernelFailure"/>.
/// </summary>
internal sealed class VerifyCommand(NetworkImportOptions options, NetworkImportDependencies dependencies, TextWriter stdout)
{
    public async Task<int> RunAsync(IAccessTokenSource tokens, CancellationToken cancellationToken)
    {
        var organizationId = NetworkImportInputs.Organization(options.Organization);
        var kernelUrl = NetworkImportInputs.KernelUrl(options.KernelUrl);
        var expected = await ReadSampleAsync(NetworkImportInputs.FeaturesFile(options.FeaturesPath), cancellationToken).ConfigureAwait(false);

        using var http = new HttpClient(dependencies.Handler, disposeHandler: false) { BaseAddress = kernelUrl };
        var client = new KernelClient(http, tokens, dependencies.TimeProvider, options.TenantDomain);
        var kernelTypeOf = AssetTypeResolution.PlannedToKernel(await client.GetAssetTypesAsync(cancellationToken).ConfigureAwait(false));
        var mismatches = new List<string>();
        foreach (var feature in expected)
        {
            var actual = await client.GetFeatureAsync(organizationId, feature.Id, cancellationToken).ConfigureAwait(false);
            var expectedType = kernelTypeOf(feature.Properties.AssetTypeId);
            if (actual is null)
                mismatches.Add($"{feature.Id}: missing");
            else if (!string.Equals(actual.Properties.AssetTypeId, expectedType, StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"{feature.Id}: asset type {actual.Properties.AssetTypeId}, expected {expectedType}");
        }

        foreach (var mismatch in mismatches)
            await stdout.LineAsync(mismatch, cancellationToken).ConfigureAwait(false);
        await stdout.LineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Verified {expected.Count - mismatches.Count} of {expected.Count} sampled features in organization {organizationId:D}."), cancellationToken).ConfigureAwait(false);
        return mismatches.Count == 0 ? NetworkImportExitCodes.Succeeded : NetworkImportExitCodes.KernelFailure;
    }

    private async Task<List<GeoFeature>> ReadSampleAsync(string path, CancellationToken cancellationToken)
    {
        var features = new List<GeoFeature>();
        using var reader = new StreamReader(path);
        var lineNumber = 0;
        while (features.Count < options.Sample && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                features.Add(JsonSerializer.Deserialize<GeoFeature>(line, GeoJsonSerializer.Options)
                    ?? throw new NetworkImportInputException(string.Create(CultureInfo.InvariantCulture, $"{path}:{lineNumber} is not a feature.")));
            }
            catch (JsonException exception)
            {
                throw new NetworkImportInputException(string.Create(CultureInfo.InvariantCulture, $"{path}:{lineNumber} is not valid GeoFeature JSON: {exception.Message}"), exception);
            }
        }

        return features;
    }
}
