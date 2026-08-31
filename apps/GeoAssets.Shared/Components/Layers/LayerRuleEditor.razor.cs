using GeoAssets.Core.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Layers;

public partial class LayerRuleEditor
{
    [Parameter, EditorRequired] public AssetType AssetType { get; set; } = null!;

    private bool _addingNew;
    private Guid? _newRuleLayerId;
    private int _newRulePriority;
    private readonly List<LayerRuleCondition> _newRuleConditions = [];

    protected override void OnParametersSet() =>
        _newRulePriority = Repository.GetLayerRules(AssetType.Id).Count;

    private string LayerName(Guid layerId) =>
        Repository.GetLayers().FirstOrDefault(l => l.Id == layerId)?.Name ?? L["map.layers.rules.none"];

    private static string OperatorSymbol(LayerRuleOperator op) => op switch
    {
        LayerRuleOperator.Equals => "=",
        LayerRuleOperator.NotEquals => "≠",
        LayerRuleOperator.GreaterThanOrEqual => "≥",
        LayerRuleOperator.LessThanOrEqual => "≤",
        _ => op.ToString()
    };

    private void OnNewRuleLayerChanged(string? value) =>
        _newRuleLayerId = string.IsNullOrEmpty(value) ? null : Guid.Parse(value);

    private void AddCondition() => _newRuleConditions.Add(new LayerRuleCondition());

    private void RemoveCondition(LayerRuleCondition condition) => _newRuleConditions.Remove(condition);

    private void SaveNewRule()
    {
        if (_newRuleLayerId is not { } layerId) return;

        var rule = new LayerRule
        {
            AssetTypeId = AssetType.Id,
            LayerId = layerId,
            Priority = _newRulePriority,
            Conditions = [.. _newRuleConditions]
        };
        Repository.AddLayerRule(rule);
        ResetForm();
    }

    private void ResetForm()
    {
        _newRuleLayerId = null;
        _newRuleConditions.Clear();
        _newRulePriority = Repository.GetLayerRules(AssetType.Id).Count;
        _addingNew = false;
    }

    private void DeleteRule(LayerRule rule) => Repository.DeleteLayerRule(rule.Id);

    private void MoveUp(LayerRule rule) => Reprioritize(rule, -1);
    private void MoveDown(LayerRule rule) => Reprioritize(rule, 1);

    /// <summary>
    /// Swaps <paramref name="rule"/>'s <see cref="LayerRule.Priority"/> with its neighbor in
    /// <paramref name="direction"/> (-1 = up/earlier, +1 = down/later). <see cref="IAssetProvider"/>
    /// has no update method for <see cref="LayerRule"/>, so persisting the swap is a delete + re-add
    /// with the same Id for each of the two rules — safe here since both objects (and their
    /// <see cref="LayerRule.Conditions"/>) are already fully in hand, no partial-update merge needed.
    /// </summary>
    private void Reprioritize(LayerRule rule, int direction)
    {
        var ordered = Repository.GetLayerRules(AssetType.Id).OrderBy(r => r.Priority).ToList();
        var index = ordered.FindIndex(r => r.Id == rule.Id);
        var swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= ordered.Count) return;

        var other = ordered[swapIndex];
        (rule.Priority, other.Priority) = (other.Priority, rule.Priority);

        Repository.DeleteLayerRule(rule.Id);
        Repository.AddLayerRule(rule);
        Repository.DeleteLayerRule(other.Id);
        Repository.AddLayerRule(other);
    }
}
