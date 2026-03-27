namespace GeoAssets.Commands.Contracts;

/// <summary>Uniform return value from all GIS command handlers.</summary>
public sealed record GeoCommandResult(
    bool    Success,
    object? Data  = null,
    string? Error = null)
{
    public static GeoCommandResult Ok(object data)      => new(true,  Data: data);
    public static GeoCommandResult Fail(string message) => new(false, Error: message);
}
