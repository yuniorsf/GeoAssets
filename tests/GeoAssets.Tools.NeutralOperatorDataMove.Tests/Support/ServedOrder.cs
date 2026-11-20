using System.Text.Json;
using GeoAssets.NeutralOperator.Server.Tests.Support;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>What a NeutralOperator.Server host answers for one order on the chassis' read routes, as the response bodies.</summary>
internal sealed record ServedOrder(string OrderId, IReadOnlyDictionary<string, string> Bodies)
{
    public static readonly string[] Routes = ["", "/targets", "/occupancy-changes", "/children"];

    public static async Task<IReadOnlyList<ServedOrder>> ReadAsync(NeutralOperatorServerHost host, IEnumerable<string> orderIds)
    {
        using var client = host.ClientFor("owner-admin");
        var served = new List<ServedOrder>();
        foreach (var id in orderIds)
        {
            var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var route in Routes)
            {
                using var response = await client.GetAsync(new Uri($"/api/workflow/service-orders/{id}{route}", UriKind.Relative)).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Assert.True(response.IsSuccessStatusCode, $"GET {id}{route} answered {(int)response.StatusCode}: {body}");
                bodies[route] = body;
            }

            served.Add(new ServedOrder(id, bodies));
        }

        return served;
    }

    public JsonElement Order => JsonDocument.Parse(Bodies[string.Empty]).RootElement.Clone();
}
