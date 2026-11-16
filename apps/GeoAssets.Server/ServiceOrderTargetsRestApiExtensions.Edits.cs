using System.Text.Json;
using GeoAssets.Server.Targets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server;

/// <content>Editing one target (R7) and removing targets (R8).</content>
public static partial class ServiceOrderTargetsRestApiExtensions
{
    /// <summary>Longest note the target store accepts.</summary>
    private const int MaxNoteLength = 2048;

    private static void MapTargetUpdate(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPatch($"{prefix}/service-orders/{{id}}/targets/{{targetId}}", async (
            string id, string targetId, HttpRequest request,
            IServiceOrderRepository orders, OrderTypeRegistry orderTypes,
            IServiceOrderTargetReader reader, IServiceOrderTargetWriter writer,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var (body, raw, bodyProblem) = await OrderTargetEndpointSupport
                .ReadBodyAsync<UpdateTargetRequest>(request, opts, ct).ConfigureAwait(false);
            if (bodyProblem is not null) return bodyProblem;
            if (!raw.TryGetProperty("version", out _))
                return WorkflowProblemResults.Validation("version is required.");

            var access = await OrderTargetEndpointSupport.ForEditAsync(id, orders, rules, principalFactory, ct).ConfigureAwait(false);
            if (access.Problem is not null) return access.Problem;

            var current = await reader.GetByIdAsync(id, targetId, ct).ConfigureAwait(false);
            if (current is null) return WorkflowProblemResults.TargetNotFound(id, targetId);

            var catalog = orderTypes.Find(access.Order!.OrderTypeId)?.TargetPolicy?.Activities ?? [];
            var edit = ApplyEdit(current, body!, noteSupplied: raw.TryGetProperty("note", out _), catalog);
            if (edit.Problem is not null) return edit.Problem;

            try
            {
                await writer.UpdateAsync(edit.Target! with { Version = body!.Version }, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ServiceOrderConcurrencyException)
            {
                return WorkflowProblemResults.ConcurrentModification(id);
            }
            catch (KeyNotFoundException)
            {
                return WorkflowProblemResults.TargetNotFound(id, targetId);
            }
        });
    }

    /// <summary>
    /// Applies the requested changes to the stored target through the domain's own guards: the
    /// activity ids must come from the order type's catalog, and the status must be a move the
    /// target's lifecycle allows. Returns the edited target, or the problem to send.
    /// </summary>
    private static (ServiceOrderTarget? Target, IResult? Problem) ApplyEdit(
        ServiceOrderTarget current, UpdateTargetRequest body, bool noteSupplied, IReadOnlyList<OrderTargetActivity> catalog)
    {
        if (noteSupplied && body.Note is { Length: > MaxNoteLength })
            return (null, WorkflowProblemResults.Validation($"note must be at most {MaxNoteLength} characters."));

        try
        {
            var edited = current;
            if (body.ActivityIds is not null) edited = edited.WithActivities(body.ActivityIds, catalog);
            if (body.Status is not null) edited = edited.WithStatus(body.Status);
            if (noteSupplied) edited = edited with { Note = body.Note };

            return (edited, null);
        }
        catch (ArgumentException ex)
        {
            return (null, WorkflowProblemResults.Validation(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return (null, WorkflowProblemResults.Validation(ex.Message));
        }
    }

    private static void MapTargetRemove(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders/{{id}}/targets/remove", async (
            string id, HttpRequest request, IServiceOrderRepository orders, IServiceOrderTargetWriter writer,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var (body, _, bodyProblem) = await OrderTargetEndpointSupport
                .ReadBodyAsync<RemoveTargetsRequest>(request, opts, ct).ConfigureAwait(false);
            if (bodyProblem is not null) return bodyProblem;
            if (OrderTargetEndpointSupport.CheckIds(body!.TargetIds, "targetIds") is { } idsProblem) return idsProblem;

            var access = await OrderTargetEndpointSupport.ForEditAsync(id, orders, rules, principalFactory, ct).ConfigureAwait(false);
            if (access.Problem is not null) return access.Problem;

            var removed = await writer.RemoveAsync(id, body.TargetIds!, ct).ConfigureAwait(false);
            return Results.Json(new RemoveTargetsResponse(removed), opts);
        });
    }
}
