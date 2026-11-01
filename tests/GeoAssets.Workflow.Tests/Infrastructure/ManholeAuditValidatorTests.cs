using FluentAssertions;
using GeoAssets.Workflow.Infrastructure;
using Xunit;

namespace GeoAssets.Workflow.Tests.Infrastructure;

public class ManholeAuditValidatorTests
{
    private static Dictionary<string, string> ValidNr33Attributes() => new(StringComparer.Ordinal)
    {
        ["entryPermitIssued"] = "true",
        ["continuousVentilationActive"] = "true",
        ["rescuePlanInPlace"] = "true",
        ["standbyAttendantPresent"] = "true",
        ["communicationEquipmentVerified"] = "true",
        ["oxygenPercent"] = "20.9",
        ["lowerExplosiveLimitPercent"] = "0",
        ["carbonMonoxidePpm"] = "0",
        ["hydrogenSulfidePpm"] = "0",
    };

    private static ManholeAudit AuditWith(Dictionary<string, string> nr33Attributes) => new()
    {
        ConduitFeatureId = "conduit-1",
        Nr33ChecklistAttributes = nr33Attributes,
    };

    [Fact]
    public void ValidateNr33Checklist_AllFieldsValid_IsValid()
    {
        ManholeAuditValidator.ValidateNr33Checklist(ValidNr33Attributes()).Should().BeEmpty();
    }

    [Fact]
    public void ValidateNr33Checklist_RescuePlanMissing_IsInvalid()
    {
        var attributes = ValidNr33Attributes();
        attributes.Remove("rescuePlanInPlace");

        ManholeAuditValidator.ValidateNr33Checklist(attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateNr33Checklist_OxygenOutOfRange_IsInvalid()
    {
        var attributes = ValidNr33Attributes();
        attributes["oxygenPercent"] = "15.0";

        ManholeAuditValidator.ValidateNr33Checklist(attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void EnsureCanComplete_ValidChecklist_DoesNotThrow()
    {
        var act = () => ManholeAuditValidator.EnsureCanComplete(AuditWith(ValidNr33Attributes()));

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanComplete_InvalidChecklist_Throws()
    {
        var attributes = ValidNr33Attributes();
        attributes["standbyAttendantPresent"] = "false";

        var act = () => ManholeAuditValidator.EnsureCanComplete(AuditWith(attributes));

        act.Should().Throw<ManholeAuditValidationException>()
            .Which.ConduitFeatureId.Should().Be("conduit-1");
    }

    [Fact]
    public void EnsureCanComplete_EmptyChecklist_Throws()
    {
        var act = () => ManholeAuditValidator.EnsureCanComplete(AuditWith([]));

        act.Should().Throw<ManholeAuditValidationException>();
    }
}
