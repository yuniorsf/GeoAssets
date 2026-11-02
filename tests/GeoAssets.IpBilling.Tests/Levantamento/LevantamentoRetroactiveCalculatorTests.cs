using FluentAssertions;
using GeoAssets.IpBilling.Levantamento;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Levantamento;

/// <summary>
/// Covers all six reconstructed <see cref="TipoRetroacao"/> codes (00/01/10/11/20/30), the
/// 730-day retroactive cap boundary, and the [FIX] proof that codes 01/11 actually change
/// output vs. the naive 00/10 math — see <see cref="TipoRetroacao"/>'s own doc comment for the
/// reconstruction caveat this decision tree is built against.
/// </summary>
public class LevantamentoRetroactiveCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 2);
    private const decimal DailyRate = 2m; // 2 kWh/day, chosen so day counts multiply out cleanly.

    private static TransformerHistoryContext Context(
        bool municipioHasAny = true,
        bool seenInMostRecent = false,
        bool everSeen = true,
        DateOnly? mostRecentStart = null,
        DateOnly? firstStart = null,
        DateOnly? fiscalizacaoAnchor = null,
        decimal alreadyBilled = 0m) => new(
        municipioHasAny, seenInMostRecent, everSeen, mostRecentStart, firstStart, fiscalizacaoAnchor, alreadyBilled);

    [Fact]
    public void Code30_MunicipioHasNoValidatedLevantamentoEver_NewButZeroRetroativo()
    {
        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(municipioHasAny: false), AsOf, DailyRate);

        outcome.IsNewTransformer.Should().BeTrue();
        outcome.Tipo.Should().Be(TipoRetroacao.PrimeiraLevantamentoDoMunicipio30);
        outcome.RetroativoKwh.Should().Be(0m);
    }

    [Fact]
    public void ExistingTransformer_SeenInMostRecentValidated_NotNewNoCode()
    {
        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(seenInMostRecent: true), AsOf, DailyRate);

        outcome.IsNewTransformer.Should().BeFalse();
        outcome.Tipo.Should().BeNull();
        outcome.RetroativoKwh.Should().BeNull();
    }

    [Fact]
    public void Code20_NeverSeenInAnyValidatedLevantamento_AnchorsAtFirstValidated()
    {
        var firstStart = AsOf.AddDays(-100);
        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(everSeen: false, firstStart: firstStart), AsOf, DailyRate);

        outcome.IsNewTransformer.Should().BeTrue();
        outcome.Tipo.Should().Be(TipoRetroacao.HistoricoCompleto20);
        outcome.RetroativoKwh.Should().Be(DailyRate * 100);
    }

    [Fact]
    public void Code00_ReappearedNoIntervening_AnchorsAtMostRecentValidatedLevantamento()
    {
        var mostRecentStart = AsOf.AddDays(-30);
        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: mostRecentStart), AsOf, DailyRate);

        outcome.IsNewTransformer.Should().BeTrue();
        outcome.Tipo.Should().Be(TipoRetroacao.Naive00);
        outcome.RetroativoKwh.Should().Be(DailyRate * 30);
    }

    [Fact]
    public void Code01_ReappearedAnchoredAtLevantamentoWithExclusion_ExcludesAlreadyBilledKwh()
    {
        // [FIX] proof, part 1: same anchor as Code00 (no Fiscalização shifts the anchor), but
        // some kWh within that window was already billed and must be excluded.
        var mostRecentStart = AsOf.AddDays(-30);

        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: mostRecentStart, alreadyBilled: 10m), AsOf, DailyRate);

        outcome.Tipo.Should().Be(TipoRetroacao.FixExclusao01);
        outcome.RetroativoKwh.Should().Be((DailyRate * 30) - 10m);
    }

    [Fact]
    public void Fix01Vs11_ActuallyChangesOutputVsNaive00And10Math()
    {
        // The literal AC: a test proving the 01/11 fix changes output vs. naive 00/10 math —
        // not just that it compiles. Same window/rate, only "was anything found to exclude"
        // differs between the naive and fixed runs.
        var anchor = AsOf.AddDays(-50);

        var naive = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: anchor), AsOf, DailyRate);
        var fixedOutcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: anchor, alreadyBilled: 40m), AsOf, DailyRate);

        naive.Tipo.Should().Be(TipoRetroacao.Naive00);
        fixedOutcome.Tipo.Should().Be(TipoRetroacao.FixExclusao01);
        naive.RetroativoKwh.Should().Be(DailyRate * 50);
        fixedOutcome.RetroativoKwh.Should().Be((DailyRate * 50) - 40m);
        fixedOutcome.RetroativoKwh.Should().NotBe(naive.RetroativoKwh);
    }

    [Fact]
    public void Code10_ReappearedAnchoredAtFiscalizacaoNoExclusionNeeded_NaiveFullAmount()
    {
        var fiscDate = AsOf.AddDays(-20);

        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: AsOf.AddDays(-200), fiscalizacaoAnchor: fiscDate), AsOf, DailyRate);

        outcome.Tipo.Should().Be(TipoRetroacao.FiscalizacaoNaive10);
        outcome.RetroativoKwh.Should().Be(DailyRate * 20);
    }

    [Fact]
    public void Code11_ReappearedAnchoredAtFiscalizacaoWithExclusion_SubtractsAlreadyBilled()
    {
        var fiscDate = AsOf.AddDays(-20);

        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: AsOf.AddDays(-200), fiscalizacaoAnchor: fiscDate, alreadyBilled: 15m), AsOf, DailyRate);

        outcome.Tipo.Should().Be(TipoRetroacao.FiscalizacaoFixExclusao11);
        outcome.RetroativoKwh.Should().Be((DailyRate * 20) - 15m);
    }

    [Fact]
    public void RetroactiveCap_ExactlyAt730Days_StillWithinCap()
    {
        var anchor = AsOf.AddDays(-730);
        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: anchor), AsOf, DailyRate);

        outcome.RetroativoKwh.Should().Be(DailyRate * 730);
    }

    [Fact]
    public void RetroactiveCap_OneDayPast730_ClampedToCap()
    {
        var anchor = AsOf.AddDays(-731);
        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: anchor), AsOf, DailyRate);

        // Clamped to exactly 730 days' worth, not 731 — the window start moves forward to the
        // cap boundary rather than extending past it.
        outcome.RetroativoKwh.Should().Be(DailyRate * 730);
    }

    [Fact]
    public void ExcludedKwhExceedingRawTotal_FlooredAtZero_NeverNegative()
    {
        var anchor = AsOf.AddDays(-5);

        var outcome = LevantamentoRetroactiveCalculator.DetermineOutcome(
            Context(mostRecentStart: AsOf.AddDays(-500), fiscalizacaoAnchor: anchor, alreadyBilled: 1_000_000m),
            AsOf, DailyRate);

        outcome.RetroativoKwh.Should().Be(0m);
    }
}
