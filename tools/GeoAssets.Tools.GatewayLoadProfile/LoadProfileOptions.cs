using System.Globalization;

namespace GeoAssets.Tools.GatewayLoadProfile;

/// <summary>The tool's command line (XD01-393): the command, the model's parameters and the replay's settings.</summary>
internal sealed record LoadProfileOptions(
    string Command,
    LoadProfileModel Model,
    Uri? BaseUrl,
    string Scenario,
    TimeSpan Duration,
    int Concurrency,
    int UploadConcurrency,
    Uri UploadPath,
    Uri UploadExtraPath,
    int UploadBytes,
    string? BearerToken)
{
    /// <summary>The environment variable a bearer token is read from (never a command-line argument, so it stays out of shell history).</summary>
    public const string TokenVariable = "GATEWAY_LOAD_PROFILE_TOKEN";

    public const string Usage = """
        GeoAssets.Tools.GatewayLoadProfile — the gateway rate-limit load profile (XD01-393, docs/perf/gateway-rate-limits.md)

          model   [model options]                         print the request-count model and the recommended buckets
          replay  --base-url <gateway> [options]          replay the profile against a gateway and report req/s per class

        Model options (defaults = the documented model):
          --viewport <w>x<h> (1580x1020)  --tile-size 256  --wms-layers 2  --actions-per-minute 30  --zoom-share 0.5
          --pan-reveal 0.5  --zoom-burst-steps 5  --vector-entries 2  --photos 100  --requests-per-photo 2  --upload-extra 20
        Replay options:
          --scenario map|upload|all (all)  --duration-seconds 60 (map session length)  --concurrency 6 (browser connections)
          --upload-concurrency 4  --upload-path <path> (/api/field-service/isp/pole-audit/load-profile/evidence)
          --upload-extra-path <path> (/api/identity/me)  --upload-bytes 200000
          A bearer token for the API classes is read from GATEWAY_LOAD_PROFILE_TOKEN (tiles are always sent anonymously).
        """;

    /// <summary>The options in <paramref name="args"/>, or an error message.</summary>
    public static (LoadProfileOptions? Options, string? Error) Parse(IReadOnlyList<string> args, string? bearerToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0 || args[0] is not ("model" or "replay")) return (null, "The first argument must be 'model' or 'replay'.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
                return (null, $"'{args[i]}' needs a value.");
            if (!values.TryAdd(args[i], args[i + 1])) return (null, $"{args[i]} is given more than once.");
        }

        try
        {
            var defaults = new LoadProfileModel();
            var (width, height) = Viewport(values, defaults);
            var model = new LoadProfileModel(
                width,
                height,
                Int(values, "--tile-size", defaults.TileSizePx),
                Int(values, "--wms-layers", defaults.WmsLayers),
                Double(values, "--actions-per-minute", defaults.ActionsPerMinute),
                Double(values, "--zoom-share", defaults.ZoomShare),
                Double(values, "--pan-reveal", defaults.PanReveal),
                Int(values, "--zoom-burst-steps", defaults.ZoomBurstSteps),
                Int(values, "--vector-entries", defaults.VectorEntries),
                Int(values, "--photos", defaults.Photos),
                Int(values, "--requests-per-photo", defaults.RequestsPerPhoto),
                Int(values, "--upload-extra", defaults.UploadExtraRequests));

            Uri? baseUrl = null;
            if (values.Remove("--base-url", out var baseUrlValue) && !Uri.TryCreate(baseUrlValue, UriKind.Absolute, out baseUrl))
                return (null, $"--base-url '{baseUrlValue}' is not an absolute URL.");
            if (string.Equals(args[0], "replay", StringComparison.Ordinal) && baseUrl is null)
                return (null, "replay needs --base-url (the gateway's root, e.g. http://localhost:8090).");

            var scenario = values.Remove("--scenario", out var scenarioValue) ? scenarioValue : "all";
            if (scenario is not ("map" or "upload" or "all")) return (null, "--scenario must be map, upload or all.");

            var options = new LoadProfileOptions(
                args[0],
                model,
                baseUrl,
                scenario,
                TimeSpan.FromSeconds(Int(values, "--duration-seconds", 60)),
                Int(values, "--concurrency", 6),
                Int(values, "--upload-concurrency", 4),
                new Uri(values.Remove("--upload-path", out var uploadPath) ? uploadPath : "/api/field-service/isp/pole-audit/load-profile/evidence", UriKind.Relative),
                new Uri(values.Remove("--upload-extra-path", out var extraPath) ? extraPath : "/api/identity/me", UriKind.Relative),
                Int(values, "--upload-bytes", 200_000),
                string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim());

            return values.Count > 0 ? (null, $"Unknown argument '{values.Keys.First()}'.") : (options, null);
        }
        catch (FormatException exception)
        {
            return (null, exception.Message);
        }
    }

    private static (int Width, int Height) Viewport(Dictionary<string, string> values, LoadProfileModel defaults)
    {
        if (!values.Remove("--viewport", out var value)) return (defaults.ViewportWidthPx, defaults.ViewportHeightPx);
        var parts = value.Split('x', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) && height > 0
            ? (width, height)
            : throw new FormatException($"--viewport '{value}' must be <width>x<height> in pixels.");
    }

    private static int Int(Dictionary<string, string> values, string name, int fallback)
    {
        if (!values.Remove(name, out var value)) return fallback;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new FormatException($"{name} '{value}' must be a positive whole number.");
    }

    private static double Double(Dictionary<string, string> values, string name, double fallback)
    {
        if (!values.Remove(name, out var value)) return fallback;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new FormatException($"{name} '{value}' must be a positive number.");
    }
}
