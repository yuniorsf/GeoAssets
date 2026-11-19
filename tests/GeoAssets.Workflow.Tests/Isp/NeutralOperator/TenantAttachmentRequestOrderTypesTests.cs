using FluentAssertions;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

/// <summary>The attribute schema of the tenant's attachment request (XD01-365): which pole it is for is required.</summary>
public sealed class TenantAttachmentRequestOrderTypesTests
{
    private static IReadOnlyList<string> Validate(params (string Key, string Value)[] attributes) =>
        ServiceOrderAttributeValidator.Validate(
            TenantAttachmentRequestOrderTypes.CreateAttachmentRequest(),
            attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));

    [Fact]
    public void TenantAttachmentRequestOrderTypes_DeclaresPoleFeatureIdSchema()
    {
        TenantAttachmentRequestOrderTypes.CreateAttachmentRequest().AttributesSchemaJson
            .Should().Be(TenantAttachmentRequestOrderTypes.AttributesSchema);

        Validate().Should().NotBeEmpty("the pole is required");
        Validate((TenantAttachmentRequestOrderTypes.PoleFeatureIdAttribute, string.Empty)).Should().NotBeEmpty("an empty pole id names no pole");
        Validate((TenantAttachmentRequestOrderTypes.PoleFeatureIdAttribute, "pole-1")).Should().BeEmpty();
        Validate((TenantAttachmentRequestOrderTypes.PoleFeatureIdAttribute, "12345")).Should().BeEmpty("a numeric-looking pole id is still a pole id");
    }
}
