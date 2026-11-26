using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Ingestion.Validation;

/// <summary>A non-blocking rule over an element's resolved references: it reports issues, the element is still imported.</summary>
internal interface IReferenceRule
{
    void Check(ReferenceRuleContext context, IssueLog issues);
}
