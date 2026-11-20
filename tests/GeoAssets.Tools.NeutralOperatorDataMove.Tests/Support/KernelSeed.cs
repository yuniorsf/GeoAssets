using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>
/// The NeutralOperator orders a kernel database holds when the cut-over is done, in the states the ticket names: created, a
/// pending approval saga with its staged asset-mutation batch, staged change sets, a closed order whose targets are
/// released, a parent with its shared-use child, an order of a type with no target policy — plus what must never move: a
/// kernel order, a remote vertical's ledger row, the kernel's own message history. Every row is written the way the kernel's
/// stores write it (the same tables, the same value shapes), with fixed ids so a test can name them.
/// </summary>
internal static class KernelSeed
{
    public const string Created = "no-created";
    public const string Approved = "no-approved";
    public const string Staged = "no-staged";
    public const string Closed = "no-closed";
    public const string Parent = "no-parent";
    public const string Child = "no-child";
    public const string KernelOrder = "kernel-inspection";
    public const string RemoteOrder = "fs-order-1";

    public const string PoleWork = "shared-use-pole-work";
    public const string OccupantNotice = "shared-use-occupant-notice";
    public const string TenantAttachment = "isp-tenant-attachment-request";

    public const string Batch = "11111111-1111-4111-8111-111111111111";

    /// <summary>The orders that move, parents first.</summary>
    public static readonly string[] Moved = [Created, Approved, Staged, Closed, Parent, Child];

    public static string Organization => TestIdentities.OwnerOrganization.ToString();

    /// <summary>The kernel-vertical ledger entry ids of each moved order's targets, by order.</summary>
    public static IReadOnlyDictionary<string, string[]> TargetIds { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [Approved] = ["t-approved-a", "t-approved-b"],
        [Staged] = ["t-staged-c"],
        [Closed] = ["t-closed-d"],
        [Parent] = ["t-parent-e"],
    };

    public static Task ApplyAsync(string kernelConnectionString) => Sql.ExecAsync(kernelConnectionString, Statements);

    private static string Statements => $$"""
        INSERT INTO "ServiceOrders" ("Id","Title","Description","OrderTypeId","Status","Priority","CreatedBy","AssignedTo","OrganizationId","CreatedAt","UpdatedAt","CompletedAt","ParentOrderId","AttributesJson","FeatureIdsJson","RowVersion") VALUES
          ('{{Created}}','Created order','A fresh pole-work order','{{PoleWork}}','Draft',1,'owner-admin',NULL,'{{Organization}}','2026-11-01T10:00:00.123456Z',NULL,NULL,NULL,'{}','[]','\x00'),
          ('{{Approved}}','Approved, saga pending','Approval awaiting the kernel''s apply','{{PoleWork}}','Pending',2,'owner-admin','owner-reader','{{Organization}}','2026-11-02T10:00:00Z','2026-11-03T09:30:00.5Z',NULL,NULL,'{"occupancy.apply.state":"awaiting-apply","note":"ação"}','["pole-a","pole-b"]','\x00'),
          ('{{Staged}}','Staged change sets','Change sets staged, order not approved','{{TenantAttachment}}','Draft',1,'owner-admin',NULL,'{{Organization}}','2026-11-04T10:00:00Z',NULL,NULL,NULL,'{}','["pole-c"]','\x00'),
          ('{{Closed}}','Closed order','Cancelled; its targets were released','{{PoleWork}}','Cancelled',3,'owner-admin',NULL,'{{Organization}}','2026-11-05T10:00:00Z','2026-11-06T10:00:00Z','2026-11-06T10:00:00Z',NULL,'{}','["pole-d"]','\x00'),
          ('{{Parent}}','Fan-out parent','Shared-use parent','{{PoleWork}}','InProgress',1,'owner-admin',NULL,'{{Organization}}','2026-11-07T10:00:00Z',NULL,NULL,NULL,'{}','["pole-e"]','\x00'),
          ('{{Child}}','Fan-out child','Occupant notice','{{OccupantNotice}}','Pending',1,'owner-admin',NULL,'{{Organization}}','2026-11-07T11:00:00Z',NULL,NULL,'{{Parent}}','{}','[]','\x00'),
          ('{{KernelOrder}}','A kernel order','Stays in the kernel','inspection','Pending',1,'owner-admin',NULL,'{{Organization}}','2026-11-08T10:00:00Z',NULL,NULL,NULL,'{}','["pole-z"]','\x00');

        INSERT INTO "OrderActionLogs" ("ServiceOrderId","Action","ActionCode","PerformedBy","PerformedAt","Comment","ResultingStatus","ActorKind") VALUES
          ('{{Created}}',NULL,'Create','owner-admin','2026-11-01T10:00:00Z','created','Draft',0),
          ('{{Approved}}',NULL,'Create','owner-admin','2026-11-02T10:00:00Z','created','Draft',0),
          ('{{KernelOrder}}',NULL,'Create','owner-admin','2026-11-08T10:00:00Z','kernel','Pending',0),
          ('{{Approved}}',NULL,'Submit','owner-admin','2026-11-02T11:00:00Z','submitted','Pending',0),
          ('{{Approved}}',NULL,'Approve','owner-admin','2026-11-03T09:30:00.5Z','approved; apply is staged',NULL,0),
          ('{{Staged}}',NULL,'Create','owner-admin','2026-11-04T10:00:00Z','created','Draft',0),
          ('{{Closed}}',NULL,'Create','owner-admin','2026-11-05T10:00:00Z','created','Draft',0),
          ('{{Closed}}',NULL,'Cancel','owner-admin','2026-11-06T10:00:00Z','cancelled','Cancelled',1),
          ('{{Parent}}',NULL,'Create','owner-admin','2026-11-07T10:00:00Z','created','Draft',0),
          ('{{Child}}',NULL,'Create','owner-admin','2026-11-07T11:00:00Z','fan-out','Pending',2);

        INSERT INTO "OrderDispatches" ("ServiceOrderId","TargetId","TargetType","DispatchedBy","DispatchedAt","Note","ActorKind") VALUES
          ('{{Parent}}','{{TestIdentities.OccupantOne}}',2,'owner-admin','2026-11-07T10:30:00Z','first occupant',0),
          ('{{Parent}}','{{TestIdentities.OccupantTwo}}',2,'owner-admin','2026-11-07T10:31:00Z',NULL,0),
          ('{{Child}}','{{TestIdentities.OccupantOne}}',2,'owner-admin','2026-11-07T11:30:00Z','notice',0),
          ('{{KernelOrder}}','{{TestIdentities.OccupantTwo}}',2,'owner-admin','2026-11-08T10:30:00Z',NULL,0);

        INSERT INTO "ServiceOrderTargets" ("Id","ServiceOrderId","VerticalId","FeatureId","AssetTypeId","ActivityIdsJson","Status","ConflictLevel","Note","AddedAt","AddedBy","OrganizationId","Version","Longitude","Latitude","ReleasedAt") VALUES
          ('t-approved-a','{{Approved}}','kernel','pole-a','pole','["relocate-attachment"]','Pending',0,NULL,'2026-11-02T10:05:00Z','owner-admin','{{Organization}}',3,-46.123456789012345,-23.5,NULL),
          ('t-approved-b','{{Approved}}','kernel','pole-b','pole','[]','InProgress',1,'a note','2026-11-02T10:06:00Z','owner-admin','{{Organization}}',4,-46.2,-23.6,NULL),
          ('t-staged-c','{{Staged}}','kernel','pole-c','pole','[]','Pending',0,NULL,'2026-11-04T10:05:00Z','owner-admin','{{Organization}}',1,-46.3,-23.7,NULL),
          ('t-closed-d','{{Closed}}','kernel','pole-d','pole','[]','Pending',0,NULL,'2026-11-05T10:05:00Z','owner-admin','{{Organization}}',2,-46.4,-23.8,'2026-11-06T10:00:00Z'),
          ('t-parent-e','{{Parent}}','kernel','pole-e','pole','[]','Pending',0,NULL,'2026-11-07T10:05:00Z','owner-admin','{{Organization}}',1,-46.5,-23.9,NULL),
          ('t-kernel-z','{{KernelOrder}}','kernel','pole-z','pole','[]','Pending',0,NULL,'2026-11-08T10:05:00Z','owner-admin','{{Organization}}',1,-46.6,-24.0,NULL);
        INSERT INTO "ServiceOrderTargets" ("Id","ServiceOrderId","VerticalId","KernelOrderId","OrderTypeId","Exclusivity","FeatureId","AssetTypeId","ActivityIdsJson","Status","ConflictLevel","AddedAt","AddedBy","OrganizationId","Version","ReleasedAt") VALUES
          ('t-remote-fs','{{RemoteOrder}}','field-service',NULL,'manhole-audit',2,'pole-fs','pole','[]','Pending',0,'2026-11-09T10:00:00Z','svc','{{Organization}}',1,NULL);

        INSERT INTO neutral_operator.field_pole_occupancies ("Id","PoleFeatureId","OrganizationId","Version","UpdatedAt") VALUES
          ('aaaaaaaa-0000-4000-8000-000000000001','pole-a','{{Organization}}',2,'2026-10-01T10:00:00Z'),
          ('aaaaaaaa-0000-4000-8000-000000000002','pole-b','{{Organization}}',1,'2026-10-01T10:00:00Z');
        INSERT INTO neutral_operator.field_pole_occupancy_entries ("Id","PoleOccupancyId","SlotIndex","Status","OccupantOrganizationId","OperatorCnpj","CableDiameterMm","CableLoadDaN","OrganizationId") VALUES
          ('bbbbbbbb-0000-4000-8000-000000000001','aaaaaaaa-0000-4000-8000-000000000001',1,'AuthorizedContracted','{{TestIdentities.OccupantOne}}','12345678000190',12.5,3.25,'{{Organization}}'),
          ('bbbbbbbb-0000-4000-8000-000000000002','aaaaaaaa-0000-4000-8000-000000000001',NULL,'ClandestineUnauthorized',NULL,NULL,1.5,0.125,'{{Organization}}');

        INSERT INTO neutral_operator."ServiceOrderOccupancyChangeSets" ("Id","ServiceOrderId","TargetId","PoleFeatureId","ChangesJson","BaselineVersion","Status","StagedBy","StagedAt","ClosedAt","OrganizationId","ApplyBatchId") VALUES
          ('cccccccc-0000-4000-8000-000000000001','{{Approved}}','t-approved-a','pole-a','[{"kind":"Add","slotIndex":1,"entryId":"eeeeeeee-0000-4000-8000-000000000001","status":"AuthorizedContracted","occupantOrganizationId":"{{TestIdentities.OccupantOne}}","operatorCnpj":"98.765.432/0001-10","cableDiameterMm":9.5,"cableLoadDaN":80}]',2,'Staged','owner-admin','2026-11-02T10:10:00Z',NULL,'{{Organization}}','{{Batch}}'),
          ('cccccccc-0000-4000-8000-000000000002','{{Approved}}','t-approved-b','pole-b','[{"kind":"Add","slotIndex":2,"entryId":"eeeeeeee-0000-4000-8000-000000000002","status":"AuthorizedContracted","occupantOrganizationId":"{{TestIdentities.OccupantOne}}","operatorCnpj":"98.765.432/0001-10","cableDiameterMm":9.5,"cableLoadDaN":80}]',1,'Staged','owner-admin','2026-11-02T10:11:00Z',NULL,'{{Organization}}','{{Batch}}'),
          ('cccccccc-0000-4000-8000-000000000003','{{Staged}}','t-staged-c','pole-c','[{"kind":"Add","slotIndex":1,"entryId":"eeeeeeee-0000-4000-8000-000000000003","status":"AuthorizedContracted","occupantOrganizationId":"{{TestIdentities.OccupantOne}}","operatorCnpj":"98.765.432/0001-10","cableDiameterMm":9.5,"cableLoadDaN":80}]',0,'Staged','owner-admin','2026-11-04T10:10:00Z',NULL,'{{Organization}}',NULL),
          ('cccccccc-0000-4000-8000-000000000004','{{Closed}}','t-closed-d','pole-d','[{"kind":"Add","slotIndex":1,"entryId":"eeeeeeee-0000-4000-8000-000000000004","status":"AuthorizedContracted","occupantOrganizationId":"{{TestIdentities.OccupantOne}}","operatorCnpj":"98.765.432/0001-10","cableDiameterMm":9.5,"cableLoadDaN":80}]',0,'Discarded','owner-admin','2026-11-05T10:10:00Z','2026-11-06T10:00:00Z','{{Organization}}',NULL);

        INSERT INTO neutral_operator.occupancy_apply_sagas ("BatchId","ServiceOrderId","OrderTypeId","Attempt","PreviousStatus","ApprovedStatus","RequestedBy","RequestedAt","LastRequestedAt","RetryCount","State","ConflictingFeatureIdsJson","BatchJson","OrganizationId") VALUES
          ('{{Batch}}','{{Approved}}','{{PoleWork}}',1,'Pending','InProgress','owner-admin','2026-11-03T09:30:00Z','2026-11-03T09:30:00Z',0,'Pending','[]','{"poles":["pole-a","pole-b"]}','{{Organization}}');
        INSERT INTO neutral_operator.occupancy_apply_saga_poles ("BatchId","PoleFeatureId","BaselineVersion","AppliedVersion","BaselineEntriesJson","OrganizationId") VALUES
          ('{{Batch}}','pole-a',2,3,'[]','{{Organization}}'),
          ('{{Batch}}','pole-b',1,2,'[]','{{Organization}}');

        INSERT INTO "WorkflowOutboxMessages" ("Id","MessageType","SchemaVersion","PartitionKey","ContentType","Payload","Status","AttemptCount","CreatedAtUtc","NextRetryAtUtc","DispatchedAtUtc") VALUES
          ('dddddddd-0000-4000-8000-000000000001','{{IntegrationEventTypes.AssetMutationBatchRequestedV1}}',1,'{{Approved}}','application/json','\x7b2262617463684964223a2231227d',0,0,'2026-11-03T09:30:00Z','2026-11-03T09:30:00Z',NULL),
          ('dddddddd-0000-4000-8000-000000000002','{{IntegrationEventTypes.AssetMutationBatchRequestedV1}}',1,'{{Approved}}','application/json','\x7b7d',3,2,'2026-11-03T09:31:00Z','2026-11-03T09:40:00Z',NULL),
          ('dddddddd-0000-4000-8000-000000000003','{{IntegrationEventTypes.OrderStateChangedV1}}',1,'{{Approved}}','application/json','\x7b7d',2,1,'2026-11-02T10:00:00Z','2026-11-02T10:00:00Z','2026-11-02T10:00:05Z'),
          ('dddddddd-0000-4000-8000-000000000004','{{IntegrationEventTypes.OrderStateChangedV1}}',1,'{{KernelOrder}}','application/json','\x7b7d',0,0,'2026-11-08T10:00:00Z','2026-11-08T10:00:00Z',NULL);
        """;
}
