using GeoAssets.IpBilling.FaturamentoLedger;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>
/// Pure view-model logic for summarizing a <see cref="FaturamentoBatchResult"/> (recarga/batch
/// load, XD01-189) into a human-readable outcome, extracted out of
/// <see cref="FaturamentoRecargaDialog"/> so it's directly unit-testable without a bUnit-style
/// component-rendering convention (this repo has none yet — same fallback pattern
/// <c>OficioDetail.IsLineSwapped</c> used in XD01-187).
/// </summary>
public static class FaturamentoBatchResultFormatter
{
    public static string Summarize(FaturamentoBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Skipped.Count == 0)
        {
            return result.Applied.Count == 1
                ? "1 row applied."
                : $"{result.Applied.Count} rows applied.";
        }

        var skipReasons = string.Join("; ", result.Skipped.Select(s => $"{s.MunicipioId}/{s.Competencia}: {s.Reason}"));
        return result.Applied.Count == 0
            ? $"No rows applied. Skipped: {skipReasons}"
            : $"{result.Applied.Count} row(s) applied. Skipped: {skipReasons}";
    }
}
