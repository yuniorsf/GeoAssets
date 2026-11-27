using GeoAssets.Core.VectorFiles;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// One run of the tool: parse the verb and options (usage errors → 1), validate the inputs (→ 2), run the verb; kernel
/// failures after retries → 3. All output goes through <c>stdout</c>/<c>stderr</c>; the kernel is reached only
/// through <see cref="NetworkImportDependencies.Handler"/>.
/// </summary>
internal static class NetworkImportApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        var dependencies = new NetworkImportDependencies(handler, TimeProvider.System, Environment.GetEnvironmentVariable);
        return await RunAsync(args, stdout, stderr, dependencies, ct).ConfigureAwait(false);
    }

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, NetworkImportDependencies dependencies, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(dependencies);

        if (NetworkImportCommandLine.IsHelp(args))
        {
            await stdout.LineAsync(NetworkImportCommandLine.Usage, ct).ConfigureAwait(false);
            return NetworkImportExitCodes.Succeeded;
        }

        var (options, usageError) = NetworkImportCommandLine.Parse(args);
        if (options is null)
            return await UsageErrorAsync(stderr, usageError ?? "Invalid arguments.", ct).ConfigureAwait(false);

        IAccessTokenSource? tokens = null;
        if (options.Verb is NetworkImportVerb.Import or NetworkImportVerb.Verify)
        {
            (tokens, var tokenError) = AccessTokenSources.FromEnvironment(dependencies.GetEnvironmentVariable);
            if (tokens is null)
                return await UsageErrorAsync(stderr, tokenError ?? "No kernel credentials.", ct).ConfigureAwait(false);
        }

        try
        {
            return options.Verb switch
            {
                NetworkImportVerb.Inventory => await InventoryCommand.RunAsync(options, stdout, ct).ConfigureAwait(false),
                NetworkImportVerb.Plan => await new PipelineCommand(options, dependencies, stdout).PlanAsync(ct).ConfigureAwait(false),
                NetworkImportVerb.Import => await new PipelineCommand(options, dependencies, stdout).ImportAsync(tokens!, ct).ConfigureAwait(false),
                _ => await new VerifyCommand(options, dependencies, stdout).RunAsync(tokens!, ct).ConfigureAwait(false),
            };
        }
        catch (Exception exception) when (exception is NetworkImportInputException or DirectoryNotFoundException or FileNotFoundException
            or IOException or UnauthorizedAccessException or InvalidDataException or VectorFileFormatUnavailableException
            or VectorTileArchiveUnavailableException)
        {
            await stderr.LineAsync(exception.Message, ct).ConfigureAwait(false);
            return NetworkImportExitCodes.InputError;
        }
        catch (KernelFailureException exception)
        {
            await stderr.LineAsync(exception.Message, ct).ConfigureAwait(false);
            return NetworkImportExitCodes.KernelFailure;
        }
    }

    private static async Task<int> UsageErrorAsync(TextWriter stderr, string message, CancellationToken ct)
    {
        await stderr.LineAsync(message, ct).ConfigureAwait(false);
        await stderr.LineAsync(NetworkImportCommandLine.Usage, ct).ConfigureAwait(false);
        return NetworkImportExitCodes.UsageError;
    }
}
