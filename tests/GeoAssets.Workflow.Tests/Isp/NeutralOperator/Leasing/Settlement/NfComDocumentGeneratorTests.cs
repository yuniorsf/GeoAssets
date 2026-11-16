using Xunit;
using GeoAssets.Core.Isp.NeutralOperator.Leasing;
using GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator.Leasing.Settlement;

public sealed class NfComDocumentGeneratorTests
{
    private static LeaseInvoice Invoice() => new(
        "11.111.111/0001-11",
        new DateOnly(2026, 11, 1),
        [
            new LeaseInvoiceLineItem("Fixation points", 150m, LeaseInvoiceLineItemKind.FixationCharge),
            new LeaseInvoiceLineItem("Regularization fine", 50m, LeaseInvoiceLineItemKind.RegularizationFine),
        ]);

    [Fact]
    public async Task GenerateAsync_ProducesWellFormedXmlWithAllLineItemsAndTotal()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var generator = new NfComDocumentGenerator(timeProvider, () => "DOC-0001");

        var result = await generator.GenerateAsync(Invoice(), CancellationToken.None);

        var doc = System.Xml.Linq.XDocument.Parse(result.Xml);
        Assert.Equal("DOC-0001", result.DocumentNumber);
        Assert.Equal(2, doc.Descendants().Count(e => string.Equals(e.Name.LocalName, "item", StringComparison.Ordinal)));
        Assert.Contains("200", doc.Descendants().First(e => string.Equals(e.Name.LocalName, "total", StringComparison.Ordinal)).Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_UsesDocumentNumberFactoryEachCall()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var counter = 0;
        var generator = new NfComDocumentGenerator(timeProvider, () => $"DOC-{++counter}");

        var first = await generator.GenerateAsync(Invoice(), CancellationToken.None);
        var second = await generator.GenerateAsync(Invoice(), CancellationToken.None);

        Assert.Equal("DOC-1", first.DocumentNumber);
        Assert.Equal("DOC-2", second.DocumentNumber);
    }

    [Fact]
    public async Task GenerateAsync_IssuedAtUtc_ReflectsTimeProvider()
    {
        var now = new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var generator = new NfComDocumentGenerator(timeProvider, () => "DOC-0001");

        var result = await generator.GenerateAsync(Invoice(), CancellationToken.None);

        Assert.Equal(now, result.IssuedAtUtc);
    }
}
