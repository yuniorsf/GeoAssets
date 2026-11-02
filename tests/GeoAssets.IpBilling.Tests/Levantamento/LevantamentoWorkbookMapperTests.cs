using FluentAssertions;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Levantamento;

public class LevantamentoWorkbookMapperTests
{
    [Fact]
    public void ToTransformerLines_GroupsRowsByCodigoInstalacao()
    {
        var rows = new[]
        {
            new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 10),
            new LevantamentoWorkbookRow("INST-001", LampTypeCode.VS, 150, 5),
            new LevantamentoWorkbookRow("INST-002", LampTypeCode.LD, 60, 20),
        };

        var lines = LevantamentoWorkbookMapper.ToTransformerLines("order-1", rows);

        lines.Should().HaveCount(2);
        var inst1 = lines.Single(l => string.Equals(l.CodigoInstalacao, "INST-001", StringComparison.Ordinal));
        inst1.LampDetails.Should().HaveCount(2);
        inst1.LampDetails.Should().OnlyContain(d => d.TransformerLineId == inst1.Id);
        inst1.ServiceOrderId.Should().Be("order-1");

        var inst2 = lines.Single(l => string.Equals(l.CodigoInstalacao, "INST-002", StringComparison.Ordinal));
        inst2.LampDetails.Should().ContainSingle(d => d.Quantity == 20 && d.WattageW == 60);
    }

    [Fact]
    public void ToTransformerLines_NoRows_ReturnsEmpty()
    {
        var lines = LevantamentoWorkbookMapper.ToTransformerLines("order-1", []);

        lines.Should().BeEmpty();
    }
}
