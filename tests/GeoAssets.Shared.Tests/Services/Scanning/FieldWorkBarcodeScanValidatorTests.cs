using FluentAssertions;
using GeoAssets.Shared.Services.Scanning;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Scanning;

/// <summary>
/// XD01-275's AC: scanning populates the corresponding field without manual retyping;
/// malformed/unrecognized scans surface a clear re-scan prompt rather than a silent failure;
/// everything here is pure/offline (no network call, by construction — it's plain regex logic).
/// </summary>
public class FieldWorkBarcodeScanValidatorTests
{
    [Theory]
    [InlineData("TAG-0001")]
    [InlineData("ABCD1234")]
    public void Validate_YellowTagId_WellFormed_IsValid(string payload)
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.YellowTagId, payload);

        result.IsValid.Should().BeTrue();
        result.NormalizedValue.Should().Be(payload);
        result.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void Validate_YellowTagId_TooShort_IsRejectedWithReScanPrompt()
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.YellowTagId, "A1");

        result.IsValid.Should().BeFalse();
        result.NormalizedValue.Should().BeNull();
        result.RejectionReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_OntFsan_WellFormed_IsValid()
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntFsan, "HWTC12345678");

        result.IsValid.Should().BeTrue();
        result.NormalizedValue.Should().Be("HWTC12345678");
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces here")]
    [InlineData("!!!invalid!!!")]
    public void Validate_OntFsan_Malformed_IsRejectedWithReScanPrompt(string payload)
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntFsan, payload);

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Contain("re-scan");
    }

    [Fact]
    public void Validate_OntMacAddress_AlreadyColonSeparated_IsValidUnchanged()
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntMacAddress, "AA:BB:CC:DD:EE:FF");

        result.IsValid.Should().BeTrue();
        result.NormalizedValue.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Theory]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AABBCCDDEEFF", "AA:BB:CC:DD:EE:FF")]
    public void Validate_OntMacAddress_BareHexDigits_NormalizesToColonSeparated(string payload, string expectedNormalized)
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntMacAddress, payload);

        result.IsValid.Should().BeTrue("a 12-hex-digit scan with no separators is a common barcode encoding and should still populate the field");
        result.NormalizedValue.Should().Be(expectedNormalized);
    }

    [Fact]
    public void Validate_OntMacAddress_HyphenSeparated_Normalizes()
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntMacAddress, "aa-bb-cc-dd-ee-ff");

        result.IsValid.Should().BeTrue();
        result.NormalizedValue.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void Validate_OntMacAddress_WrongLength_IsRejectedWithReScanPrompt()
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntMacAddress, "AABBCC");

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Contain("re-scan");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespacePayload_IsRejectedAsEmptyScan(string? payload)
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntFsan, payload);

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Contain("Empty scan");
    }

    [Fact]
    public void Validate_RawPayload_PreservedVerbatimEvenWhenRejected()
    {
        var result = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntFsan, "  bad!!  ");

        result.RawPayload.Should().Be("  bad!!  ");
    }
}
