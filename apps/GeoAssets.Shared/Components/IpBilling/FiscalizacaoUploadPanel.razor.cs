using GeoAssets.IpBilling.Fiscalizacao;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class FiscalizacaoUploadPanel
{
    [Parameter, EditorRequired] public string OrderId { get; set; } = string.Empty;
    [Parameter] public EventCallback OnUploaded { get; set; }

    private bool _uploading;
    private int? _successCount;
    private string? _genericError;
    private List<FiscalizacaoUploadRowError> _rowErrors = [];

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs e)
    {
        _uploading = true;
        _successCount = null;
        _genericError = null;
        _rowErrors = [];
        StateHasChanged();

        try
        {
            using var stream = e.File.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024);
            var count = await Client.UploadAsync(OrderId, stream, e.File.Name);
            _successCount = count;
            await OnUploaded.InvokeAsync();
        }
        catch (FiscalizacaoUploadRejectedException ex)
        {
            _rowErrors = [.. ex.RowErrors];
            _genericError = ex.Message;
        }
        catch (Exception ex)
        {
            _genericError = ex.Message;
        }
        finally
        {
            _uploading = false;
            StateHasChanged();
        }
    }
}
