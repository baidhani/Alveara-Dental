# Plan-change proposal: three owner-approved deferred enhancements (ALV-N030, ALV-N031, ALV-N032)

- **Date:** 2026-11-13. **Session:** CC-20261109-8w3d. **Raised with:** ALV-N021 briefing revision 2; ALV-N032 added with revision 3. Owner decisions: `docs/design/ALV-N021_OWNER_DECISION_RECORD.md` OD-33 to OD-36.
- **Status:** **PROPOSED for the reviewer to act on.** The agent has not edited `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` or `.alveara/EXECUTION_STATUS.json`; the plan admits new features only through a separately approved package.
- **Owner instruction:** place both at the end of the current stabilization sprint, after the existing deferred placeholder ALV-N029 (item 65a) and before the final accumulated chain ALV-N028 (item 65), so the one final chain includes them and does not have to run twice. Any further owner feature later joins the same slot.

## Proposed plan text

### 65b — ALV-N030 — Replace Appointment (linked cancel-and-rebook)

**Type:** New Production — owner-approved deferred enhancement
**Dependencies:** ALV-N021

**Scope:** Instead of changing an appointment's type in place, an authorized user replaces the appointment: in one transaction the original is cancelled with category "Replaced" and a new appointment is created for the same patient, with a changed type, length and/or provider, defaulting to the original slot and room. The two are linked ("Replaced by" / "Replaces"). Allowed only before treatment starts.

**Acceptance:** The new appointment passes the same availability rules as booking (provider working, not blocked, not double-booked, room free, patient not double-booked) through the shared scheduling guards; if it is refused, the original is unchanged. A reason is required. The action needs `ReassignAppointmentProvider` (or a separate permission if the reviewer prefers). Both appointments' history and the audit record the actor, the reason and the link. A retry with the same request cannot create two replacements. The visit state of the replaced appointment (for example Checked in) is carried to the new one only if the reviewer approves that rule; otherwise replacement is limited to Scheduled and Confirmed.

**Required coverage:** atomicity on a refused replacement, idempotent retry, authorization denial, link display on both appointments, reports counting a replacement neither as a patient cancellation nor as a no-show, and history/audit evidence.

**NEXT:** Continue to item 65c — ALV-N031.

### 65c — ALV-N031 — Appointment Confirmation Reminders for Front Desk

**Type:** New Production — owner-approved deferred enhancement
**Dependencies:** ALV-N021 (configurable Confirm step), STORY-009 scheduled-notification pattern

**Scope:** In-app reminders so front desk confirms appointments in time. Recipients: accounts with the FrontDesk role only. Only **unconfirmed** appointments (state Scheduled) trigger anything. Available whether the Confirm step is required or optional.

**Settings (Admin only, audited; defaults in brackets):**
- Confirmation reminders master switch [On].
- Individual reminder: On/Off [On]; N business days before the appointment [1].
- Summary: On/Off [On]; M business days **before the individual reminder** [1], so the summary goes N + M business days before the appointment [2].
- N >= 1, M >= 1, N + M <= 10. Turning a reminder off keeps its saved days; the summary's position is still counted from N when the individual reminder is off. No "0 means off".
- The settings screen states the result in words, for example "Summary 5 business days before, reminder 3 business days before".

**Behaviour:**
- Business days are the practice's open days (closed days and closures skipped), not weekdays.
- At the practice's opening time on each business day: one **summary** notification (Normal priority) listing the unconfirmed appointments due N + M business days ahead; no summary when the list is empty. One **individual** notification (Important priority) per unconfirmed appointment due N business days ahead.
- Each front-desk user receives their own copy. A front-desk user signing in for the first time that day sees the day's reminders waiting, shown with that user's chosen pop-up style.
- After the appointment is confirmed (or cancelled), the reminder stays in the notification centre with a clearly visible "Confirmed" status. The stored priority is not changed (notifications are append-only and the database forbids priority changes); the display shows a confirmed reminder as Normal, removes it from the Important count and pop-ups, and sorts it below open ones.
- Notifications carry no clinical content: time and a link; the patient's name is shown only inside the app.
- One notification per recipient, appointment, kind and day: a rerun of the job sends nothing twice (source-key idempotency).

**Acceptance:** settings validation and audit; business-day arithmetic across closed days, holidays, the practice time zone and DST; exact recipients (FrontDesk role only); empty-summary suppression; idempotent reruns; confirmed display and counts; sign-in pop-up delivery; unchanged behaviour when switched off. Two new notification kinds need one additive migration of the notification kind/shape constraints (ALV-N020 pattern).

**Required coverage:** the above with the injected clock; authorization denial on the settings; a browser check of the pop-up and the confirmed display; no PHI in notification text.

**NEXT:** Continue to item 65d — ALV-N032.

### 65d — ALV-N032 — Late Arrival, Automatic No-Show and No-Show Fee

**Type:** New Production — owner-approved deferred enhancement
**Dependencies:** ALV-N021 (visit workflow, manual no-show, checkout gate), STORY-015/ALV-008-C02 ledger

**Scope:** A background job watches today's appointments in practice time.
- **Late:** a Scheduled/Confirmed appointment not checked in by its start plus the late threshold [10 minutes] is flagged "Late" on Calendar and Board, and an **Important** notification goes to the front-desk and office-manager users signed in that day and to the appointment's provider. Check-in stays possible; a late check-in is logged with the minutes late (already in ALV-N021).
- **Automatic no-show:** if still not checked in at start plus the no-show threshold [30 minutes], the appointment is marked No-show automatically (system actor), freeing the provider and room. The manual "Mark no-show" button remains. A no-show is final; the patient is rebooked as a new appointment (no correction).
- **No-show fee:** optional practice setting [off; amount when on]. When set, an automatic no-show (and a manual one, if the reviewer agrees) posts one no-show fee charge to the patient's account. This adds a **new non-procedure charge type to the ledger**. A wrong fee is reversed by a `ManageBilling` holder with a reason.

**Settings (Admin/`ManagePracticeConfiguration`, audited):** late threshold, no-show threshold (greater than the late threshold), fee on/off and amount.

**Acceptance:** thresholds evaluated in practice time across DST; exactly one late notification per recipient and appointment and exactly one no-show per appointment on job reruns (idempotency keys); a check-in racing the automatic no-show resolves under the row version with one outcome; the fee is posted at most once per appointment, is idempotent, appears in the ledger, statements and financial reports, and is reversible with a reason; no PHI in notifications; settings validation and authorization denial. The ledger change follows the financial-integrity rules (append-only, verified by the ledger verification).

**Required coverage:** the above with the injected clock; a ledger-verification run after fee posting and reversal; a browser check of the Late flag and notifications.

**NEXT:** Continue to item 65 — ALV-N028.

## Consequential plan edits for the reviewer

1. Item 65 (ALV-N028) dependencies: add `ALV-N030`, `ALV-N031`, `ALV-N032`.
2. Item 65a (ALV-N029) NEXT: "Continue to item 65b — ALV-N030" instead of item 65.
3. Item 65 NEXT: "After `ALV-N032` has closed" instead of "After `ALV-N029` has closed".
4. `.alveara/EXECUTION_STATUS.json`: three PLANNED entries after ALV-N029, entered only by the review-closure protocol after the reviewer approves this proposal.
