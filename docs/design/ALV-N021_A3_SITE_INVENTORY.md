# A3 client site inventory (generated from src/alveara-client/src at HEAD; tests and fakes excluded)

Classification is by pattern and is to be confirmed line by line in the briefing review.

- A DISPLAY (browser zone unless a timeZone is passed): 76
- B browser TODAY / local calendar day: 12
- C INPUT datetime-local: 9
- D already practice-zone: 2
- E current time (use to check): 21
- F wire value (UTC ISO): 4
- Z number formatting (not a time): 33

| Class | File | Line | Code |
|---|---|---|---|
| A DISPLAY (browser zone unless a timeZone is passed) | `components/dateOnly.ts` | 12 | `return day.toLocaleDateString(undefined, options);` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/AdminUserDetailPage.tsx` | 177 | `<dd>{new Date(state.user.createdAtUtc).toLocaleString()}</dd>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/AuditLogPage.tsx` | 77 | `<td>{new Date(entry.timestampUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/LoginPage.tsx` | 86 | `{formState.lockedUntilUtc && ' Try again after ${new Date(formState.lockedUntilUtc).toLocaleTimeString()}.'}` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/PatientHistoryPanel.tsx` | 61 | `<td>{new Date(r.changedAtUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/PatientHistoryPanel.tsx` | 71 | `<p className="alv-workspace__note">Registered {new Date(patient.createdAtUtc).toLocaleString()}.</p>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/SystemStatusPage.tsx` | 82 | `Last poll: {new Date(status.backgroundRunner.lastPollUtc).toLocaleString()}` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/SystemStatusPage.tsx` | 127 | `{new Date(state.lastConfirmedAtUtc).toLocaleString()}. The most recent check failed,` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/backup/ArchivePanel.tsx` | 37 | `<td>{new Date(a.modifiedUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/backup/BackupLists.tsx` | 10 | `const when = (iso: string \| null) => (iso ? new Date(iso).toLocaleString() : "-");` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/backup/BackupStatusPanel.tsx` | 4 | `const when = (iso: string \| null) => (iso ? new Date(iso).toLocaleString() : "never");` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/backup/RecoveryKeyPanel.tsx` | 86 | `{settings.recoveryKeyConfiguredAtUtc ? ', ${new Date(settings.recoveryKeyConfiguredAtUtc).toLocaleDateString()}' : ""}). The server holds only its pub` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/backup/RestoreWizard.tsx` | 132 | `Restore wizard - {archive ? 'retained backup file ${archive.fileName}' : 'backup of ${new Date(record!.startedAtUtc).toLocaleString()}'}` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingChargeSource.tsx` | 6 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingDocumentView.tsx` | 3 | `const day = (iso: string) => new Date(iso).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingDocumentView.tsx` | 21 | `<p>For: {statement.patient.name}<br />Addressed to: {statement.addressee.name}{statement.addressee.isSelf ? "" : " (responsible for this account)"}<br` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingDocumentView.tsx` | 57 | `<p>For: {receipt.patient.name}<br />Received from: {receipt.receivedFrom.name}<br />Received: {day(receipt.receivedAtUtc)}{receipt.method ? ' by ${rec` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingDocuments.tsx` | 127 | `<p className="alv-clinical__meta">{detail.document.documentType} {detail.document.documentNumber}, issued {new Date(detail.document.issuedAtUtc).toLoc` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingDocuments.tsx` | 153 | `<td>{new Date(d.issuedAtUtc).toLocaleString()}</td><td>{d.cutOffs.entryNumber}</td><td>{d.issuedByName ?? "Unknown"}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingLedger.tsx` | 55 | `<td>{new Date(e.recordedAtUtc).toLocaleDateString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/billing/BillingTimelineAging.tsx` | 54 | `<span className="alv-bill__when">{new Date(e.atUtc).toLocaleString()}</span> <strong>{e.type}</strong>{e.number !== null ? ' ${e.number}' : ""}: {e.su` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/calendar/AppointmentHistory.tsx` | 31 | `<td>{new Date(e.occurredAtUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/calendar/AppointmentPanel.tsx` | 187 | `{flowLabel && current.flowChangedAtUtc && <div className="alv-form-answers__row"><dt>{flowLabel} at</dt><dd>{new Date(current.flowChangedAtUtc).toLoca` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/calendar/AppointmentPanel.tsx` | 194 | `{current.statusChangedAtUtc && current.status !== "Scheduled" && <div className="alv-form-answers__row"><dt>{statusWord(current.status)} at</dt><dd>{n` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/calendar/calendarLayout.ts` | 151 | `const parts = new Intl.DateTimeFormat("en-CA", {` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/clinical/AddendumPanel.tsx` | 11 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/clinical/EncounterView.tsx` | 27 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/clinical/PatientClinicalPanel.tsx` | 14 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/clinical/notes/VitalsPanel.tsx` | 14 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/clinical/record/ItemHistory.tsx` | 6 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/clinical/record/RecordItemRow.tsx` | 12 | `const when = (iso: string) => new Date(iso).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/clinical/record/RecordSectionCard.tsx` | 11 | `const when = (iso: string) => new Date(iso).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/diagnosis/DiagnosisChips.tsx` | 6 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/diagnosis/DiagnosisForm.tsx` | 140 | `{encounters.map((e) => <option key={e.id} value={e.id}>{new Date(e.encounterAtUtc).toLocaleString()} ({e.status.toLowerCase()})</option>)}` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/diagnosis/DiagnosisForm.tsx` | 144 | `{correcting && <p className="alv-clinical__meta">Encounter of {new Date(correcting.encounterAtUtc).toLocaleString()}. The patient and the encounter ne` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/diagnosis/DiagnosisHistory.tsx` | 7 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/diagnosis/DiagnosisItem.tsx` | 14 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/diagnosis/DiagnosisItem.tsx` | 30 | `<span className="alv-clinical__meta"> {methodWords(resolved.method)[0].toUpperCase() + methodWords(resolved.method).slice(1)}{resolved.byName ? ' by $` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/diagnosis/DiagnosisLinkForm.tsx` | 18 | `const when = (iso: string) => new Date(iso).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/documents/documentText.ts` | 14 | `export const formatDateTime = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/documents/documentText.ts` | 31 | `export const encounterLabel = (atUtc: string) => new Date(atUtc).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/exports/exportFormat.ts` | 10 | `return when.toLocaleString();                                                       // a time zone name this browser does not know: the browser's own ` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/externalcases/ExternalCaseDetailPage.tsx` | 88 | `<strong>{describeEvent(e)}</strong> by {e.actorName ?? "a former colleague"} · {new Date(e.occurredAtUtc).toLocaleString()}` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/externalcases/PatientExternalCasesPanel.tsx` | 64 | `<strong>{describeEvent({ kind: t.eventKind, fromStatus: t.fromStatus, toStatus: t.toStatus })}</strong>: {t.caseSummary} ({kindLabel(t.caseKind)}) by ` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/externalcases/externalCaseFormat.ts` | 16 | `return new Date(y, m - 1, d).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/flow/FlowBoardPage.tsx` | 27 | `const clock = (ms: number) => new Date(ms).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/followups/followUpFormat.ts` | 21 | `export const formatWhen = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/forms/FormTemplatesPage.tsx` | 104 | `<td>{new Date(v.createdAtUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/forms/PatientFormView.tsx` | 156 | `<span>Started {new Date(s.startedAtUtc).toLocaleString()}</span>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/forms/PatientFormView.tsx` | 162 | `<p><strong>This form is void</strong>{s.voidedAtUtc ? ' as of ${new Date(s.voidedAtUtc).toLocaleString()}' : ""}. Reason: {s.voidReason ?? "—"}</p>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/forms/PatientFormsPanel.tsx` | 14 | `const when = (iso: string \| null) => (iso ? new Date(iso).toLocaleString() : "—");` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/forms/SignedFormView.tsx` | 8 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/imports/importFormat.ts` | 14 | `export const money = (amount: number) => amount.toLocaleString("en-US", { style: "currency", currency: "USD" });` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/notifications/AdminNotificationsPage.tsx` | 13 | `const when = (iso: string \| null) => (iso ? new Date(iso).toLocaleString() : "");` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/notifications/MessagesPage.tsx` | 13 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/notifications/NotificationsPage.tsx` | 16 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/odontogram/odontogramText.ts` | 5 | `export const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/patientmerge/MergeHistoryBanner.tsx` | 7 | `const when = (iso: string) => new Date(iso).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/patientmerge/PatientMergePage.tsx` | 133 | `<td>{d.reason}</td><td>{d.dismissedByName \|\| d.dismissedByUserId}</td><td>{new Date(d.dismissedAtUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/patientmerge/PatientMergePage.tsx` | 149 | `<td>{new Date(m.mergedAtUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/patientmerge/PatientMergePage.tsx` | 156 | `<td>{m.undoneAtUtc ? 'Undone ${new Date(m.undoneAtUtc).toLocaleDateString()} by ${m.undoneByName \|\| "an administrator"}' : "In force"}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/perio/PerioChartExtras.tsx` | 38 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/perio/PerioHistory.tsx` | 25 | `const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/perio/PerioSessionPanel.tsx` | 253 | `labels={{ current: "The chart in progress", previous: new Date(latestChart.recordedAtUtc).toLocaleString() }} numbering={numbering} />` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/permissions/PolicyHistoryPanel.tsx` | 30 | `<td>{new Date(h.changedAtUtc).toLocaleString()}</td>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/prescription/workflow/PrintPreview.tsx` | 9 | `const day = (iso: string) => new Date(iso).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/procedures/procedureText.ts` | 15 | `export const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/procedures/procedureText.ts` | 17 | `export const dateText = (d: string) => new Date('${d}T00:00:00').toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/reports/PatientFlowView.tsx` | 31 | `<li><span>Past their time, not completed</span><strong>{d.pastNotCompleted.toLocaleString()}</strong></li>` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/safety/safetyText.ts` | 4 | `export const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/safety/safetyText.ts` | 5 | `export const day = (iso: string) => new Date(iso).toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/treatmentplan/TreatmentPlanPatientCopy.tsx` | 41 | `const today = new Date().toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/treatmentplan/treatmentPlanLayout.ts` | 101 | `const parts = [phase.plannedVisit, phase.targetDate ? 'target ${new Date('${phase.targetDate}T00:00:00').toLocaleDateString()}' : null].filter((x): x ` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/treatmentplan/treatmentPlanRules.ts` | 13 | `export const when = (iso: string) => new Date(iso).toLocaleString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/treatmentplan/treatmentPlanRules.ts` | 16 | `export const dayOf = (isoDate: string) => new Date('${isoDate}T00:00:00').toLocaleDateString();` |
| A DISPLAY (browser zone unless a timeZone is passed) | `pages/treatmentplan/treatmentPlanRules.ts` | 65 | `export const formatFee = (amount: number) => amount.toLocaleString("en-US", { style: "currency", currency: "USD" });` |
| B browser TODAY / local calendar day | `components/dateOnly.ts` | 11 | `if (day.getFullYear() !== y \|\| day.getMonth() !== m - 1 \|\| day.getDate() !== d) return isoDate;` |
| B browser TODAY / local calendar day | `components/dateOnly.ts` | 19 | `export function todayDateOnly(now: Date = new Date()): string {` |
| B browser TODAY / local calendar day | `components/dateOnly.ts` | 21 | `return '${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}';` |
| B browser TODAY / local calendar day | `pages/calendar/calendarLayout.ts` | 15 | `const fromDayNumber = (n: number) => new Date(n * 86_400_000).toISOString().slice(0, 10);` |
| B browser TODAY / local calendar day | `pages/documents/documentText.ts` | 19 | `return '${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}';` |
| B browser TODAY / local calendar day | `pages/externalcases/externalCaseFormat.ts` | 23 | `return '${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}';` |
| B browser TODAY / local calendar day | `pages/followups/followUpFormat.ts` | 11 | `return '${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}';` |
| B browser TODAY / local calendar day | `pages/reports/reportFormat.ts` | 5 | `export const isoDate = (d: Date) => '${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}';` |
| B browser TODAY / local calendar day | `pages/reports/reportFormat.ts` | 40 | `const first = new Date(now.getFullYear(), now.getMonth() - 1, 1);` |
| B browser TODAY / local calendar day | `pages/reports/reportFormat.ts` | 41 | `const last = new Date(now.getFullYear(), now.getMonth(), 0);` |
| B browser TODAY / local calendar day | `pages/scheduling/SchedulePage.tsx` | 37 | `return '${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}';` |
| B browser TODAY / local calendar day | `pages/treatmentplan/completionReviewRules.ts` | 14 | `return '${moment.getFullYear()}-${pad(moment.getMonth() + 1)}-${pad(moment.getDate())}T${pad(moment.getHours())}:${pad(moment.getMinutes())}:${pad(mom` |
| C INPUT datetime-local | `pages/clinical/notes/VitalsPanel.tsx` | 162 | `<input id={atId} type="datetime-local" className="alv-form-field__input" value={values.measuredAt} aria-invalid={errors.measuredAtUtc ? true : undefin` |
| C INPUT datetime-local | `pages/configuration/AvailabilityTab.tsx` | 404 | `<FormField label="Blocked from" type="datetime-local" value={blockForm.start} onChange={(e) => editBlockForm({ start: e.target.value })} />` |
| C INPUT datetime-local | `pages/configuration/AvailabilityTab.tsx` | 405 | `<FormField label="Blocked until" type="datetime-local" value={blockForm.end} onChange={(e) => editBlockForm({ end: e.target.value })} />` |
| C INPUT datetime-local | `pages/followups/FollowUpForm.tsx` | 76 | `<input id="fu-due" type="datetime-local" className="alv-form-field__input" value={values.due} onChange={set("due")} aria-invalid={!!errors.due} aria-d` |
| C INPUT datetime-local | `pages/followups/followUpFormat.ts` | 8 | `/** The value of a 'datetime-local' input (the person's own clock) for an instant. */` |
| C INPUT datetime-local | `pages/followups/followUpFormat.ts` | 14 | `/** The instant (ISO, UTC) a 'datetime-local' value means on the person's own clock, or null when it is empty or not a date. */` |
| C INPUT datetime-local | `pages/treatmentplan/TreatmentPlanAdoptCompletion.tsx` | 143 | `<input id={'${uid}-performed'} type="datetime-local" step="1" value={performed} onChange={(e) => setPerformed(e.target.value)} aria-describedby={error` |
| C INPUT datetime-local | `pages/treatmentplan/TreatmentPlanReviewComplete.tsx` | 132 | `<input id={'${uid}-performed'} type="datetime-local" step="1" value={performed} onChange={(e) => setPerformed(e.target.value)} aria-describedby={error` |
| C INPUT datetime-local | `pages/treatmentplan/completionReviewRules.ts` | 12 | `/** A date and time as a 'datetime-local' field shows it (the person's own clock, to the second so "now" is never earlier than something just recorded` |
| D already practice-zone | `pages/calendar/calendarLayout.ts` | 150 | `export function practiceNow(timeZoneId: string, now: Date = new Date()): string {` |
| D already practice-zone | `pages/exports/exportFormat.ts` | 8 | `return when.toLocaleString(undefined, timeZone ? { ...options, timeZone } : options);` |
| E current time (use to check) | `contexts/AuthContext.tsx` | 167 | `const remainingMs = expiresAtMs - Date.now();` |
| E current time (use to check) | `hooks/useSystemStatus.ts` | 68 | `const confirmedAtUtc = new Date().toISOString();` |
| E current time (use to check) | `pages/documents/documentText.ts` | 17 | `export function todayValue(now = new Date()): string {` |
| E current time (use to check) | `pages/externalcases/externalCaseFormat.ts` | 21 | `const d = new Date();` |
| E current time (use to check) | `pages/flow/FlowBoardPage.tsx` | 50 | `const [clientNow, setClientNow] = useState(() => Date.now());` |
| E current time (use to check) | `pages/flow/FlowBoardPage.tsx` | 90 | `setFetchedAt(Date.now());` |
| E current time (use to check) | `pages/flow/FlowBoardPage.tsx` | 111 | `const t = setInterval(() => setClientNow(Date.now()), CLOCK_TICK_MS);` |
| E current time (use to check) | `pages/imports/importTestServer.ts` | 52 | `if (u.includes("/api/auth/permissions")) return Promise.resolve(json({ username: "admin", role: "Admin", permissions: this.permissions, sessionExpires` |
| E current time (use to check) | `pages/imports/useImportBatch.ts` | 38 | `const started = Date.now();` |
| E current time (use to check) | `pages/imports/useImportBatch.ts` | 40 | `if (Date.now() - started >= POLL_LIMIT_MS) { clearInterval(timer); setPollExpired(true); return; }` |
| E current time (use to check) | `pages/notifications/ComposeMessage.tsx` | 9 | `const newKey = () => (crypto.randomUUID ? crypto.randomUUID() : 'k-${Date.now()}-${Math.random().toString(16).slice(2)}');` |
| E current time (use to check) | `pages/reports/reportFormat.ts` | 6 | `export const today = () => isoDate(new Date());` |
| E current time (use to check) | `pages/reports/reportFormat.ts` | 39 | `const now = new Date();` |
| E current time (use to check) | `pages/scheduling/SchedulePage.tsx` | 35 | `const d = new Date();` |
| E current time (use to check) | `pages/treatmentplan/TreatmentPlanAdoptCompletion.tsx` | 42 | `const [performed, setPerformed] = useState(() => localInputValue(new Date()));` |
| E current time (use to check) | `pages/treatmentplan/TreatmentPlanReviewComplete.tsx` | 37 | `const [performed, setPerformed] = useState(() => localInputValue(new Date()));` |
| E current time (use to check) | `services/clinicalApi.ts` | 201 | `export const newKey = () => (typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : 'k-${Date.now()}-${Math.random().toString` |
| E current time (use to check) | `services/externalCasesApi.ts` | 48 | `export const newCaseKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : 'k-${Date.now()}-${Math.random().toString(16).slice(2)}');` |
| E current time (use to check) | `services/followUpsApi.ts` | 53 | `export const newFollowUpKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : 'k-${Date.now()}-${Math.random().toString(16).slice(2)}');` |
| E current time (use to check) | `services/patientMergesApi.ts` | 63 | `export const newMergeKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : 'k-${Date.now()}-${Math.random().toString(16).slice(2)}');` |
| E current time (use to check) | `services/permissionPolicyApi.ts` | 54 | `export const newPolicyKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : 'k-${Date.now()}-${Math.random().toString(16).slice(2)}');` |
| F wire value (UTC ISO) | `components/dateOnly.ts` | 16 | `* ALV-N021 A1 (found in the A1 date-site classification, same kind as F-107): today's calendar date on the viewer's own clock, as a date box takes it ` |
| F wire value (UTC ISO) | `pages/followups/followUpFormat.ts` | 18 | `return Number.isNaN(d.getTime()) ? null : d.toISOString();` |
| F wire value (UTC ISO) | `pages/treatmentplan/completionReviewRules.ts` | 21 | `return Number.isNaN(moment.getTime()) ? null : moment.toISOString();` |
| F wire value (UTC ISO) | `services/clinicalNotesApi.ts` | 58 | `if (input.measuredAt.trim() !== "") body.measuredAtUtc = new Date(input.measuredAt).toISOString();` |
| Z number formatting (not a time) | `pages/configuration/SignInScreenTab.tsx` | 165 | `Notice text (plain text, up to {NOTICE_MAX.toLocaleString()} characters)` |
| Z number formatting (not a time) | `pages/reports/BalancesView.tsx` | 29 | `<li><span>Owed at the end</span><strong>{money(d.atEnd.totalOwed)}</strong><small>{d.atEnd.accountsOwing.toLocaleString()} {d.atEnd.accountsOwing === ` |
| Z number formatting (not a time) | `pages/reports/BalancesView.tsx` | 30 | `<li><span>Owed at the start</span><strong>{money(d.atStart.totalOwed)}</strong><small>{d.atStart.accountsOwing.toLocaleString()} {d.atStart.accountsOw` |
| Z number formatting (not a time) | `pages/reports/BalancesView.tsx` | 38 | `{row("Accounts with any entry", (s) => s.accounts.toLocaleString(), "accounts")}` |
| Z number formatting (not a time) | `pages/reports/BalancesView.tsx` | 41 | `: s.accountsOwing.toLocaleString()), "owing")}` |
| Z number formatting (not a time) | `pages/reports/BalancesView.tsx` | 45 | `: s.accountsInCredit.toLocaleString()), "credit")}` |
| Z number formatting (not a time) | `pages/reports/BalancesView.tsx` | 52 | `? <p className="alv-rep__hint">The change ({money(d.change)}) equals an independent sum of the {d.ledgerEntries.toLocaleString()} {d.ledgerEntries ===` |
| Z number formatting (not a time) | `pages/reports/CancellationsView.tsx` | 52 | `<li><span>Booked</span><strong>{d.totals.booked.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/CancellationsView.tsx` | 53 | `<li><span>Cancelled</span><strong>{d.totals.cancelled.toLocaleString()}</strong><small>{percent(d.totals.cancellationRate)} of booked</small></li>` |
| Z number formatting (not a time) | `pages/reports/CancellationsView.tsx` | 54 | `<li><span>No-shows</span><strong>{d.totals.noShow.toLocaleString()}</strong><small>{percent(d.totals.noShowRate)} of booked</small></li>` |
| Z number formatting (not a time) | `pages/reports/CollectionsView.tsx` | 32 | `<li><span>Received</span><strong>{money(d.paymentsReceived)}</strong><small>{d.paymentEntries.toLocaleString()} {d.paymentEntries === 1 ? "payment" : ` |
| Z number formatting (not a time) | `pages/reports/ComparisonSection.tsx` | 11 | `const side = (s: MetricSide \| null) => (s === null ? "None" : '${percent(s.rate)} (${s.numeratorCount.toLocaleString()} of ${s.denominatorCount.toLoca` |
| Z number formatting (not a time) | `pages/reports/CompletedProceduresView.tsx` | 28 | `<li><span>Procedures performed</span><strong>{d.procedures.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/LedgerProof.tsx` | 23 | `<td>{opens.includes(l.line) ? <NumberButton value={l.entries} label={LINE_LABELS[l.line] ?? l.line} onOpen={() => onOpen(l.line)} /> : l.entries.toLoc` |
| Z number formatting (not a time) | `pages/reports/LedgerProof.tsx` | 27 | `<tr><th scope="row">All lines</th><td>{proof.linesEntries.toLocaleString()}</td><td>{money(proof.linesTotal)}</td></tr>` |
| Z number formatting (not a time) | `pages/reports/LedgerProof.tsx` | 31 | `? <p className="alv-rep__hint">The lines add up to the ledger: {money(proof.ledgerTotal)} across {proof.ledgerEntries.toLocaleString()} {proof.ledgerE` |
| Z number formatting (not a time) | `pages/reports/MetricsSection.tsx` | 39 | `<td>{m.numeratorCount.toLocaleString()}</td>` |
| Z number formatting (not a time) | `pages/reports/MetricsSection.tsx` | 40 | `<td>{m.denominatorCount.toLocaleString()}</td>` |
| Z number formatting (not a time) | `pages/reports/NumberButton.tsx` | 5 | `<button type="button" className="alv-rep__number" aria-label={'${value}, show the records: ${label}'} aria-pressed={selected ?? false} onClick={onOpen` |
| Z number formatting (not a time) | `pages/reports/PatientFlowView.tsx` | 28 | `<li><span>Appointments</span><strong>{d.appointments.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/PatientFlowView.tsx` | 29 | `<li><span>Cancelled</span><strong>{d.cancelled.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/PatientFlowView.tsx` | 30 | `<li><span>No-shows</span><strong>{d.noShow.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/PatientFlowView.tsx` | 48 | `<tbody>{d.transitions.map((t) => <tr key={t.event}><th scope="row">{words(t.event)}</th><td>{t.count.toLocaleString()}</td></tr>)}</tbody>` |
| Z number formatting (not a time) | `pages/reports/ProductionView.tsx` | 33 | `<li><span>Charged</span><strong>{money(d.grossCharges)}</strong><small>{d.chargeEntries.toLocaleString()} {d.chargeEntries === 1 ? "charge" : "charges` |
| Z number formatting (not a time) | `pages/reports/ProductionView.tsx` | 58 | `<tbody>{d.codes.map((c) => <tr key={c.procedureCode}><th scope="row">{c.procedureCode}</th><td>{c.charges.toLocaleString()}</td><td>{money(c.gross)}</` |
| Z number formatting (not a time) | `pages/reports/RecordsPanel.tsx` | 58 | `<p>{first.total.toLocaleString()} {first.total === 1 ? "record" : "records"}; showing {rows.length.toLocaleString()}.</p>` |
| Z number formatting (not a time) | `pages/reports/ScheduleView.tsx` | 46 | `<li><span>Appointments</span><strong>{t.appointments.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/ScheduleView.tsx` | 47 | `<li><span>Scheduled</span><strong>{t.scheduled.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/ScheduleView.tsx` | 48 | `<li><span>Cancelled</span><strong>{t.cancelled.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/ScheduleView.tsx` | 49 | `<li><span>No-shows</span><strong>{t.noShow.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/ScheduleView.tsx` | 51 | `<li><span>Reschedules recorded</span><strong>{report.data.rescheduleEvents.toLocaleString()}</strong></li>` |
| Z number formatting (not a time) | `pages/reports/TreatmentPlansView.tsx` | 40 | `<li><span>Plans made</span><strong>{d.plans.toLocaleString()}</strong><small>{d.plansProposed.toLocaleString()} proposed, {d.plansWithdrawn.toLocaleSt` |
| Z number formatting (not a time) | `pages/reports/TreatmentPlansView.tsx` | 41 | `<li><span>Procedures proposed</span><strong>{d.items.toLocaleString()}</strong></li>` |

## Server request fields (section 5 of the briefing)

| Route | Field today | A3 field |
|---|---|---|
| `POST api/treatment-plans/{planId}/complete-procedures` | `performedAtUtc` | `performedAtLocal` |
| `POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion` | `performedAtUtc` | `performedAtLocal` |
| `POST api/patients/{patientId}/follow-ups` | `dueAtUtc` | `dueAtLocal` |
| `PUT api/follow-ups/{id}` | `dueAtUtc` | `dueAtLocal` |
| `POST api/encounters/{encounterId}/vitals` | `measuredAtUtc` | `measuredAtLocal` |
| `GET api/auth/permissions` | - | adds `practiceTimeZoneId` |
