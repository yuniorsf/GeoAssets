using FluentAssertions;
using GeoAssets.Core.Plugins;
using Xunit;

namespace GeoAssets.Core.Tests.Plugins;

public sealed class NativeLibraryOwnershipTests
{
    private static string UniqueLibrary() => "test-native-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TheFirstClaim_OwnsTheLibrary_AndTheSameAssemblyMayClaimAgain()
    {
        var library = UniqueLibrary();
        var owner = typeof(NativeLibraryOwnershipTests).Assembly;

        NativeLibraryOwnership.OwnerOf(library).Should().BeNull();
        NativeLibraryOwnership.Claim(library, owner);
        NativeLibraryOwnership.Claim(library.ToUpperInvariant(), owner);

        NativeLibraryOwnership.OwnerOf(library).Should().BeSameAs(owner);
    }

    [Fact]
    public void AnotherAssembly_IsRefused_NamingBothCopies()
    {
        var library = UniqueLibrary();
        var owner = typeof(NativeLibraryOwnershipTests).Assembly;
        NativeLibraryOwnership.Claim(library, owner);

        var act = () => NativeLibraryOwnership.Claim(library, typeof(NativeLibraryOwnership).Assembly);

        var refusal = act.Should().Throw<NativeLibraryOwnershipException>().Which;
        refusal.LibraryName.Should().Be(library);
        refusal.OwnerLoadContext.Should().Be("Default");
        refusal.ClaimantLoadContext.Should().Be("Default");
        refusal.Message.Should().Contain("GeoAssets.Core.Tests").And.Contain("GeoAssets.Core").And.Contain("never both");
        NativeLibraryOwnership.OwnerOf(library).Should().BeSameAs(owner);
    }

    [Fact]
    public async Task ConcurrentClaims_HaveExactlyOneWinner()
    {
        var library = UniqueLibrary();
        var contenders = new[] { typeof(NativeLibraryOwnershipTests).Assembly, typeof(NativeLibraryOwnership).Assembly };
        using var start = new Barrier(contenders.Length * 8);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, contenders.Length * 8).Select(i => Task.Run(() =>
        {
            start.SignalAndWait();
            try
            {
                NativeLibraryOwnership.Claim(library, contenders[i % contenders.Length]);
                return contenders[i % contenders.Length];
            }
            catch (NativeLibraryOwnershipException)
            {
                return null;
            }
        })));

        var winner = NativeLibraryOwnership.OwnerOf(library);
        outcomes.Where(o => o is not null).Should().OnlyContain(o => ReferenceEquals(o, winner)).And.HaveCount(8);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankLibraryName_IsRejected(string library)
    {
        var claim = () => NativeLibraryOwnership.Claim(library, typeof(NativeLibraryOwnershipTests).Assembly);
        var lookup = () => NativeLibraryOwnership.OwnerOf(library);

        claim.Should().Throw<ArgumentException>();
        lookup.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ANullOwner_IsRejected()
    {
        var act = () => NativeLibraryOwnership.Claim(UniqueLibrary(), null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
