using System.ComponentModel.DataAnnotations;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class MunicipioForm
{
    [Parameter] public Guid? MunicipioId { get; set; }
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public EventCallback OnDeleted { get; set; }

    private sealed class MunicipioEditModel
    {
        [Required] public string IbgeCode { get; set; } = string.Empty;
        [Required] public string Name { get; set; } = string.Empty;
        [Required] public string Uf { get; set; } = string.Empty;
        public Guid DistribuidoraOrganizationId { get; set; }
        public Guid? PrefeituraOrganizationId { get; set; }
        public OrigemWorkflow OrigemLevantamento { get; set; } = OrigemWorkflow.Planilha;
        public OrigemWorkflow OrigemFiscalizacao { get; set; } = OrigemWorkflow.Planilha;
        public OrigemWorkflow OrigemOficio { get; set; } = OrigemWorkflow.Planilha;
        public OrigemWorkflow OrigemMemoriaCalculo { get; set; } = OrigemWorkflow.Planilha;
    }

    private MunicipioEditModel _editModel = new();
    private Municipio? _municipio;
    private List<Organization> _organizations = [];
    private bool _loading = true;
    private string? _error;
    private Guid? _loadedMunicipioId;

    protected override async Task OnInitializedAsync()
    {
        _organizations = [.. await OrganizationRepository.GetAllAsync()];
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsNew)
        {
            _loadedMunicipioId = null;
            _municipio = null;
            _editModel = new MunicipioEditModel();
            _loading = false;
            return;
        }

        if (MunicipioId is null || MunicipioId == _loadedMunicipioId) return;

        _loading = true;
        StateHasChanged();

        _loadedMunicipioId = MunicipioId;
        _municipio = await Repository.GetByIdAsync(MunicipioId.Value);
        if (_municipio is not null)
        {
            _editModel = new MunicipioEditModel
            {
                IbgeCode = _municipio.IbgeCode,
                Name = _municipio.Name,
                Uf = _municipio.Uf,
                DistribuidoraOrganizationId = _municipio.DistribuidoraOrganizationId,
                PrefeituraOrganizationId = _municipio.PrefeituraOrganizationId,
                OrigemLevantamento = _municipio.OrigemLevantamento,
                OrigemFiscalizacao = _municipio.OrigemFiscalizacao,
                OrigemOficio = _municipio.OrigemOficio,
                OrigemMemoriaCalculo = _municipio.OrigemMemoriaCalculo,
            };
        }
        _loading = false;
    }

    private void OnDistribuidoraChanged(ChangeEventArgs e)
    {
        var value = e.Value as string;
        if (!string.IsNullOrEmpty(value) && Guid.TryParse(value, out var id))
            _editModel.DistribuidoraOrganizationId = id;
    }

    private void OnPrefeituraChanged(ChangeEventArgs e)
    {
        var value = e.Value as string;
        _editModel.PrefeituraOrganizationId = string.IsNullOrEmpty(value) ? null : Guid.Parse(value);
    }

    private async Task HandleSave()
    {
        _error = null;

        try
        {
            if (IsNew)
            {
                var municipio = new Municipio
                {
                    IbgeCode = _editModel.IbgeCode,
                    DistribuidoraOrganizationId = _editModel.DistribuidoraOrganizationId,
                };
                ApplyEditModel(municipio);
                await Repository.AddAsync(municipio);
                await Repository.SaveChangesAsync();
                await OnSaved.InvokeAsync(municipio.Id);
            }
            else if (_municipio is not null)
            {
                ApplyEditModel(_municipio);
                await Repository.UpdateAsync(_municipio);
                await Repository.SaveChangesAsync();
                await OnSaved.InvokeAsync(_municipio.Id);
            }
        }
        catch (Exception ex)
        {
            // A duplicate IBGE code or similar server-side rejection must not take down the
            // form — surface it so the admin can correct the fields and retry.
            _error = L.GetString("admin.ipBilling.municipios.saveError", ex.Message);
        }
    }

    private void ApplyEditModel(Municipio municipio)
    {
        municipio.IbgeCode = _editModel.IbgeCode;
        municipio.Name = _editModel.Name;
        municipio.Uf = _editModel.Uf;
        municipio.DistribuidoraOrganizationId = _editModel.DistribuidoraOrganizationId;
        municipio.PrefeituraOrganizationId = _editModel.PrefeituraOrganizationId;
        municipio.OrigemLevantamento = _editModel.OrigemLevantamento;
        municipio.OrigemFiscalizacao = _editModel.OrigemFiscalizacao;
        municipio.OrigemOficio = _editModel.OrigemOficio;
        municipio.OrigemMemoriaCalculo = _editModel.OrigemMemoriaCalculo;
    }

    private async Task HandleDelete()
    {
        if (_municipio is null) return;

        _error = null;
        try
        {
            await Repository.DeleteAsync(_municipio.Id);
            await Repository.SaveChangesAsync();
            await OnDeleted.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.municipios.saveError", ex.Message);
        }
    }

    private Task Cancel() => OnCancel.InvokeAsync();
}
