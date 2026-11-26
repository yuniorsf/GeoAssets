using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GeoAssets.Ingestion.Model;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Model;

public sealed class DeterministicFeatureIdTests
{
    private static readonly SourceKey Key = new("neoenergia-gse", "PG", "12345");

    [Fact]
    public void For_SameKey_IsStable()
    {
        DeterministicFeatureId.For(Key).Should().Be(DeterministicFeatureId.For(new SourceKey("neoenergia-gse", "PG", "12345")));
    }

    [Fact]
    public void For_IsAVersion8Rfc9562Uuid()
    {
        var id = DeterministicFeatureId.For(Key);

        Guid.TryParseExact(id, "D", out var parsed).Should().BeTrue();
        id.Should().HaveLength(36);
        id[14].Should().Be('8');
        "89ab".Should().Contain(id[19].ToString());
        parsed.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void For_IsLowerCaseAndFitsTheKernelsIdLimit()
    {
        var id = DeterministicFeatureId.For(Key);

        id.Any(char.IsUpper).Should().BeFalse();
        id.Length.Should().BeLessThanOrEqualTo(36);
    }

    [Theory]
    [InlineData("other-profile", "PG", "12345")]
    [InlineData("neoenergia-gse", "INST", "12345")]
    [InlineData("neoenergia-gse", "PG", "12346")]
    public void For_AnyKeyPartChanging_ChangesTheId(string profile, string ns, string natural)
    {
        DeterministicFeatureId.For(new SourceKey(profile, ns, natural)).Should().NotBe(DeterministicFeatureId.For(Key));
    }

    [Fact]
    public void For_IsTheSha256OfTheKeysStringFormWithVersionAndVariantBitsSet()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("neoenergia-gse|PG|12345"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        DeterministicFeatureId.For(Key).Should().Be(new Guid(bytes, bigEndian: true).ToString());
    }
}
