# Plan-change proposal: two owner-approved deferred enhancements (ALV-N030, ALV-N031)

- **Date:** 2026-11-13. **Session:** CC-20261109-8w3d. **Raised with:** ALV-N021 briefing revision 2.
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

**NEXT:** Continue to item 65 — ALV-N028.

## Consequential plan edits for the reviewer

1. Item 65 (ALV-N028) dependencies: add `ALV-N030`, `ALV-N031`.
2. Item 65a (ALV-N029) NEXT: "Continue to item 65b — ALV-N030" instead of item 65.
3. Item 65 NEXT: "After `ALV-N031` has closed" instead of "After `ALV-N029` has closed".
4. `.alveara/EXECUTION_STATUS.json`: two PLANNED entries after ALV-N029, entered at closure of this proposal by the review-closure protocol only.
