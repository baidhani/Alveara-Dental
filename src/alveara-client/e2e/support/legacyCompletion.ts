import { execFileSync } from "node:child_process";

/**
 * ALV-008-C02 TEST-ONLY SEED. The routes that completed a procedure the old, state-only way are retired (they answer 410), so the only way for a real-backend walkthrough to start from "a procedure that was
 * completed before provider, encounter, chart and charge records existed" is to write what the retired code wrote: the procedure's Completed state row, its ItemCompleted history event and its audit entry, straight into the
 * database the API is using (E2E_DB_NAME). It records no provider, encounter, performed time, chart effect or charge, exactly like the original. Only the guid ids are interpolated, after being checked.
 */
const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export function sqlOnE2eDatabase(statement: string): void {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (a legacy completion can only be seeded into it)");
  execFileSync("sqlcmd", ["-S", "(localdb)\\MSSQLLocalDB", "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
}

/** Completes one accepted procedure the old way. Completing an already completed procedure changes nothing. */
export function seedLegacyCompletion(planId: string, itemId: string): void {
  if (!GUID.test(planId) || !GUID.test(itemId)) throw new Error("seedLegacyCompletion needs a plan id and an item id");
  sqlOnE2eDatabase(`SET NOCOUNT ON;
DECLARE @newState uniqueidentifier = NEWID();
IF NOT EXISTS (SELECT 1 FROM TreatmentPlanItemStates s WHERE s.ItemId = '${itemId}' AND s.Completion = 'Completed')
BEGIN
  INSERT INTO TreatmentPlanItemStates (Id, PlanId, PatientId, ItemId, Sequence, Acceptance, Scheduling, Completion, PlannedDate, Reason, DecidedByName, RevisionNumber, RecordedByUserId, RecordedAtUtc)
  SELECT TOP 1 @newState, PlanId, PatientId, ItemId, Sequence + 1, 'Accepted', Scheduling, 'Completed', PlannedDate, NULL, DecidedByName, RevisionNumber, RecordedByUserId, SYSDATETIMEOFFSET() AT TIME ZONE 'UTC'
  FROM TreatmentPlanItemStates WHERE ItemId = '${itemId}' AND Acceptance = 'Accepted' ORDER BY Sequence DESC;
  IF @@ROWCOUNT <> 1 THROW 59050, 'The procedure must be accepted before it can be completed the old way.', 1;
  INSERT INTO TreatmentPlanEvents (Id, PlanId, PatientId, EventNumber, ChangeType, ItemId, Title, Reason, RevisionNumber, ActorUserId, OccurredAtUtc)
  SELECT NEWID(), s.PlanId, s.PatientId, (SELECT ISNULL(MAX(EventNumber), 0) + 1 FROM TreatmentPlanEvents WHERE PlanId = s.PlanId), 'ItemCompleted', s.ItemId, NULL, NULL, s.RevisionNumber, s.RecordedByUserId, s.RecordedAtUtc
  FROM TreatmentPlanItemStates s WHERE s.Id = @newState;
  INSERT INTO AuditLogEntries (Id, EventType, EntityType, TargetUserAccountId, PerformedByUserAccountId, Details, TimestampUtc)
  SELECT NEWID(), 'TreatmentPlanItemCompleted', 'TreatmentPlan', s.PlanId, s.RecordedByUserId,
    'Item ' + CAST((SELECT ItemNumber FROM TreatmentPlanItems WHERE Id = s.ItemId) AS nvarchar(10)) + ' completed in a treatment plan.', s.RecordedAtUtc
  FROM TreatmentPlanItemStates s WHERE s.Id = @newState;
END`);
}
