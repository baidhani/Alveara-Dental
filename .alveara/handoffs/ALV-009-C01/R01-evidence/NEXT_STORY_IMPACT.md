# ALV-009-C01 R01 - Next-story impact

Next in the plan: item 47, ALV-009-C02 (referral and dental laboratory case tracking). **Not started.**

- **Reusable:** system-made tasks with a unique source key (`SystemTaskWriter`, `FollowUpRules` key helpers) and job-driven creation/closure (`RecallScheduler` and handlers as the pattern) are the way to make a task for an overdue lab case or an unanswered referral without a person creating it; the work queue (filters, kinds, claim, assign, priority, timeline) shows any new kind once it is added to `FollowUpKinds` and the `CK_FollowUpTasks_Kind`/`CK_FollowUpTasks_Source` constraints (a migration).
- **Contract to respect:** a task's kind and source never change after creation (trigger `TR_FollowUpTasks_Guard`); system tasks have no creator and no owner until a person takes them; every state change appends an event and an audit entry in the same save.
- **Permissions:** `ManageRecallSettings` (Admin, Office Manager) is new and is not protected, so it can be granted, revoked and delegated through the ALV-N018 policy.
- **Not done and possibly wanted later:** closing a recall task when the patient books; an operator reset for a recall/treatment job that fails all attempts; email or SMS reminders (the reminder channel seam `IReminderChannel` and `IStagingReminderChannel` exists; nothing sends today); showing the kind "UnscheduledTreatment" in a browser demo.
- **Operational:** `FollowUps:WorkPollSeconds` (default 300, 0 = off) drives the recall and unscheduled-treatment passes; `scripts/Invoke-RealBackendWalkthrough.ps1` sets it to 3 for walkthroughs.
