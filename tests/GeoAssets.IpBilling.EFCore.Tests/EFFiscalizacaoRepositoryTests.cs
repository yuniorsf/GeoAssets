using FluentAssertions;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFFiscalizacaoRepositoryTests
{
    private static FiscalizacaoHeader Header(string orderId, Guid municipioId, string competencia, bool validated = false) => new()
    {
        ServiceOrderId = orderId,
        MunicipioId = municipioId,
        Competencia = competencia,
        IsValidated = validated,
    };

    private static FiscalizacaoLine Line(string orderId, string codigo)
    {
        var line = new FiscalizacaoLine
        {
            ServiceOrderId = orderId,
            CodigoInstalacao = codigo,
            Agregado = 3,
            Retroativo = 2,
        };
        line.OperationGroups.Add(new FiscalizacaoOperationGroupDetail
        {
            LineId = line.Id,
            OperationGroup = FiscalizacaoOperationGroup.Fiscalizacao,
            Count = 10,
        });
        line.OperationGroups.Add(new FiscalizacaoOperationGroupDetail
        {
            LineId = line.Id,
            OperationGroup = FiscalizacaoOperationGroup.UltimoLevantamento,
            Count = 8,
        });
        return line;
    }

    [Fact]
    public async Task AddHeaderAsync_ThenGetHeaderAsync_RoundTrips()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFFiscalizacaoRepository(fixture.Context);
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
        var repo = new EFFiscalizacaoRepository(fixture.Context);
        await repo.AddHeaderAsync(Header("order-1", Guid.NewGuid(), "2027-01"));
        await repo.SaveChangesAsync();

        await repo.MarkValidatedAsync("order-1");
        await repo.SaveChangesAsync();

        var loaded = await new EFFiscalizacaoRepository(fixture.NewContext()).GetHeaderAsync("order-1");
        loaded!.IsValidated.Should().BeTrue();
    }

    [Fact]
    public async Task ReplaceLinesAsync_RoundTripsLinesAndOperationGroupDetails()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFFiscalizacaoRepository(fixture.Context);

        await repo.ReplaceLinesAsync("order-1", [Line("order-1", "INST-001")]);
        await repo.SaveChangesAsync();

        var lines = await new EFFiscalizacaoRepository(fixture.NewContext()).GetLinesAsync("order-1");
        lines.Should().ContainSingle();
        var line = lines[0];
        line.CodigoInstalacao.Should().Be("INST-001");
        line.Agregado.Should().Be(3);
        line.Retroativo.Should().Be(2);
        line.OperationGroups.Should().HaveCount(2);
        line.OperationGroups.Should().ContainSingle(d => d.OperationGroup == FiscalizacaoOperationGroup.Fiscalizacao && d.Count == 10);
    }

    [Fact]
    public async Task ReplaceLinesAsync_CalledAgain_RemovesPriorLinesAndDetails()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFFiscalizacaoRepository(fixture.Context);

        await repo.ReplaceLinesAsync("order-1", [Line("order-1", "INST-001")]);
        await repo.SaveChangesAsync();

        await repo.ReplaceLinesAsync("order-1", [Line("order-1", "INST-002")]);
        await repo.SaveChangesAsync();

        var lines = await new EFFiscalizacaoRepository(fixture.NewContext()).GetLinesAsync("order-1");
        lines.Should().ContainSingle(l => l.CodigoInstalacao == "INST-002");
    }

    [Fact]
    public async Task GetLinesAsync_NoLines_ReturnsEmpty()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFFiscalizacaoRepository(fixture.Context);

        var lines = await repo.GetLinesAsync("order-missing");

        lines.Should().BeEmpty();
    }

    // ── XD01-301: lamp details ───────────────────────────────────────────────

    private static FiscalizacaoLine LineWithLampDetails(string orderId, string codigo)
    {
        var line = Line(orderId, codigo);
        line.LampDetails.Add(new FiscalizacaoLampDetail
        {
            LineId = line.Id,
            OperationGroup = FiscalizacaoOperationGroup.Fiscalizacao,
            Code = LampTypeCode.VS,
            WattageW = 100,
            Quantity = 6,
        });
        line.LampDetails.Add(new FiscalizacaoLampDetail
        {
            LineId = line.Id,
            OperationGroup = FiscalizacaoOperationGroup.UltimoLevantamento,
            Code = LampTypeCode.LD,
            WattageW = 60,
            Quantity = 8,
        });
        return line;
    }

    [Fact]
    public async Task ReplaceLinesAsync_RoundTripsLampDetails()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFFiscalizacaoRepository(fixture.Context);
        var original = LineWithLampDetails("order-1", "INST-001");

        await repo.ReplaceLinesAsync("order-1", [original, Line("order-1", "INST-002")]);
        await repo.SaveChangesAsync();

        var lines = await new EFFiscalizacaoRepository(fixture.NewContext()).GetLinesAsync("order-1");
        lines.Should().HaveCount(2);
        var loaded = lines.Single(l => string.Equals(l.CodigoInstalacao, "INST-001", StringComparison.Ordinal));
        loaded.LampDetails.Should().BeEquivalentTo(original.LampDetails);
        loaded.LampDetails.Should().OnlyContain(d => d.LineId == loaded.Id);
        loaded.OperationGroups.Should().HaveCount(2, "lamp details do not disturb the operation groups");
        lines.Single(l => string.Equals(l.CodigoInstalacao, "INST-002", StringComparison.Ordinal)).LampDetails.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceLinesAsync_CalledAgain_ReplacesLampDetailsAndLeavesNoOrphans()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFFiscalizacaoRepository(fixture.Context);
        await repo.ReplaceLinesAsync("order-1", [LineWithLampDetails("order-1", "INST-001")]);
        await repo.SaveChangesAsync();

        var replacement = Line("order-1", "INST-001");
        replacement.LampDetails.Add(new FiscalizacaoLampDetail
        {
            LineId = replacement.Id,
            OperationGroup = FiscalizacaoOperationGroup.Fiscalizacao,
            Code = LampTypeCode.MV,
            WattageW = 400,
            Quantity = 1,
        });
        var replacer = new EFFiscalizacaoRepository(fixture.NewContext());
        await replacer.ReplaceLinesAsync("order-1", [replacement]);
        await replacer.SaveChangesAsync();

        var fresh = fixture.NewContext();
        var loaded = (await new EFFiscalizacaoRepository(fresh).GetLinesAsync("order-1")).Single();
        loaded.LampDetails.Should().ContainSingle().Which.Code.Should().Be(LampTypeCode.MV);
        (await fresh.Database.SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM "FiscalizacaoLampDetails" """).SingleAsync())
            .Should().Be(1, "the replaced line's lamp details were deleted with it");
    }
}
