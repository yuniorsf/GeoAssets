using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.UI.Services.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.NeutralOperator.UI.Components.ServiceOrders;

/// <summary>
/// Field editor for the occupancy of one order target's pole (XD01-315): shows the pole's six
/// regulated slots and its entries beyond them, lets the crew put together one change per slot
/// or entry — add, modify, remove, mark unidentified, add beyond the regulated slots — with a
/// live preview of the result, and stages or discards them on the order. Nothing here changes
/// the pole itself; that happens when the order is approved.
///
/// The working copy and every rule live in <see cref="OccupancyChangeDraft"/> and
/// <see cref="OccupancyEntryForm"/>; this class only wires them to the markup and to
/// <see cref="IOrderOccupancyStagingService"/>.
/// </summary>
public partial class OccupancyChangeEditor
{
    private readonly string _idPrefix = $"occ-{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _lifetime = new();

    private string? _loadedKey;
    private TargetOccupancyView? _view;
    private OccupancyChangeDraft? _draft;
    private IReadOnlyList<OccupancyDraftRow> _rows = [];
    private OccupancyProjection? _preview;
    private string? _previewErrorKey;
    private string? _loadErrorKey;
    private string? _actionErrorKey;
    private bool _busy;

    private OccupancyEntryForm? _form;
    private string? _formRowKey;
    private int? _formSlotIndex;
    private bool _formAdds;
    private string _formLabel = string.Empty;
    private IReadOnlyDictionary<string, string> _formErrors = new Dictionary<string, string>(StringComparer.Ordinal);

    [Parameter, EditorRequired] public string OrderId { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string TargetId { get; set; } = string.Empty;
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;

    /// <summary>What to call the pole in the heading (its alias); the pole's feature id when omitted.</summary>
    [Parameter] public string? TargetLabel { get; set; }

    /// <summary>False shows the pole and what is staged without any editing control.</summary>
    [Parameter] public bool CanEdit { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Raised after changes were staged or discarded, with the target's view afterwards.</summary>
    [Parameter] public EventCallback<TargetOccupancyView> OnChanged { get; set; }

    private bool CanSave => !_busy && _form is null && _draft is { IsEmpty: false, IsDirty: true } && _preview is not null;

    protected override async Task OnParametersSetAsync()
    {
        var key = $"{OrderId}\n{TargetId}";
        if (string.Equals(key, _loadedKey, StringComparison.Ordinal)) return;

        _loadedKey = key;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        (_view, _draft, _loadErrorKey, _actionErrorKey) = (null, null, null, null);
        CancelForm();

        try
        {
            Show(await Staging.GetAsync(OrderId, TargetId, Principal, _lifetime.Token));
        }
        catch (OperationCanceledException)
        {
            // The component went away while loading.
        }
        catch (Exception ex)
        {
            _loadErrorKey = OccupancyEditorMessages.ErrorKey(ex);
        }
    }

    private void Show(TargetOccupancyView view)
    {
        _view = view;
        _draft = new OccupancyChangeDraft(view);
        Refresh();
    }

    private void Refresh()
    {
        if (_draft is null) return;

        _rows = _draft.Rows();
        _draft.TryPreview(out _preview, out _previewErrorKey);
    }

    // ── Row actions ───────────────────────────────────────────────────────────

    private void BeginAdd(OccupancyDraftRow row) => OpenForm(new OccupancyEntryForm(), row.Key, row.SlotIndex, adds: true, RowLabel(row));

    private void BeginAddBeyondRegulatedSlots() =>
        OpenForm(new OccupancyEntryForm(), rowKey: null, slotIndex: null, adds: true, L["occupancy.table.beyond"]);

    private void BeginEdit(OccupancyDraftRow row)
    {
        // A pending Add or Modify is edited from its own payload — also when the draft does not
        // apply at the moment, which is exactly when the user needs to correct it.
        var form = row.Change is { Kind: OccupancyChangeKind.Add or OccupancyChangeKind.Modify } pending
            ? OccupancyEntryForm.From(pending)
            : row.Current is { } entry ? OccupancyEntryForm.From(entry) : null;
        if (form is null) return;

        OpenForm(form, row.Key, row.SlotIndex, adds: false, RowLabel(row));
    }

    private void Remove(OccupancyDraftRow row) => Edit(draft => draft.Remove(row.Key));

    private void MarkUnidentified(OccupancyDraftRow row) => Edit(draft => draft.MarkUnidentified(row.Key));

    private void Undo(OccupancyDraftRow row) => Edit(draft => draft.Undo(row.Key));

    private void Reset()
    {
        if (_view is not null) Show(_view);
        CancelForm();
    }

    private void Edit(Action<OccupancyChangeDraft> change)
    {
        if (_draft is null) return;

        _actionErrorKey = null;
        change(_draft);
        Refresh();
    }

    // ── Entry form ────────────────────────────────────────────────────────────

    private void OpenForm(OccupancyEntryForm form, string? rowKey, int? slotIndex, bool adds, string label)
    {
        (_form, _formRowKey, _formSlotIndex, _formAdds, _formLabel) = (form, rowKey, slotIndex, adds, label);
        _formErrors = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void CancelForm()
    {
        (_form, _formRowKey, _formSlotIndex, _formAdds) = (null, null, null, false);
        _formErrors = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void ApplyForm()
    {
        if (_form is not { } form || _draft is null) return;

        _formErrors = form.Validate(beyondRegulatedSlots: _formSlotIndex is null);
        if (_formErrors.Count > 0) return;

        var (rowKey, slotIndex, adds) = (_formRowKey, _formSlotIndex, _formAdds);
        Edit(draft =>
        {
            if (!adds && rowKey is not null) draft.SetEntry(rowKey, form);
            else if (slotIndex is { } slot) draft.AddToSlot(slot, form, Guid.NewGuid());
            else draft.AddBeyondRegulatedSlots(form, Guid.NewGuid());
        });
        CancelForm();
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    private Task SaveAsync() => _draft is { } draft
        ? RunAsync(async ct => Show(await Staging.StageAsync(OrderId, TargetId, draft.Changes, Principal, ct)))
        : Task.CompletedTask;

    private Task DiscardAsync() => RunAsync(async ct =>
    {
        await Staging.DiscardAsync(OrderId, TargetId, Principal, ct);
        Show(await Staging.GetAsync(OrderId, TargetId, Principal, ct));
    });

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return;

        (_busy, _actionErrorKey) = (true, null);
        try
        {
            await action(_lifetime.Token);
            if (_view is not null) await OnChanged.InvokeAsync(_view);
        }
        catch (OperationCanceledException)
        {
            // The component went away mid-request.
        }
        catch (Exception ex)
        {
            _actionErrorKey = OccupancyEditorMessages.ErrorKey(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private Task CloseAsync() => OnClose.InvokeAsync();

    // ── Markup helpers ────────────────────────────────────────────────────────

    private string Id(string name) => $"{_idPrefix}-{name}";

    private string RowLabel(OccupancyDraftRow row) => row.SlotIndex is { } slotIndex
        ? L.GetString("occupancy.table.slotNumber", slotIndex)
        : L["occupancy.table.beyond"];

    private string? Invalid(string field) => _formErrors.ContainsKey(field) ? "is-invalid" : null;

    private string? AriaInvalid(string field) => _formErrors.ContainsKey(field) ? "true" : null;

    /// <summary>The ids describing a field: its error, when it has one, and its hint, when it has one.</summary>
    private string? DescribedBy(string field, string? hint = null)
    {
        var ids = new List<string>(2);
        if (_formErrors.ContainsKey(field)) ids.Add(Id($"{field}-error"));
        if (hint is not null) ids.Add(Id(hint));
        return ids.Count == 0 ? null : string.Join(' ', ids);
    }

    private RenderFragment? FieldError(string field) => _formErrors.TryGetValue(field, out var messageKey)
        ? builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "id", Id($"{field}-error"));
            builder.AddAttribute(2, "class", "invalid-feedback d-block");
            builder.AddContent(3, L[messageKey]);
            builder.CloseElement();
        }
        : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }
}
