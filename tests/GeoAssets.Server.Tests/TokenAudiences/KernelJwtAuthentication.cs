using Microsoft.Extensions.Configuration;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>The kernel's <c>AzureAdCiam</c> configuration of a test: its own <c>ClientId</c> and the <c>AdditionalAudiences</c> it accepts.</summary>
internal sealed record KernelJwtAuthentication(IReadOnlyList<string?> AdditionalAudiences)
{
    public IConfiguration Configuration()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AzureAdCiam:Instance"] = "https://contoso.ciamlogin.com/",
            ["AzureAdCiam:TenantId"] = "00000000-0000-4000-8000-000000000390",
            ["AzureAdCiam:ClientId"] = LocalJwt.KernelAudience,
        };
        for (var i = 0; i < AdditionalAudiences.Count; i++)
            values[$"AzureAdCiam:{KernelAudiences.AdditionalAudiencesKey}:{i}"] = AdditionalAudiences[i];

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
