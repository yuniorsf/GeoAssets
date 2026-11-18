using GeoAssets.Hosting.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Hosting.Abstractions.Tests;

internal sealed class AsyncRecordingModule(string id, List<string> calls, List<CancellationToken>? tokens = null)
    : RecordingModule(id, calls), IModuleServiceInitializer, IModuleMiddleware
{
    private readonly List<string> _calls = calls;

    public async Task ConfigureServicesAsync(IServiceCollection services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await Task.Yield();
        _calls.Add($"configure-async:{Id}");
        tokens?.Add(cancellationToken);
    }

    public void UseMiddleware(IApplicationBuilder app) => _calls.Add($"middleware:{Id}");
}
