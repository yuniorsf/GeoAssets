using GeoAssets.Hosting.Abstractions;

namespace GeoAssets.Hosting.Abstractions.Tests;

internal sealed class SeedingRecordingModule(string id, List<string> seedLog, List<CancellationToken>? tokens = null)
    : RecordingModule(id), IModuleSeeder
{
    public Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        seedLog.Add($"seed:{Id}");
        tokens?.Add(cancellationToken);
        return Task.CompletedTask;
    }
}
