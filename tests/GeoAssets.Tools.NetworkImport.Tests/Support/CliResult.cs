namespace GeoAssets.Tools.NetworkImport.Tests.Support;

internal sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public string Output => Stdout + Stderr;
}
