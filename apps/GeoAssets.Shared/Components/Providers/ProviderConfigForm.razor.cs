using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace GeoAssets.Shared.Components.Providers;

public partial class ProviderConfigForm
{
    [Parameter, EditorRequired] public IProviderPlugin Plugin { get; set; } = null!;
    [Parameter, EditorRequired] public ProviderConfig Config { get; set; } = null!;

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
        Config.Get(f.Key, f.DefaultValue ?? string.Empty) == "true";

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
}
