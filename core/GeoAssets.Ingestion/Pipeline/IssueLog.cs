using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>Counts a run's issues per code and keeps the first <see cref="MaxSamplesPerCode"/> of each code.</summary>
internal sealed class IssueLog
{
    public const int MaxSamplesPerCode = 20;

    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);
    private readonly List<ImportIssue> _samples = [];

    public IReadOnlyDictionary<string, long> Counts => _counts;

    public IReadOnlyList<ImportIssue> Samples => _samples;

    public void Add(ImportIssue issue)
    {
        var count = _counts.GetValueOrDefault(issue.Code) + 1;
        _counts[issue.Code] = count;
        if (count <= MaxSamplesPerCode)
        {
            _samples.Add(issue);
        }
    }

    public void Add(IssueSeverity severity, string code, string subject, string message) =>
        Add(new ImportIssue(severity, code, subject, message));

    public void AddRange(IEnumerable<ImportIssue> issues)
    {
        foreach (var issue in issues)
        {
            Add(issue);
        }
    }
}
