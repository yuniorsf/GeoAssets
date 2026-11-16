using FluentAssertions;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>
/// Covers every guard/branch of <see cref="InspectionLedgerPoster"/> (XD01-301, IDs POST-*),
/// running it over the real <c>FaturamentoLedgerService</c>.
/// </summary>
public class InspectionLedgerPosterTests
{
    private const string OrderId = "order-1";
    private const string Competencia = "2027-01"; // 31 days
    private static readonly Guid DistribuidoraId = Guid.NewGuid();
    private static readonly DateTimeOffset PostedAt = new(2027, 2, 3, 10, 0, 0, TimeSpan.Zero);

    private static Municipio NewMunicipio(Guid? distribuidoraId = null) => new()
    {
        IbgeCode = "3550308",
        Name = "Teste",
        Uf = "SP",
        DistribuidoraOrganizationId = Guid.NewGuid(),
        DistribuidoraId = distribuidoraId,
    };

    private static InspectionPointEntry Exact(int quantity, LampTypeCode code = LampTypeCode.VS, int wattageW = 100, bool retro = false) =>
        new(quantity, 1, false, retro, code, wattageW);

    private static InspectionPointEntry Lampless(int quantity) => new(quantity, 1, false, false);

    private static InspectionLedgerPostingRequest Request(
        Municipio municipio, IReadOnlyList<InspectionPointEntry> entries, string competencia = Competencia, string orderId = OrderId) =>
        new(orderId, municipio, competencia, entries, PostedAt);

    // 1 point × W × 11.8667 h × 31 d ÷ 1000, rounded to 4 dp.
    private static decimal JanuaryKwh(int quantity, decimal totalBilledW) =>
        IpEnergyMath.Round(quantity * totalBilledW * 11.8667m * 31 / 1000m);

    // ── POST-G1 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostAsync_POST_G1_NullRequest_Throws()
    {
        var poster = new InspectionPostingHarness().CreatePoster();

        await poster.Invoking(p => p.PostAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await poster.Invoking(p => p.FindExistingAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public Task PostAsync_POST_G1_BlankServiceOrderId_Throws(string orderId)
    {
        var poster = new InspectionPostingHarness().CreatePoster();

        return poster.Invoking(p => p.PostAsync(Request(NewMunicipio(), [], orderId: orderId)))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PostAsync_POST_G1_NullMunicipioOrEntries_Throws()
    {
        var poster = new InspectionPostingHarness().CreatePoster();

        await poster.Invoking(p => p.PostAsync(new InspectionLedgerPostingRequest(OrderId, null!, Competencia, [], PostedAt)))
            .Should().ThrowAsync<ArgumentNullException>();
        await poster.Invoking(p => p.PostAsync(new InspectionLedgerPostingRequest(OrderId, NewMunicipio(), Competencia, null!, PostedAt)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("2027-13")]
    [InlineData("2027-1")]
    [InlineData("01-2027")]
    [InlineData("")]
    public async Task PostAsync_POST_G1_CompetenciaNotYearMonth_Throws(string competencia)
    {
        var harness = new InspectionPostingHarness();
        var poster = harness.CreatePoster();

        await poster.Invoking(p => p.PostAsync(Request(NewMunicipio(), [], competencia)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*yyyy-MM*");
        harness.Faturamentos.PostCalls.Should().Be(0);
    }

    // ── POST-B1 / POST-B2 ─────────────────────────────────────────────────────

    [Fact]
    public async Task PostAsync_POST_B1_AlreadyPosted_ReturnsStoredPosting_WithoutReadingChartsOrWriting()
    {
        var harness = new InspectionPostingHarness();
        var municipio = NewMunicipio(DistribuidoraId);
        var poster = harness.CreatePoster();
        var first = await poster.PostAsync(Request(municipio, [Lampless(10)]));
        var (postCalls, saveCalls, meanCalls) = (harness.Faturamentos.PostCalls, harness.Faturamentos.SaveCalls, harness.DistribuidoraChart.MeanCalls);

        // A different entry list on the retry proves the stored figures are reused, not reconverted.
        var retry = await poster.PostAsync(Request(municipio, [Exact(3), Lampless(2)]));

        retry.WasAlreadyPosted.Should().BeTrue();
        retry.DeltaKwh.Should().Be(first.DeltaKwh);
        retry.IsProvisional.Should().BeTrue("the stored marker is echoed");
        retry.ExactPoints.Should().Be(3);
        retry.ProvisionalPoints.Should().Be(2);
        retry.Faturamento.KwhFaturado.Should().Be(first.DeltaKwh, "nothing was accumulated twice");
        harness.Faturamentos.PostCalls.Should().Be(postCalls, "no ledger call");
        harness.Faturamentos.SaveCalls.Should().Be(saveCalls, "no save");
        harness.DistribuidoraChart.MeanCalls.Should().Be(meanCalls, "no chart read");
        harness.Faturamentos.Postings.Should().ContainSingle();
    }

    [Fact]
    public async Task FindExistingAsync_POST_B2_NoPosting_ReturnsNull()
    {
        var poster = new InspectionPostingHarness().CreatePoster();

        (await poster.FindExistingAsync(Request(NewMunicipio(), []))).Should().BeNull();
    }

    [Fact]
    public async Task FindExistingAsync_POST_B2_PostingButLedgerRowMissing_ReturnsNull_AndPostAsyncPostsAgain()
    {
        var harness = new InspectionPostingHarness();
        var municipio = NewMunicipio();
        var poster = harness.CreatePoster();
        await poster.PostAsync(Request(municipio, [Exact(1)]));
        harness.Faturamentos.DropLedgerRow(municipio.Id, Competencia);

        (await poster.FindExistingAsync(Request(municipio, [Exact(1)]))).Should().BeNull();

        var reposted = await poster.PostAsync(Request(municipio, [Exact(1)]));
        reposted.WasAlreadyPosted.Should().BeFalse();
    }

    [Fact]
    public async Task FindExistingAsync_POST_B2_PostingForAnotherCompetencia_ReturnsNull()
    {
        var harness = new InspectionPostingHarness();
        var municipio = NewMunicipio();
        var poster = harness.CreatePoster();
        await poster.PostAsync(Request(municipio, [Exact(1)], "2026-12"));

        (await poster.FindExistingAsync(Request(municipio, [Exact(1)], Competencia))).Should().BeNull();
    }

    [Fact]
    public async Task FindExistingAsync_POST_B2_PostingInAnotherMode_ReturnsNull()
    {
        var harness = new InspectionPostingHarness();
        var municipio = NewMunicipio();
        await harness.Faturamentos.PostOrderCalculationAsync(
            OrderId, municipio.Id, Competencia, 500m, FaturamentoPostingMode.Set, PostedAt);

        (await harness.CreatePoster().FindExistingAsync(Request(municipio, []))).Should().BeNull();
    }

    // ── POST-B3..B6: chart resolution ─────────────────────────────────────────

    [Fact]
    public async Task PostAsync_POST_B3_MunicipioWithoutDistribuidora_UsesGlobalChartOnly()
    {
        var harness = new InspectionPostingHarness(); // global VS 100 W → 117 W billed
        var poster = harness.CreatePoster();

        var posting = await poster.PostAsync(Request(NewMunicipio(), [Exact(10), Lampless(1)]));

        posting.DeltaKwh.Should().Be(IpEnergyMath.Round(JanuaryKwh(10, 117m) + JanuaryKwh(1, 117m)));
        harness.DistribuidoraChart.FindCalls.Should().BeEmpty("the Distribuidora reader is never called");
        harness.DistribuidoraChart.MeanCalls.Should().Be(0);
    }

    [Fact]
    public async Task PostAsync_POST_B4_DistribuidoraRow_WinsOverGlobalRowForTheSameKey()
    {
        var harness = new InspectionPostingHarness();
        harness.DistribuidoraChart.With(DistribuidoraId, LampTypeCode.VS, 100, ballastLossW: 30m); // 130 W vs global 117 W

        var posting = await harness.CreatePoster().PostAsync(Request(NewMunicipio(DistribuidoraId), [Exact(10)]));

        posting.DeltaKwh.Should().Be(JanuaryKwh(10, 130m));
        posting.IsProvisional.Should().BeFalse();
        posting.ExactPoints.Should().Be(10);
    }

    [Fact]
    public async Task PostAsync_POST_B5_DistribuidoraLacksTheRow_FallsBackToGlobal()
    {
        var harness = new InspectionPostingHarness();
        harness.DistribuidoraChart.With(DistribuidoraId, LampTypeCode.LD, 60, ballastLossW: 0m);

        var posting = await harness.CreatePoster().PostAsync(Request(NewMunicipio(DistribuidoraId), [Exact(10)]));

        posting.DeltaKwh.Should().Be(JanuaryKwh(10, 117m));
    }

    [Fact]
    public async Task PostAsync_POST_B5_LampInNeitherChart_Throws_AndNothingIsPostedOrSaved()
    {
        var harness = new InspectionPostingHarness();
        var poster = harness.CreatePoster();

        var act = () => poster.PostAsync(Request(NewMunicipio(DistribuidoraId), [Exact(1), Exact(2, LampTypeCode.MV, 400)]));

        var ex = (await act.Should().ThrowAsync<InspectionLampNotInChartException>()).Which;
        ex.Code.Should().Be(LampTypeCode.MV);
        ex.WattageW.Should().Be(400);
        harness.Faturamentos.PostCalls.Should().Be(0);
        harness.Faturamentos.SaveCalls.Should().Be(0);
        harness.Faturamentos.Postings.Should().BeEmpty();
    }

    [Fact]
    public async Task PostAsync_POST_B6_DistribuidoraMean_IsUsedForProvisionalPoints()
    {
        var harness = new InspectionPostingHarness();
        harness.DistribuidoraChart
            .With(DistribuidoraId, LampTypeCode.LD, 60, ballastLossW: 0m)    // 60
            .With(DistribuidoraId, LampTypeCode.VS, 250, ballastLossW: 30m); // 280 → mean 170

        var posting = await harness.CreatePoster().PostAsync(Request(NewMunicipio(DistribuidoraId), [Lampless(10)]));

        posting.DeltaKwh.Should().Be(JanuaryKwh(10, 170m));
        posting.IsProvisional.Should().BeTrue();
        posting.ProvisionalPoints.Should().Be(10);
        harness.Faturamentos.Postings.Single().IsProvisional.Should().BeTrue();
    }

    [Fact]
    public async Task PostAsync_POST_B6_EmptyDistribuidoraChart_FallsBackToTheRoundedGlobalMean()
    {
        // Global mean of 117, 117.5 and 100.1234 = 111.54113… → rounded to 111.5411 W per point.
        var harness = new InspectionPostingHarness
        {
            StandardChart = new FakeLampTypeRepository(
                (LampTypeCode.VS, 100, 17m), (LampTypeCode.VS, 70, 47.5m), (LampTypeCode.LD, 100, 0.1234m)),
        };

        var posting = await harness.CreatePoster().PostAsync(Request(NewMunicipio(DistribuidoraId), [Lampless(10)]));

        posting.DeltaKwh.Should().Be(JanuaryKwh(10, 111.5411m));
        harness.DistribuidoraChart.MeanCalls.Should().Be(1);
    }

    [Fact]
    public async Task PostAsync_POST_B6_NoChartRowsAnywhere_ProvisionalPointsThrow_AndNothingIsPosted()
    {
        var harness = new InspectionPostingHarness { StandardChart = new FakeLampTypeRepository() };
        var poster = harness.CreatePoster();

        await poster.Invoking(p => p.PostAsync(Request(NewMunicipio(DistribuidoraId), [Lampless(1)])))
            .Should().ThrowAsync<InspectionStandardWattageUnavailableException>();
        harness.Faturamentos.Postings.Should().BeEmpty();
        harness.Faturamentos.SaveCalls.Should().Be(0);
    }

    // ── POST-B7: retroactive window ───────────────────────────────────────────

    [Fact]
    public async Task PostAsync_POST_B7_PriorValidatedLevantamento_DrivesTheRetroactiveDays()
    {
        var harness = new InspectionPostingHarness();
        var municipio = NewMunicipio();
        // Most recent one strictly before 2027-01 is 2026-12 → 31 retroactive days; 2027-01 itself is ignored.
        harness.Levantamentos
            .WithValidated(municipio.Id, "2026-10")
            .WithValidated(municipio.Id, "2026-12")
            .WithValidated(municipio.Id, "2027-01");

        var posting = await harness.CreatePoster().PostAsync(Request(municipio, [Exact(10, retro: true)]));

        posting.DeltaKwh.Should().Be(IpEnergyMath.Round(JanuaryKwh(10, 117m) + JanuaryKwh(10, 117m)),
            "31 days of the competência plus 31 retroactive days back to 2026-12-01");
    }

    [Fact]
    public async Task PostAsync_POST_B7_NoPriorLevantamento_RetroactiveKwhIsZero()
    {
        var harness = new InspectionPostingHarness();

        var posting = await harness.CreatePoster().PostAsync(Request(NewMunicipio(), [Exact(10, retro: true)]));

        posting.DeltaKwh.Should().Be(JanuaryKwh(10, 117m));
    }

    // ── POST-B8 / POST-B9 ─────────────────────────────────────────────────────

    [Fact]
    public async Task PostAsync_POST_B8_Success_AccumulatesOnce_SavesOnce_AndStoresTheProvisionalMarker()
    {
        var harness = new InspectionPostingHarness();
        var municipio = NewMunicipio();

        var posting = await harness.CreatePoster().PostAsync(Request(municipio, [Exact(10)]));

        posting.WasAlreadyPosted.Should().BeFalse();
        harness.Faturamentos.PostCalls.Should().Be(1);
        harness.Faturamentos.SaveCalls.Should().Be(1);
        var stored = harness.Faturamentos.Postings.Should().ContainSingle().Which;
        stored.Mode.Should().Be(FaturamentoPostingMode.Accumulate);
        stored.ServiceOrderId.Should().Be(OrderId);
        stored.MunicipioId.Should().Be(municipio.Id);
        stored.Competencia.Should().Be(Competencia);
        stored.DeltaOrBaselineKwh.Should().Be(posting.DeltaKwh);
        stored.PostedAt.Should().Be(PostedAt);
        stored.IsProvisional.Should().BeFalse();
        posting.Faturamento.KwhFaturado.Should().Be(posting.DeltaKwh);
    }

    [Fact]
    public async Task PostAsync_POST_B8_NoEntries_PostsAZeroNonProvisionalDelta()
    {
        var harness = new InspectionPostingHarness();

        var posting = await harness.CreatePoster().PostAsync(Request(NewMunicipio(), []));

        posting.DeltaKwh.Should().Be(0m);
        posting.IsProvisional.Should().BeFalse();
        harness.Faturamentos.Postings.Should().ContainSingle(p => p.DeltaOrBaselineKwh == 0m && !p.IsProvisional);
    }

    [Fact]
    public async Task PostAsync_POST_B8_ForwardsTheCancellationToken()
    {
        var harness = new InspectionPostingHarness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await harness.CreatePoster().Invoking(p => p.PostAsync(Request(NewMunicipio(), [Exact(1)]), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        harness.Faturamentos.Postings.Should().BeEmpty();
    }

    [Fact]
    public async Task PostAsync_POST_B9_SameLampAcrossEntries_IsLookedUpOnce()
    {
        var harness = new InspectionPostingHarness
        {
            StandardChart = new FakeLampTypeRepository((LampTypeCode.VS, 100, 17m), (LampTypeCode.LD, 60, 0m)),
        };

        await harness.CreatePoster().PostAsync(Request(
            NewMunicipio(DistribuidoraId),
            [Exact(1), Exact(2), Exact(3, LampTypeCode.LD, 60), Exact(4), Lampless(5)]));

        harness.DistribuidoraChart.FindCalls.Should().BeEquivalentTo(
        [
            (DistribuidoraId, LampTypeCode.VS, 100),
            (DistribuidoraId, LampTypeCode.LD, 60),
        ]);
    }
}
