using System.Globalization;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// Parses the tool's arguments (pure): the verb first, then its options. Every option but the flags takes a value and
/// may appear once. Values are checked only for presence here (and <c>--sample</c> for being a positive integer); the
/// verbs validate the rest (input errors).
/// </summary>
internal static class NetworkImportCommandLine
{
    public const string Usage =
        """
        Usage: GeoAssets.Tools.NetworkImport <verb> [options]

          inventory --root <dir> --profile <id> [--out <dir>]
              Checks a delivery folder; writes inventory.json and data-request.md.
          plan      --root <dir> --profile <id> --scope-prefix <prefix> [--regions a,b] [--layers x,y]
                    [--include-geometry-only] [--proj-data <dir>] [--out <dir>] [--export flatgeobuf|geoparquet]
              Runs the pipeline into features.ndjson; writes report.json. --export also writes the plan as
              features.fgb (FlatGeobuf) or features.parquet (GeoParquet) for a GIS tool.
          import    <plan options> --kernel-url <url> --organization <guid> [--tenant-domain <domain>]
                    [--import-id <id>] [--replace-scope] [--checkpoint <file>]
              Runs the pipeline into the kernel's bulk feature import; writes report.json.
          verify    --kernel-url <url> --organization <guid> --features <features.ndjson> [--sample <n>]
                    [--tenant-domain <domain>]
              Checks the first <n> (default 50) features of a plan exist in the kernel with the same asset type.

        Profiles: neoenergia-gse, equatorial-qgis-municipal.
        Authentication (import, verify): GEOASSETS_NETWORK_IMPORT_TOKEN (a bearer token), else the client credentials
        GEOASSETS_SERVICEAUTH_INSTANCE (the kernel's AzureAdCiam:Instance), GEOASSETS_SERVICEAUTH_TENANT_ID,
        GEOASSETS_SERVICEAUTH_CLIENT_ID, GEOASSETS_SERVICEAUTH_CLIENT_SECRET and GEOASSETS_SERVICEAUTH_SCOPE.
        Exit codes: 0 ok, 1 usage error, 2 input error, 3 kernel failure.
        """;

    private const string Root = "--root";
    private const string Profile = "--profile";
    private const string Out = "--out";
    private const string ScopePrefix = "--scope-prefix";
    private const string Regions = "--regions";
    private const string Layers = "--layers";
    private const string IncludeGeometryOnly = "--include-geometry-only";
    private const string ProjData = "--proj-data";
    private const string KernelUrl = "--kernel-url";
    private const string Organization = "--organization";
    private const string TenantDomain = "--tenant-domain";
    private const string ImportId = "--import-id";
    private const string ReplaceScope = "--replace-scope";
    private const string Checkpoint = "--checkpoint";
    private const string Features = "--features";
    private const string Sample = "--sample";
    private const string Export = "--export";

    private static readonly string[] InventoryValues = [Root, Profile, Out];
    private static readonly string[] PlanValues = [Root, Profile, Out, ScopePrefix, Regions, Layers, ProjData];
    private static readonly string[] PlanOnlyValues = [.. PlanValues, Export];
    private static readonly string[] ImportValues = [.. PlanValues, KernelUrl, Organization, TenantDomain, ImportId, Checkpoint];
    private static readonly string[] VerifyValues = [KernelUrl, Organization, Features, Sample, TenantDomain];

    /// <summary>True when the arguments ask for the usage text.</summary>
    public static bool IsHelp(IReadOnlyList<string> args) =>
        args.Count == 1 && args[0] is "--help" or "-h" or "help";

    public static (NetworkImportOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
            return (null, "A verb is required (inventory, plan, import or verify).");

        NetworkImportVerb verb;
        string[] valueOptions;
        string[] flags;
        string[] required;
        switch (args[0])
        {
            case "inventory":
                (verb, valueOptions, flags, required) = (NetworkImportVerb.Inventory, InventoryValues, [], [Root, Profile]);
                break;
            case "plan":
                (verb, valueOptions, flags, required) = (NetworkImportVerb.Plan, PlanOnlyValues, [IncludeGeometryOnly], [Root, Profile, ScopePrefix]);
                break;
            case "import":
                (verb, valueOptions, flags, required) = (NetworkImportVerb.Import, ImportValues, [IncludeGeometryOnly, ReplaceScope],
                    [Root, Profile, ScopePrefix, KernelUrl, Organization]);
                break;
            case "verify":
                (verb, valueOptions, flags, required) = (NetworkImportVerb.Verify, VerifyValues, [], [KernelUrl, Organization, Features]);
                break;
            default:
                return (null, $"Unknown verb '{args[0]}' (expected inventory, plan, import or verify).");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var setFlags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Count; i++)
        {
            var name = args[i];
            if (flags.Contains(name, StringComparer.Ordinal))
            {
                if (!setFlags.Add(name))
                    return (null, $"{name} is given more than once.");
                continue;
            }

            if (!valueOptions.Contains(name, StringComparer.Ordinal))
                return (null, $"Unknown option '{name}' for {args[0]}.");
            if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return (null, $"{name} needs a value.");
            if (!values.TryAdd(name, args[++i]))
                return (null, $"{name} is given more than once.");
        }

        if (required.FirstOrDefault(name => !values.ContainsKey(name)) is { } missing)
            return (null, $"{missing} is required for {args[0]}.");

        var sample = NetworkImportOptions.DefaultSample;
        if (values.TryGetValue(Sample, out var sampleText)
            && (!int.TryParse(sampleText, NumberStyles.None, CultureInfo.InvariantCulture, out sample) || sample < 1))
            return (null, $"{Sample} must be a positive integer.");

        return (new NetworkImportOptions
        {
            Verb = verb,
            Root = values.GetValueOrDefault(Root),
            Profile = values.GetValueOrDefault(Profile),
            OutputDirectory = values.GetValueOrDefault(Out) ?? ".",
            ScopePrefix = values.GetValueOrDefault(ScopePrefix),
            Regions = SplitList(values.GetValueOrDefault(Regions)),
            Layers = SplitList(values.GetValueOrDefault(Layers)),
            IncludeGeometryOnly = setFlags.Contains(IncludeGeometryOnly),
            ProjDataDirectory = values.GetValueOrDefault(ProjData),
            KernelUrl = values.GetValueOrDefault(KernelUrl),
            Organization = values.GetValueOrDefault(Organization),
            TenantDomain = values.GetValueOrDefault(TenantDomain),
            ImportId = values.GetValueOrDefault(ImportId),
            ReplaceScope = setFlags.Contains(ReplaceScope),
            CheckpointPath = values.GetValueOrDefault(Checkpoint),
            FeaturesPath = values.GetValueOrDefault(Features),
            Sample = sample,
            Export = values.GetValueOrDefault(Export),
        }, null);
    }

    private static string[] SplitList(string? value) =>
        value is null ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
