using GeoAssets.IpBilling.Oficio;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>
/// Unlike <c>FiscalizacaoUploadPanel</c>, this also carries a <c>NumeroPrefeitura</c> field
/// (document-level metadata, not a spreadsheet column — see <c>OficioWorkbookRow</c>'s doc
/// comment, XD01-186) and handles the duplicate-número conflict path (XD01-185): on a 409, the
/// uploaded <see cref="IBrowserFile"/> reference is kept so "Supersede and retry" can re-open
/// its stream and resubmit without asking the user to re-pick the file.
/// </summary>
public partial class OficioUploadPanel
{
    [Parameter, EditorRequired] public string OrderId { get; set; } = string.Empty;
    [Parameter] public EventCallback OnUploaded { get; set; }

    private string _numeroPrefeitura = string.Empty;
    private bool _uploading;
    private int? _successCount;
    private string? _genericError;
    private List<OficioUploadRowError> _rowErrors = [];
    private OficioDuplicateNumeroRejectedException? _duplicateConflict;
    private IBrowserFile? _pendingFile;

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs e)
    {
        _pendingFile = e.File;
        await UploadAsync(e.File);
    }

    private async Task HandleSupersedeAsync()
    {
        if (_duplicateConflict is null || _pendingFile is null) return;

        _uploading = true;
        StateHasChanged();

        try
        {
            await Client.SupersedeAsync(_duplicateConflict.ExistingServiceOrderId, performedBy: null);
            _duplicateConflict = null;
            await UploadAsync(_pendingFile);
        }
        catch (Exception ex)
        {
            _genericError = ex.Message;
            _uploading = false;
            StateHasChanged();
        }
    }

    private async Task UploadAsync(IBrowserFile file)
    {
        _uploading = true;
        _successCount = null;
        _genericError = null;
        _rowErrors = [];
        _duplicateConflict = null;
        StateHasChanged();

        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024);
            var numeroPrefeitura = string.IsNullOrWhiteSpace(_numeroPrefeitura) ? null : _numeroPrefeitura;
            var count = await Client.UploadAsync(OrderId, numeroPrefeitura, stream, file.Name);
            _successCount = count;
            _pendingFile = null;
            await OnUploaded.InvokeAsync();
        }
        catch (OficioDuplicateNumeroRejectedException ex)
        {
            _duplicateConflict = ex;
        }
        catch (OficioUploadRejectedException ex)
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
