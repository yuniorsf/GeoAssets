using GeoAssets.Workflow.Persistence;

namespace GeoAssets.FieldService.Server;

internal sealed record InspectionCommitResponse(string ServiceOrderId, decimal TotalUs, decimal TotalBrl, int PostingCount, Guid OutboxMessageId)
{
    public static InspectionCommitResponse FromDomain(InspectionCommitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(result.ServiceOrderId, result.TotalUs, result.TotalBrl, result.Postings.Count, result.OutboxMessageId);
    }
}
