using FluentAssertions;
using GeoAssets.Core.Services.Evidence;
using Xunit;

namespace GeoAssets.Core.Tests.Services.Evidence;

public class EvidenceHmacSignerTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 11, 1, 14, 32, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeSignature_SameInputs_ProducesSameSignature()
    {
        byte[] image = [1, 2, 3, 4, 5];

        var a = EvidenceHmacSigner.ComputeSignature(image, -23.5, -46.6, CapturedAt, "order-1", "tenant-secret");
        var b = EvidenceHmacSigner.ComputeSignature(image, -23.5, -46.6, CapturedAt, "order-1", "tenant-secret");

        a.Should().Be(b);
    }

    [Fact]
    public void ComputeSignature_ChangedImageByte_ProducesDifferentSignature()
    {
        byte[] image = [1, 2, 3, 4, 5];
        byte[] tampered = [1, 2, 3, 4, 6];

        var original = EvidenceHmacSigner.ComputeSignature(image, -23.5, -46.6, CapturedAt, "order-1", "tenant-secret");
        var afterTamper = EvidenceHmacSigner.ComputeSignature(tampered, -23.5, -46.6, CapturedAt, "order-1", "tenant-secret");

        original.Should().NotBe(afterTamper);
    }

    [Theory]
    [InlineData(-23.6, -46.6, "order-1", "tenant-secret")] // latitude changed
    [InlineData(-23.5, -46.7, "order-1", "tenant-secret")] // longitude changed
    [InlineData(-23.5, -46.6, "order-2", "tenant-secret")] // order id changed
    [InlineData(-23.5, -46.6, "order-1", "other-secret")]  // tenant secret changed
    public void ComputeSignature_ChangedSignedField_ProducesDifferentSignature(double lat, double lon, string orderId, string secret)
    {
        byte[] image = [1, 2, 3, 4, 5];

        var baseline = EvidenceHmacSigner.ComputeSignature(image, -23.5, -46.6, CapturedAt, "order-1", "tenant-secret");
        var changed = EvidenceHmacSigner.ComputeSignature(image, lat, lon, CapturedAt, orderId, secret);

        changed.Should().NotBe(baseline);
    }
}
