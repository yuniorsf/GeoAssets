namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// Developer tool (XD01-317): imports the legacy Geos <c>SharedUses</c> table into pole occupancy
/// and writes a reconciliation report. See <see cref="SharedUseImportOptions.Usage"/>.
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        var application = new SharedUseImportApplication(
            options => PostgresOccupancyStore.Open(options, TimeProvider.System),
            TimeProvider.System,
            Console.Out,
            Console.Error);
        return application.RunAsync(
            args,
            Environment.GetEnvironmentVariable(SharedUseImportOptions.ConnectionStringVariable),
            CancellationToken.None);
    }
}
