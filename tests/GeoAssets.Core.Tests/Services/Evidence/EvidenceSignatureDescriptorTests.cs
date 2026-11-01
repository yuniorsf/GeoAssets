using FluentAssertions;
using GeoAssets.Core.Services.Evidence;
using Xunit;

namespace GeoAssets.Core.Tests.Services.Evidence;

public class EvidenceSignatureDescriptorTests
{
    [Fact]
    public void ToDescriptorString_ThenTryParse_RoundTrips()
    {
        var original = new EvidenceSignatureDescriptor(
            Signature: "c2lnbmF0dXJl",
            Latitude: -23.55052,
            Longitude: -46.633309,
            CapturedAtUtc: new DateTimeOffset(2026, 11, 1, 14, 32, 0, TimeSpan.Zero),
            OrderId: "order-123");

        var parsed = EvidenceSignatureDescriptor.TryParse(original.ToDescriptorString(), out var result);

        parsed.Should().BeTrue();
        result.Should().Be(original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-descriptor")]
    [InlineData("WrongPrefix;sig;1;2;2026-11-01T00:00:00Z;order")]
    [InlineData("GeoAssetsEvidenceV1;sig;not-a-number;2;2026-11-01T00:00:00Z;order")]
    public void TryParse_RejectsMalformedInput(string? malformed)
    {
        EvidenceSignatureDescriptor.TryParse(malformed, out var result).Should().BeFalse();
        result.Should().BeNull();
    }
}
