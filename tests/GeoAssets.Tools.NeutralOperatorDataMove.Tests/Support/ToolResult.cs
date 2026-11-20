namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>What one invocation of the tool printed and exited with.</summary>
internal sealed record ToolResult(int ExitCode, string Output, string Error)
{
    public string Text => Output + Error;
}
