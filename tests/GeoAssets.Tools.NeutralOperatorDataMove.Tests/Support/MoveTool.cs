namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>Runs the tool's real command line (the application over <see cref="DataMoveRunner"/>), as <c>Program</c> does.</summary>
internal static class MoveTool
{
    public static async Task<ToolResult> RunAsync(string kernel, string target, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new DataMoveApplication(DataMoveRunner.RunAsync, output, error);
        var exitCode = await application.RunAsync(args, kernel, target, CancellationToken.None).ConfigureAwait(false);
        return new ToolResult(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>The kernel and target databases for a test: migrated, the kernel seeded with <see cref="KernelSeed"/>.</summary>
    public static async Task<(string Kernel, string Target)> SeededPairAsync()
    {
        var kernel = await MoveDatabases.CreateKernelAsync().ConfigureAwait(false);
        var target = await MoveDatabases.CreateTargetAsync().ConfigureAwait(false);
        await KernelSeed.ApplyAsync(kernel).ConfigureAwait(false);
        return (kernel, target);
    }
}
