using System.Globalization;
using FluentAssertions;
using GeoAssets.IpBilling.UI.Components;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.IpBilling;

/// <summary>How <see cref="InspectionPostingSummary"/> prints a posted kWh figure (XD01-301).</summary>
public class InspectionPostingSummaryTests
{
    private static string FormatIn(string culture, decimal kwh)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return InspectionPostingSummary.FormatKwh(kwh);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatKwh_AlwaysShowsTheLedgersFourDecimals()
    {
        FormatIn("en-US", 404.6545m).Should().Be("404.6545");
        FormatIn("en-US", 12m).Should().Be("12.0000");
        FormatIn("en-US", 0m).Should().Be("0.0000");
    }

    [Fact]
    public void FormatKwh_UsesTheCurrentCulturesSeparators()
    {
        FormatIn("pt-BR", 1234.5m).Should().Be("1.234,5000");
        FormatIn("en-US", 1234.5m).Should().Be("1,234.5000");
    }

    [Fact]
    public void FormatKwh_ACreditStaysNegative() =>
        FormatIn("en-US", -47.4668m).Should().Be("-47.4668");
}
