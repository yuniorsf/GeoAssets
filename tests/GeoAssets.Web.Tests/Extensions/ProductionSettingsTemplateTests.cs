using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// XD01-384: <c>appsettings.Production.json.template</c> — rendered by the Web container's nginx (<c>envsubst</c>) at start —
/// holds a placeholder for every REST base URL the Web reads, the gateway's for all but IP billing's own, and no hardcoded host.
/// </summary>
public sealed class ProductionSettingsTemplateTests
{
    private const string GatewayUrl = "https://gateway.example.test";
    private const string IpBillingUrl = "https://ipbilling.example.test";
    private const string CiamAuthority = "https://tenant.ciamlogin.example.test/00000000-0000-0000-0000-000000000001";
    private const string CiamClientId = "00000000-0000-0000-0000-000000000002";
    private const string ApiScope = "api://00000000-0000-0000-0000-000000000003/access_as_user";
    private const string IpBillingApiScope = "api://00000000-0000-0000-0000-000000000004/access_as_user";

    private static readonly Regex Placeholder = new(@"\$\{(?<name>[A-Z_]+)\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
    private static readonly Regex ConfigurationRead = new("""Configuration\["(?<key>[A-Za-z:]+(?:BaseUrl))"\]""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output.");
    }

    private static string Template() => File.ReadAllText(Path.Combine(Root(), "apps", "GeoAssets.Web", "appsettings.Production.json.template"));

    private const string AppInsightsConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://ingest.example.test/";

    private static string Rendered(string appInsightsConnectionString = AppInsightsConnectionString) => Template()
        .Replace("${GEOASSETS_GATEWAY_URL}", GatewayUrl, StringComparison.Ordinal)
        .Replace("${GEOASSETS_IPBILLING_SERVER_URL}", IpBillingUrl, StringComparison.Ordinal)
        .Replace("${GEOASSETS_APPINSIGHTS_CONNECTION_STRING}", appInsightsConnectionString, StringComparison.Ordinal)
        .Replace("${GEOASSETS_CIAM_AUTHORITY}", CiamAuthority, StringComparison.Ordinal)
        .Replace("${GEOASSETS_CIAM_CLIENT_ID}", CiamClientId, StringComparison.Ordinal)
        .Replace("${GEOASSETS_API_SCOPE}", ApiScope, StringComparison.Ordinal)
        .Replace("${GEOASSETS_IPBILLING_API_SCOPE}", IpBillingApiScope, StringComparison.Ordinal);

    private static string ValueOf(JsonElement root, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var element = root;
        foreach (var part in key.Split(':')) element = element.GetProperty(part);
        return element.GetString()!;
    }

    [Fact]
    public void TheRenderedTemplate_IsValidJson_WithNoPlaceholderLeft()
    {
        var rendered = Rendered();

        var act = () => JsonDocument.Parse(rendered);

        act.Should().NotThrow();
        Placeholder.IsMatch(rendered).Should().BeFalse("every placeholder has a variable the container sets");
    }

    [Fact]
    public void TheTemplate_UsesOnlyTheDocumentedVariables()
    {
        Placeholder.Matches(Template()).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal)
            .Should().BeEquivalentTo(
                "GEOASSETS_GATEWAY_URL", "GEOASSETS_IPBILLING_SERVER_URL", "GEOASSETS_APPINSIGHTS_CONNECTION_STRING",
                "GEOASSETS_CIAM_AUTHORITY", "GEOASSETS_CIAM_CLIENT_ID", "GEOASSETS_API_SCOPE", "GEOASSETS_IPBILLING_API_SCOPE");
    }

    [Theory]
    [InlineData(AppInsightsConnectionString)]
    [InlineData("")]
    public void TheAppInsightsConnectionString_RendersFromItsVariable_BlankWhenUnset(string connectionString)
    {
        using var document = JsonDocument.Parse(Rendered(connectionString));

        ValueOf(document.RootElement, "Observability:AzureMonitor:ConnectionString").Should().Be(connectionString);
    }

    [Theory]
    [InlineData("AzureAdCiam:Authority", CiamAuthority)]
    [InlineData("AzureAdCiam:ClientId", CiamClientId)]
    [InlineData("GeoAssetsServer:ApiScope", ApiScope)]
    [InlineData("GeoAssetsIpBillingServer:ApiScope", IpBillingApiScope)]
    public void EachSignInSetting_RendersFromItsVariable(string key, string expected)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var document = JsonDocument.Parse(Rendered());

        ValueOf(document.RootElement, key).Should().Be(expected);
    }

    [Fact]
    public void TheTemplate_HasNoHardcodedHost()
    {
        var template = Template();

        template.Should().NotContain("http://").And.NotContain("https://").And.NotContainEquivalentOf("localhost").And.NotContain("YOUR_");
    }

    [Fact]
    public void EveryBaseUrlTheWebProgramReads_HasAKeyInTheTemplate_AnAbsoluteUrl_Once_Rendered()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "apps", "GeoAssets.Web", "Program.cs"));
        var keys = ConfigurationRead.Matches(program).Select(m => m.Groups["key"].Value).Distinct(StringComparer.Ordinal).ToList();
        using var document = JsonDocument.Parse(Rendered());

        keys.Should().Contain(["GeoAssetsServer:BaseUrl", "GeoAssetsIpBillingServer:BaseUrl", "ServiceOrders:ApiBaseUrl", "Projects:ApiBaseUrl"]);
        foreach (var key in keys)
        {
            var value = ValueOf(document.RootElement, key);
            Uri.TryCreate(value, UriKind.Absolute, out var uri).Should().BeTrue($"{key} must render to an absolute URL, was '{value}'");
            uri!.Scheme.Should().Be("https");
        }
    }

    [Theory]
    [InlineData("GeoAssetsServer:BaseUrl", GatewayUrl)]
    [InlineData("Api:BaseUrl", $"{GatewayUrl}/api/geoassets")]
    [InlineData("ServiceOrders:ApiBaseUrl", $"{GatewayUrl}/api/workflow")]
    [InlineData("Projects:ApiBaseUrl", $"{GatewayUrl}/api/projects")]
    [InlineData("GeoAssetsIpBillingServer:BaseUrl", IpBillingUrl)]
    public void EachBaseUrl_RendersToTheGatewayOrigin_AndItsKernelRoute(string key, string expected)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var document = JsonDocument.Parse(Rendered());

        ValueOf(document.RootElement, key).Should().Be(expected);
    }

    [Fact]
    public void TheWebContainer_RendersTheTemplate_FromItsEnvironment()
    {
        var dockerfile = File.ReadAllText(Path.Combine(Root(), "apps", "GeoAssets.Web", "Dockerfile"));

        dockerfile.Should().Contain("appsettings.Production.json.template /etc/nginx/templates/appsettings.Production.json.template");
        dockerfile.Should().Contain("NGINX_ENVSUBST_OUTPUT_DIR=/usr/share/nginx/html");
        dockerfile.Should().Contain("GEOASSETS_GATEWAY_URL=");
        foreach (var variable in Placeholder.Matches(Template()).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal))
            dockerfile.Should().Contain($"{variable}=", "envsubst leaves an unset variable's placeholder in place");
    }
}
