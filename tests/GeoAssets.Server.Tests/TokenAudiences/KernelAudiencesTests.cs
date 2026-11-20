using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.TokenAudiences;

public sealed class KernelAudiencesTests
{
    [Fact]
    public void AdditionalAudiencesKey_IsTheKeyUnderTheCiamSection() =>
        KernelAudiences.AdditionalAudiencesKey.Should().Be("AdditionalAudiences");

    [Fact]
    public void Resolve_WithAClientIdAndNoAdditional_IsTheClientIdAndItsApiForm() =>
        KernelAudiences.Resolve("kernel", null).Should().Equal("kernel", "api://kernel");

    [Fact]
    public void Resolve_WithAnEmptyAdditionalList_IsTheClientIdAndItsApiForm() =>
        KernelAudiences.Resolve("kernel", []).Should().Equal("kernel", "api://kernel");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithABlankClientId_OmitsItAndItsApiForm(string? clientId) =>
        KernelAudiences.Resolve(clientId, ["vertical"]).Should().Equal("vertical");

    [Fact]
    public void Resolve_WithABlankClientIdAndNoAdditional_IsEmpty() =>
        KernelAudiences.Resolve(null, null).Should().BeEmpty();

    [Fact]
    public void Resolve_AppendsTheAdditionalAudiences_InOrder_AfterTheKernelsOwn() =>
        KernelAudiences.Resolve("kernel", ["b", "a", "c"]).Should().Equal("kernel", "api://kernel", "b", "a", "c");

    [Fact]
    public void Resolve_DropsBlankAndNullAdditionalEntries() =>
        KernelAudiences.Resolve("kernel", [null, "", "  ", "a"]).Should().Equal("kernel", "api://kernel", "a");

    [Fact]
    public void Resolve_TrimsWhitespaceAroundEntriesAndTheClientId() =>
        KernelAudiences.Resolve("  kernel ", ["  a  ", "\tb\n"]).Should().Equal("kernel", "api://kernel", "a", "b");

    [Fact]
    public void Resolve_KeepsTheFirstOccurrenceOfADuplicate() =>
        KernelAudiences.Resolve("kernel", ["a", "b", "a", " b "]).Should().Equal("kernel", "api://kernel", "a", "b");

    [Fact]
    public void Resolve_DropsAnAdditionalEntryEqualToTheClientIdOrItsApiForm() =>
        KernelAudiences.Resolve("kernel", ["api://kernel", "kernel", "a"]).Should().Equal("kernel", "api://kernel", "a");

    [Fact]
    public void Resolve_TreatsDuplicatesOrdinally_SoCaseDifferencesAreDistinct() =>
        KernelAudiences.Resolve("kernel", ["A", "a"]).Should().Equal("kernel", "api://kernel", "A", "a");

    [Fact]
    public void AnyAdditional_WhenNull_IsFalse() => KernelAudiences.AnyAdditional(null).Should().BeFalse();

    [Fact]
    public void AnyAdditional_WhenEmpty_IsFalse() => KernelAudiences.AnyAdditional([]).Should().BeFalse();

    [Fact]
    public void AnyAdditional_WhenOnlyBlankEntries_IsFalse() => KernelAudiences.AnyAdditional([null, "", "   "]).Should().BeFalse();

    [Fact]
    public void AnyAdditional_WhenAtLeastOneEntryIsNotBlank_IsTrue() => KernelAudiences.AnyAdditional([null, "a"]).Should().BeTrue();
}
