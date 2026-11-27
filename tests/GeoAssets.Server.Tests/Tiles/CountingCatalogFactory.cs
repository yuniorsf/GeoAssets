using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>Counts the organization catalog loads (<c>WarmCacheAsync</c> behind the PostgreSQL factory).</summary>
internal sealed class CountingCatalogFactory(IAssetProvider catalog) : IOrganizationCatalogFactory
{
    private int _warmed;

    public int Warmed => Volatile.Read(ref _warmed);

    public IAssetProvider GetForOrganization(Guid organizationId) => catalog;

    public ValueTask<IAssetProvider> GetForOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        Interlocked.Increment(ref _warmed);
        return ValueTask.FromResult(catalog);
    }
}
