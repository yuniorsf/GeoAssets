using System.Reflection;
using System.Text.RegularExpressions;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// Vertical-token ratchet for the generic workflow engine (XD01-353, ADR-001 invariant I2 / smells K and O): identifiers
/// in the engine namespaces of <c>GeoAssets.Workflow</c> and in <c>GeoAssets.Workflow.EFCore</c> must not name a vertical
/// domain, and the engine assemblies must not reference <c>GeoAssets.Domain.*</c>. Tokens match whole PascalCase words,
/// so <c>Dispatch</c> or <c>Display</c> never count as <c>Isp</c>. Both allowlists may only shrink.
/// </summary>
internal static partial class VerticalTokenRatchet
{
    private const string DomainAssemblyPrefix = "GeoAssets.Domain";

    private const BindingFlags DeclaredMembers =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static IReadOnlyList<string> VerticalTokens { get; } =
    [
        "Pole",
        "Occupancy",
        "Isp",
        "NeutralOperator",
        "Paving",
        "Manhole",
        "Contractor",
        "Municipio",
        "Prefeitura",
        "Ftth",
    ];

    public static IReadOnlyList<string> GenericEngineNamespaces { get; } =
    [
        "GeoAssets.Workflow.Orders",
        "GeoAssets.Workflow.Rules",
        "GeoAssets.Workflow.Targets",
        "GeoAssets.Workflow.Selection",
        "GeoAssets.Workflow.Notifications",
    ];

    /// <summary>
    /// Today's offenders, keyed by top-level type full name, each annotated with the epic that removes it.
    /// Remove an entry in the same change that eliminates its violation. Never add one.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Allowlist { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GeoAssets.Workflow.Persistence.Configurations.ContractorUsLedgerEntryRecordConfiguration"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.Configurations.ContractorUsPostingRecordConfiguration"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.Configurations.ManholeAuditRecordConfiguration"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.Configurations.PavingMeasurementRecordConfiguration"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.ContractorUsLedgerMapper"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.ContractorUsPostingMapper"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.EFContractorUsLedgerRepository"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.EFContractorUsPostingRepository"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.EFManholeAuditRepository"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.EFPavingMeasurementRepository"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.Entities.ContractorUsLedgerEntryRecord"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.Entities.ContractorUsPostingRecord"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.Entities.ManholeAuditRecord"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.Entities.PavingMeasurementRecord"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.InspectionCommitResult"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.InspectionCommitService"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.ManholeAuditMapper"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.PavingMeasurementMapper"] = "XD01-350 (P6) — XD01-381 removes the FieldService tables (paving, manhole audits, contractor US) from the kernel context",
            ["GeoAssets.Workflow.Persistence.ServiceOrderDbContext"] = "XD01-350 (P6) — XD01-381 moves the remaining vertical DbSets (paving, manhole audits, contractor US) out of the kernel context",
        };

    /// <summary>Engine assembly → <c>GeoAssets.Domain.*</c> references that exist today, each with its removing epic.</summary>
    public static IReadOnlyDictionary<string, string> DomainReferenceAllowlist { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GeoAssets.Workflow.EFCore -> GeoAssets.Domain.Sanitation"] = "XD01-350 (P6) — XD01-381 moves InspectionCommitService's paving spawn (TrenchDimensions) out of the kernel persistence",
        };

    private static readonly string[][] TokenWords =
        VerticalTokens.Select(token => SplitWords(token).ToArray()).ToArray();

    public static bool IsGenericEngineNamespace(string? ns) =>
        ns is not null
        && GenericEngineNamespaces.Any(engine =>
            string.Equals(ns, engine, StringComparison.Ordinal)
            || ns.StartsWith(engine + ".", StringComparison.Ordinal));

    public static IReadOnlyList<string> FindTokens(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var words = SplitWords(identifier).ToArray();
        var found = new List<string>();
        for (var t = 0; t < VerticalTokens.Count; t++)
        {
            if (ContainsWordSequence(words, TokenWords[t]))
            {
                found.Add(VerticalTokens[t]);
            }
        }

        return found;
    }

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> FindViolations(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var byOwner = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var type in types.Where(t => !IsCompilerGenerated(t.Name)))
        {
            var hits = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (name, display) in IdentifiersOf(type))
            {
                foreach (var token in FindTokens(name))
                {
                    hits.Add($"{display} ~ {token}");
                }
            }

            if (hits.Count == 0)
            {
                continue;
            }

            var owner = KernelReferenceScanner.OwnerOf(type);
            if (!byOwner.TryGetValue(owner, out var set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                byOwner[owner] = set;
            }

            set.UnionWith(hits);
        }

        return byOwner.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value, StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> FindDomainAssemblyReferences(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var references = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            var name = assembly.GetName().Name;
            foreach (var referenced in assembly.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty))
            {
                if (string.Equals(referenced, DomainAssemblyPrefix, StringComparison.Ordinal)
                    || referenced.StartsWith(DomainAssemblyPrefix + ".", StringComparison.Ordinal))
                {
                    references[$"{name} -> {referenced}"] = new HashSet<string>(StringComparer.Ordinal) { referenced };
                }
            }
        }

        return references;
    }

    private static IEnumerable<(string Name, string Display)> IdentifiersOf(Type type)
    {
        yield return (type.Name, type.Name);

        foreach (var member in type.GetMembers(DeclaredMembers).Where(m => !IsCompilerGenerated(m.Name)))
        {
            yield return (member.Name, $"{type.Name}.{member.Name}");

            if (member is MethodBase method)
            {
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.Name is { } parameterName)
                    {
                        yield return (parameterName, $"{type.Name}.{member.Name}({parameterName})");
                    }
                }
            }
        }
    }

    private static bool ContainsWordSequence(string[] words, string[] token)
    {
        for (var start = 0; start + token.Length <= words.Length; start++)
        {
            var matched = true;
            for (var i = 0; i < token.Length && matched; i++)
            {
                var word = words[start + i];
                matched = i < token.Length - 1
                    ? string.Equals(word, token[i], StringComparison.OrdinalIgnoreCase)
                    : IsWordOrPlural(word, token[i]);
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordOrPlural(string word, string tokenWord) =>
        string.Equals(word, tokenWord, StringComparison.OrdinalIgnoreCase)
        || string.Equals(word, tokenWord + "s", StringComparison.OrdinalIgnoreCase)
        || string.Equals(word, tokenWord + "es", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitWords(string identifier) =>
        WordPattern().Matches(identifier).Select(m => m.Value);

    private static bool IsCompilerGenerated(string name) =>
        name.Contains('<', StringComparison.Ordinal) || name.Contains('$', StringComparison.Ordinal);

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z][a-z0-9]*|[a-z0-9]+", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordPattern();
}
