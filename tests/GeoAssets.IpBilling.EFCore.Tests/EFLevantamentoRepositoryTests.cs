using FluentAssertions;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFLevantamentoRepositoryTests
{
    private static LevantamentoHeader Header(string orderId, Guid municipioId, string competencia, bool validated = false) => new()
    {
        ServiceOrderId = orderId,
        MunicipioId = municipioId,
        Competencia = competencia,
        IsValidated = validated,
    };

    private static LevantamentoTransformerLine Line(string orderId, string codigo, bool isNew = false)
    {
        var line = new LevantamentoTransformerLine
        {
            ServiceOrderId = orderId,
            CodigoInstalacao = codigo,
            IsNewTransformer = isNew,
            Tipo = isNew ? TipoRetroacao.Naive00 : null,
            NormalKwh = 123.4567m,
            RetroativoKwh = isNew ? 10m : null,
        };
        line.LampDetails.Add(new LevantamentoLampDetail
        {
            TransformerLineId = line.Id,
            Code = LampTypeCode.LD,
            WattageW = 100,
            Quantity = 5,
        });
        return line;
    }

    [Fact]
    public async Task AddHeaderAsync_ThenGetHeaderAsync_RoundTrips()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);
        var municipioId = Guid.NewGuid();

        await repo.AddHeaderAsync(Header("order-1", municipioId, "2027-01"));
        await repo.SaveChangesAsync();

        var loaded = await repo.GetHeaderAsync("order-1");
        loaded.Should().NotBeNull();
        loaded!.MunicipioId.Should().Be(municipioId);
        loaded.Competencia.Should().Be("2027-01");
        loaded.IsValidated.Should().BeFalse();
    }

    [Fact]
    public async Task MarkValidatedAsync_SetsIsValidated()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);
        await repo.AddHeaderAsync(Header("order-1", Guid.NewGuid(), "2027-01"));
        await repo.SaveChangesAsync();

        await repo.MarkValidatedAsync("order-1");
        await repo.SaveChangesAsync();

        var loaded = await new EFLevantamentoRepository(fixture.NewContext()).GetHeaderAsync("order-1");
        loaded!.IsValidated.Should().BeTrue();
    }

    [Fact]
    public async Task ReplaceTransformerLinesAsync_RoundTripsLinesAndLampDetails()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);

        await repo.ReplaceTransformerLinesAsync("order-1", [Line("order-1", "INST-001", isNew: true)]);
        await repo.SaveChangesAsync();

        var lines = await new EFLevantamentoRepository(fixture.NewContext()).GetTransformerLinesAsync("order-1");
        lines.Should().ContainSingle();
        var line = lines[0];
        line.CodigoInstalacao.Should().Be("INST-001");
        line.IsNewTransformer.Should().BeTrue();
        line.Tipo.Should().Be(TipoRetroacao.Naive00);
        line.RetroativoKwh.Should().Be(10m);
        line.LampDetails.Should().ContainSingle(d => d.Code == LampTypeCode.LD && d.Quantity == 5);
    }

    [Fact]
    public async Task ReplaceTransformerLinesAsync_CalledAgain_RemovesPriorLinesAndDetails()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);

        await repo.ReplaceTransformerLinesAsync("order-1", [Line("order-1", "INST-001")]);
        await repo.SaveChangesAsync();

        await repo.ReplaceTransformerLinesAsync("order-1", [Line("order-1", "INST-002")]);
        await repo.SaveChangesAsync();

        var lines = await new EFLevantamentoRepository(fixture.NewContext()).GetTransformerLinesAsync("order-1");
        lines.Should().ContainSingle(l => l.CodigoInstalacao == "INST-002");
    }

    [Fact]
    public async Task Replace24hLinesAsync_RoundTrips()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);
        var featureId = Guid.NewGuid();

        await repo.Replace24hLinesAsync("order-1",
            [new Levantamento24hLine { ServiceOrderId = "order-1", FeatureId = featureId, Code = LampTypeCode.VS, WattageW = 250, Quantity = 2, Kwh = 42.5m }]);
        await repo.SaveChangesAsync();

        var lines = await new EFLevantamentoRepository(fixture.NewContext()).Get24hLinesAsync("order-1");
        lines.Should().ContainSingle(l => l.FeatureId == featureId && l.Kwh == 42.5m);
    }

    [Fact]
    public async Task GetMostRecentValidatedAsync_ReturnsLatestValidatedBeforeCutoff_IgnoringUnvalidatedAndLater()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);
        var municipioId = Guid.NewGuid();

        await repo.AddHeaderAsync(Header("order-jan", municipioId, "2027-01", validated: true));
        await repo.AddHeaderAsync(Header("order-feb", municipioId, "2027-02", validated: false)); // not validated — excluded
        await repo.AddHeaderAsync(Header("order-mar", municipioId, "2027-03", validated: true));
        await repo.SaveChangesAsync();

        var result = await repo.GetMostRecentValidatedAsync(municipioId, beforeCompetencia: "2027-04");
        result!.ServiceOrderId.Should().Be("order-mar");

        var beforeMarch = await repo.GetMostRecentValidatedAsync(municipioId, beforeCompetencia: "2027-03");
        beforeMarch!.ServiceOrderId.Should().Be("order-jan");
    }

    [Fact]
    public async Task GetFirstValidatedAsync_ReturnsOldestValidatedCompetencia()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);
        var municipioId = Guid.NewGuid();

        await repo.AddHeaderAsync(Header("order-mar", municipioId, "2027-03", validated: true));
        await repo.AddHeaderAsync(Header("order-jan", municipioId, "2027-01", validated: true));
        await repo.SaveChangesAsync();

        var result = await repo.GetFirstValidatedAsync(municipioId);
        result!.ServiceOrderId.Should().Be("order-jan");
    }

    [Fact]
    public async Task GetAllValidatedCodigosInstalacaoAsync_OnlyIncludesCodigosFromValidatedHeaders()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFLevantamentoRepository(fixture.Context);
        var municipioId = Guid.NewGuid();

        await repo.AddHeaderAsync(Header("order-jan", municipioId, "2027-01", validated: true));
        await repo.AddHeaderAsync(Header("order-feb", municipioId, "2027-02", validated: false));
        await repo.ReplaceTransformerLinesAsync("order-jan", [Line("order-jan", "INST-001")]);
        await repo.ReplaceTransformerLinesAsync("order-feb", [Line("order-feb", "INST-999")]);
        await repo.SaveChangesAsync();

        var codigos = await repo.GetAllValidatedCodigosInstalacaoAsync(municipioId);

        codigos.Should().Contain("INST-001");
        codigos.Should().NotContain("INST-999");
    }
}
