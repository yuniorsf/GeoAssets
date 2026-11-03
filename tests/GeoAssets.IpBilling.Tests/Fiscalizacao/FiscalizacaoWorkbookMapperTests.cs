using FluentAssertions;
using GeoAssets.IpBilling.Fiscalizacao;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Fiscalizacao;

public class FiscalizacaoWorkbookMapperTests
{
    [Fact]
    public void ToLines_GroupsRowsByCodigoInstalacao()
    {
        var rows = new[]
        {
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 8),
            new FiscalizacaoWorkbookRow("INST-002", FiscalizacaoOperationGroup.Fiscalizacao, 5),
        };

        var lines = FiscalizacaoWorkbookMapper.ToLines("order-1", rows);

        lines.Should().HaveCount(2);
        var inst1 = lines.Single(l => string.Equals(l.CodigoInstalacao, "INST-001", StringComparison.Ordinal));
        inst1.OperationGroups.Should().HaveCount(2);
        inst1.OperationGroups.Should().OnlyContain(d => d.LineId == inst1.Id);
        inst1.ServiceOrderId.Should().Be("order-1");

        var inst2 = lines.Single(l => string.Equals(l.CodigoInstalacao, "INST-002", StringComparison.Ordinal));
        inst2.OperationGroups.Should().ContainSingle(d => d.Count == 5 && d.OperationGroup == FiscalizacaoOperationGroup.Fiscalizacao);
    }

    [Fact]
    public void ToLines_NoRows_ReturnsEmpty()
    {
        var lines = FiscalizacaoWorkbookMapper.ToLines("order-1", []);

        lines.Should().BeEmpty();
    }

    [Fact]
    public void ToLines_DoesNotComputeAgregadoOrRetroativo()
    {
        var rows = new[] { new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10) };

        var lines = FiscalizacaoWorkbookMapper.ToLines("order-1", rows);

        lines.Single().Agregado.Should().Be(0);
        lines.Single().Retroativo.Should().Be(0);
    }
}
