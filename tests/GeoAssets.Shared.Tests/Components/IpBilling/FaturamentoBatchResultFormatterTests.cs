using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;
using GeoAssets.Shared.Components.IpBilling;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.IpBilling;

/// <summary>
/// Exercises <see cref="FaturamentoBatchResultFormatter.Summarize"/> directly — the pure
/// recarga/batch-load outcome message <see cref="FaturamentoRecargaDialog"/> (XD01-190)
/// displays, following the same pure-logic-extraction fallback <c>OficioDetailTests</c>
/// (XD01-187) used in the absence of a bUnit-style component-rendering convention.
/// </summary>
public class FaturamentoBatchResultFormatterTests
{
    private static readonly Guid MunicipioId = Guid.NewGuid();

    [Fact]
    public void Summarize_NullResult_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FaturamentoBatchResultFormatter.Summarize(null!));
    }

    [Fact]
    public void Summarize_OneAppliedNoSkips_ReportsSingular()
    {
        var applied = new Faturamento { MunicipioId = MunicipioId, Competencia = "2026-03", KwhFaturado = 100m };
        var result = new FaturamentoBatchResult([applied], []);

        Assert.Equal("1 row applied.", FaturamentoBatchResultFormatter.Summarize(result));
    }

    [Fact]
    public void Summarize_MultipleAppliedNoSkips_ReportsPlural()
    {
        var applied1 = new Faturamento { MunicipioId = MunicipioId, Competencia = "2026-03", KwhFaturado = 100m };
        var applied2 = new Faturamento { MunicipioId = MunicipioId, Competencia = "2026-04", KwhFaturado = 200m };
        var result = new FaturamentoBatchResult([applied1, applied2], []);

        Assert.Equal("2 rows applied.", FaturamentoBatchResultFormatter.Summarize(result));
    }

    [Fact]
    public void Summarize_AllSkipped_ReportsNoneAppliedWithReasons()
    {
        var skip = new FaturamentoBatchSkip(MunicipioId, "2026-03", "Faturamento inexistente");
        var result = new FaturamentoBatchResult([], [skip]);

        var message = FaturamentoBatchResultFormatter.Summarize(result);

        Assert.Contains("No rows applied.", message, StringComparison.Ordinal);
        Assert.Contains("Faturamento inexistente", message, StringComparison.Ordinal);
        Assert.Contains("2026-03", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Summarize_MixedAppliedAndSkipped_ReportsBoth()
    {
        var applied = new Faturamento { MunicipioId = MunicipioId, Competencia = "2026-03", KwhFaturado = 100m };
        var skip = new FaturamentoBatchSkip(MunicipioId, "2026-04", "Faturamento já cadastrado");
        var result = new FaturamentoBatchResult([applied], [skip]);

        var message = FaturamentoBatchResultFormatter.Summarize(result);

        Assert.Contains("1 row(s) applied.", message, StringComparison.Ordinal);
        Assert.Contains("Faturamento já cadastrado", message, StringComparison.Ordinal);
    }
}
