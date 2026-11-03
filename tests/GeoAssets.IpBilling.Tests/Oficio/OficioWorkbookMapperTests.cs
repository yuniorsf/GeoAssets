using FluentAssertions;
using GeoAssets.IpBilling.Oficio;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Oficio;

public class OficioWorkbookMapperTests
{
    [Fact]
    public void ToLines_GroupsRowsByCodigoInstalacao()
    {
        var rows = new[]
        {
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 10),
            new OficioWorkbookRow("INST-001", OficioOperationGroup.Retirada, 3),
            new OficioWorkbookRow("INST-002", OficioOperationGroup.Inclusao, 5),
        };

        var lines = OficioWorkbookMapper.ToLines("order-1", rows);

        lines.Should().HaveCount(2);
        var inst001 = lines.Single(l => string.Equals(l.CodigoInstalacao, "INST-001", StringComparison.Ordinal));
        inst001.OperationGroups.Should().HaveCount(2);
        inst001.ServiceOrderId.Should().Be("order-1");
    }

    [Fact]
    public void ToLines_NoRows_ReturnsEmpty()
    {
        var lines = OficioWorkbookMapper.ToLines("order-1", []);

        lines.Should().BeEmpty();
    }

    [Fact]
    public void ToLines_DoesNotComputeAgregadoOrRetroativo()
    {
        var rows = new[] { new OficioWorkbookRow("INST-001", OficioOperationGroup.Inclusao, 10) };

        var lines = OficioWorkbookMapper.ToLines("order-1", rows);

        lines.Single().Agregado.Should().Be(0, "mapping is grouping-only; OficioLineCalculator computes totals separately");
    }
}
