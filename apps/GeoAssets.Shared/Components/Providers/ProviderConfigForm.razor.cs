using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Providers;

public partial class ProviderConfigForm
{
    [Parameter, EditorRequired] public IProviderPlugin Plugin { get; set; } = null!;
    [Parameter, EditorRequired] public ProviderConfig Config { get; set; } = null!;

    // Only registered on MAUI (a browser <input type=file> never exposes a real filesystem path)
    // — resolved optionally via IServiceProvider, same defensive pattern as
    // ProviderPoolPanel.Session/ShapefileExporter, so this form still renders the plain
    // InputFile on Web rather than wiring to a service that isn't there.
    private INativeFilePicker? NativePicker => Services.GetService<INativeFilePicker>();

    protected override void OnParametersSet()
    {
        foreach (var f in Plugin.ConfigFields.Where(f => f.DefaultValue is not null && !Config.Has(f.Key)))
            Config.Set(f.Key, f.DefaultValue!);
    }

    private string GetValue(ProviderConfigField f) =>
        Config.Get(f.Key, f.DefaultValue ?? string.Empty);

    private void SetValue(ProviderConfigField f, ChangeEventArgs e) =>
        Config.Set(f.Key, e.Value?.ToString() ?? string.Empty);

    private bool IsChecked(ProviderConfigField f) =>
        string.Equals(Config.Get(f.Key, f.DefaultValue ?? string.Empty), "true", StringComparison.Ordinal);

    private void SetChecked(ProviderConfigField f, ChangeEventArgs e) =>
        Config.Set(f.Key, (bool)(e.Value ?? false) ? "true" : "false");

    private async Task HandleFileAsync(InputFileChangeEventArgs ev, string key)
    {
        using var stream = ev.File.OpenReadStream(maxAllowedSize: 100 * 1024 * 1024);
        using var reader = new System.IO.StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        Config.Set(key + "_content", content);
        Config.Set(key + "_name", ev.File.Name);
    }

    private async Task HandleBinaryFileAsync(InputFileChangeEventArgs ev, string key)
    {
        using var stream = ev.File.OpenReadStream(maxAllowedSize: 100 * 1024 * 1024);
        using var ms = new System.IO.MemoryStream();
        await stream.CopyToAsync(ms);
        Config.Set(key + "_content", Convert.ToBase64String(ms.ToArray()));
        Config.Set(key + "_name", ev.File.Name);
    }

    /// <summary>
    /// MAUI-only counterpart to <see cref="HandleBinaryFileAsync"/> — also captures the picked
    /// file's real full path under <c>&lt;key&gt;_path</c>, which survives into
    /// <see cref="ProviderConfig.ToReconnectValues"/> (unlike <c>_content</c>) so a plugin can
    /// reopen the file directly from disk on a later reconnect instead of requiring a re-pick.
    /// </summary>
    private async Task PickNativeBinaryFileAsync(string key)
    {
        var result = await NativePicker!.PickZipArchiveAsync();
        if (result is null) return;

        Config.Set(key + "_content", Convert.ToBase64String(result.Content));
        Config.Set(key + "_name", result.FileName);
        Config.Set(key + "_path", result.FullPath);
    }
}
