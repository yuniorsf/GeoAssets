using GeoAssets.Core.Navigation;
using GeoAssets.Shared.Components.Projects;

namespace GeoAssets.Shared.Navigation;

/// <summary>Opens the <see cref="ProjectPanel"/> panel (XD01-145). Sorted ahead of the
/// "management" section (<see cref="ManagementSectionItem"/>, SortOrder 10) — which Project is
/// open governs the whole session, so it sits with <see cref="OverviewMenuItem"/> rather than
/// among the per-Project management concerns (Layers/Assets/Collections).</summary>
public sealed class ProjectsMenuItem : MenuPanelItem
{
    public override string Id => "projects";
    public override string LabelKey => "projects.title";
    public override string? Icon => "📁";
    public override int SortOrder => 5;
    public override Type ComponentType => typeof(ProjectPanel);
}
