using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Erp.Sap;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Sap;

/// <summary>XD01-231's inbound adapter: pure parse of a SAP PM work-order notification payload.</summary>
public class SapPmOrderAdapterTests
{
    private static readonly Guid ValidNotificationId = Guid.Parse("8f14e45f-ceea-467e-bd4b-5f3b1d06e26e");
    private const string ValidReportedAt = "2026-01-15T08:30:00Z";

    private static Dictionary<string, object?> ValidPayload() => new(StringComparer.Ordinal)
    {
        ["FunctionalLocation"] = "floc-001",
        ["OrderNumber"] = "4500001234",
        ["ShortText"] = "Replace damaged pole",
        ["ServiceCategory"] = "EmergencyRestoration",
        ["TerritorialSetting"] = "Urban",
        ["NotificationId"] = ValidNotificationId.ToString(),
        ["ReportedAt"] = ValidReportedAt,
    };

    private static string ToJson(Dictionary<string, object?> payload) => JsonSerializer.Serialize(payload);

    private static readonly SapPmOrderAdapter Adapter = new();

    [Fact]
    public void ParseWorkOrderNotification_ValidPayload_ReturnsFullyPopulatedSpec()
    {
        var json = ToJson(ValidPayload());

        var spec = Adapter.ParseWorkOrderNotification(json);

        spec.FunctionalLocation.Should().Be("floc-001");
        spec.OrderNumber.Should().Be("4500001234");
        spec.ShortText.Should().Be("Replace damaged pole");
        spec.ServiceCategory.Should().Be("EmergencyRestoration");
        spec.TerritorialSetting.Should().Be("Urban");
        spec.NotificationId.Should().Be(ValidNotificationId);
        spec.ReportedAtUtc.Should().Be(DateTimeOffset.Parse(ValidReportedAt, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ParseWorkOrderNotification_NullPayload_ThrowsArgumentNullException()
    {
        var act = () => Adapter.ParseWorkOrderNotification(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParseWorkOrderNotification_EmptyPayload_ThrowsArgumentException()
    {
        var act = () => Adapter.ParseWorkOrderNotification(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("not json at all")]
    public void ParseWorkOrderNotification_MalformedJson_ThrowsSapPmOrderParseException(string malformed)
    {
        var act = () => Adapter.ParseWorkOrderNotification(malformed);

        act.Should().Throw<SapPmOrderParseException>()
            .WithInnerException<JsonException>();
    }

    [Theory]
    [InlineData("FunctionalLocation")]
    [InlineData("OrderNumber")]
    [InlineData("ShortText")]
    [InlineData("ServiceCategory")]
    [InlineData("TerritorialSetting")]
    [InlineData("NotificationId")]
    [InlineData("ReportedAt")]
    public void ParseWorkOrderNotification_MissingRequiredField_ThrowsSapPmOrderParseException(string fieldName)
    {
        var payload = ValidPayload();
        payload.Remove(fieldName);
        var json = ToJson(payload);

        var act = () => Adapter.ParseWorkOrderNotification(json);

        act.Should().Throw<SapPmOrderParseException>().WithMessage($"*{fieldName}*");
    }

    [Theory]
    [InlineData("FunctionalLocation")]
    [InlineData("OrderNumber")]
    [InlineData("ShortText")]
    [InlineData("ServiceCategory")]
    [InlineData("TerritorialSetting")]
    public void ParseWorkOrderNotification_BlankRequiredStringField_ThrowsSapPmOrderParseException(string fieldName)
    {
        var payload = ValidPayload();
        payload[fieldName] = "   ";
        var json = ToJson(payload);

        var act = () => Adapter.ParseWorkOrderNotification(json);

        act.Should().Throw<SapPmOrderParseException>().WithMessage($"*{fieldName}*");
    }

    [Theory]
    [InlineData("FunctionalLocation")]
    [InlineData("NotificationId")]
    [InlineData("ReportedAt")]
    public void ParseWorkOrderNotification_NonStringField_ThrowsSapPmOrderParseException(string fieldName)
    {
        var payload = ValidPayload();
        payload[fieldName] = 12345;
        var json = ToJson(payload);

        var act = () => Adapter.ParseWorkOrderNotification(json);

        act.Should().Throw<SapPmOrderParseException>().WithMessage($"*{fieldName}*");
    }

    [Fact]
    public void ParseWorkOrderNotification_NotificationIdNotAGuid_ThrowsSapPmOrderParseException()
    {
        var payload = ValidPayload();
        payload["NotificationId"] = "not-a-guid";
        var json = ToJson(payload);

        var act = () => Adapter.ParseWorkOrderNotification(json);

        act.Should().Throw<SapPmOrderParseException>().WithMessage("*NotificationId*");
    }

    [Fact]
    public void ParseWorkOrderNotification_ReportedAtNotIso8601_ThrowsSapPmOrderParseException()
    {
        var payload = ValidPayload();
        payload["ReportedAt"] = "yesterday-ish";
        var json = ToJson(payload);

        var act = () => Adapter.ParseWorkOrderNotification(json);

        act.Should().Throw<SapPmOrderParseException>().WithMessage("*ReportedAt*");
    }

    [Fact]
    public void ParseWorkOrderNotification_ReportedAtWithoutUtcOffset_StillParsesAsDateTimeOffset()
    {
        var payload = ValidPayload();
        payload["ReportedAt"] = "2026-01-15T08:30:00";
        var json = ToJson(payload);

        var spec = Adapter.ParseWorkOrderNotification(json);

        spec.ReportedAtUtc.Should().Be(DateTimeOffset.Parse("2026-01-15T08:30:00", System.Globalization.CultureInfo.InvariantCulture));
    }
}
