using System.Globalization;
using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Projection;
using GeoAssets.Ingestion.Resolution;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Validation;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// Pass 2 for one record: map, de-duplicate, validate the shape, resolve references, run the reference rules,
/// reproject, assemble edges and project. Answers null when the record is skipped (the reason is in the issues).
/// </summary>
internal sealed class ElementProcessor(
    ElementRegistry registry,
    ElementShapeValidator validator,
    ElementReferenceResolver resolver,
    IReadOnlyList<IReferenceRule> rules,
    TopologyAssembler topology,
    GeoFeatureProjector projector,
    ImportStatistics statistics,
    IssueLog issues)
{
    public GeoFeature? Process(RawRecord record, ActiveLayer layer)
    {
        var element = Map(record, layer);
        if (element is null)
        {
            return null;
        }

        var id = FeatureIdentity.For(element.Key);
        if (!registry.TryMarkSeen(id.Value))
        {
            issues.Add(IssueSeverity.Warning, ImportIssueCodes.DuplicateElement, element.Key.ToString(), "An element with this key was already read in this run; this one is skipped.");
            return null;
        }

        var validated = validator.Validate(element, issues);
        if (validated is null)
        {
            return null;
        }

        var definition = layer.Plan.Definition;
        var resolved = resolver.ResolveAll(element, definition);
        var context = new ReferenceRuleContext(validated, definition, resolved);
        foreach (var rule in rules)
        {
            rule.Check(context, issues);
        }

        var geographic = Reprojection.ToGeographic(layer.Transformer, validated.NativeGeometry);
        var edges = topology.Assemble(id, element.Key, resolved);
        var feature = projector.Project(new ProjectionInput(
            validated,
            id,
            layer.Plan.Scope,
            geographic,
            CompletenessLabels.Effective(element.Completeness, resolved),
            layer.Transformer.Accuracy,
            edges));

        registry.MarkProjected(id.Value);
        statistics.CountElement(validated.AssetClass.Name);
        return feature;
    }

    private CanonicalElement? Map(RawRecord record, ActiveLayer layer)
    {
        var mapping = layer.Mapper.Map(record, layer.Plan.Scope);
        if (mapping.Element is not null)
        {
            return mapping.Element;
        }

        issues.Add(mapping.Issue ?? new ImportIssue(
            IssueSeverity.Error,
            ImportIssueCodes.UnmappedRecord,
            string.Concat(layer.Plan.Scope, "#", record.RecordNumber.ToString(CultureInfo.InvariantCulture)),
            "The profile did not map this record."));
        return null;
    }
}
