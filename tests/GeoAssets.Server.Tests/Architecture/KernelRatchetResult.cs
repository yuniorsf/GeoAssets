namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// Outcome of <see cref="KernelReferenceRatchet.Evaluate"/>: owners violating outside the allowlist, and allowlist
/// entries that no longer violate (the ratchet forces their removal).
/// </summary>
internal sealed record KernelRatchetResult(IReadOnlyList<string> NewOffenders, IReadOnlyList<string> StaleAllowlistEntries)
{
    public bool IsClean => NewOffenders.Count == 0 && StaleAllowlistEntries.Count == 0;
}
