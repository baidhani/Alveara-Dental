# ALV-N021 A1 decision request 01: statement aging counted in UTC days

- **Date:** 2026-11-14. **Session:** CC-20261109-8w3d. **Story/attempt:** ALV-N021 A1 (PKG-04 date/time integrity), approved in `BRIEFING-R04.md`.
- **Raised by:** the coding agent during A1's read-only first step; referred to the reviewer by the product owner.
- **Status:** **DECISION REQUESTED.** No code has been changed. A1's other fixes do not depend on this answer and are listed in section 5 for context only.

## 1. What was found

`src/Alveara.Api/Architecture/Billing/FinancialDocumentBuilder.cs` line 47:

```csharp
private static int AgeDays(DateTimeOffset asOf, DateTimeOffset service) => Math.Max(0, (asOf.UtcDateTime.Date - service.UtcDateTime.Date).Days);
```

A charge's age (and so its aging bucket: Current 0-30, 31-60, 61-90, Over 90) is counted in whole **UTC** calendar days from the service date (the procedure's performed time, as amended, or the charge's recorded time) to the document's as-of instant. The class comment says this is intentional ("Aging counts whole UTC days"); it was built that way in ALV-007-C02.

The practice runs on its own time zone (`PracticeClock`, for example America/Chicago, UTC-6/-5). Everywhere else, A1 makes a calendar day mean the **practice-local** day. This is the one remaining place where a day is the UTC day, and it is in financial documents.

## 2. When it gives a different answer

Only when the service time or the document's as-of time falls between local midnight and the UTC midnight (in Chicago: 6 or 7 PM to midnight local).

Example (Chicago, CST): a procedure performed **Monday 10:42 PM local** (= Tuesday 04:42 UTC). Statement as of **Thursday 9:00 AM local** (Thursday 15:00 UTC).
- UTC days: Tuesday to Thursday = **2** days.
- Practice days: Monday to Thursday = **3** days.

The difference is always at most one day. It changes the bucket only when the age is exactly on a boundary (30/31, 60/61, 90/91), so the open amount can be shown in a different bucket on such a day. Balances, totals and amounts owed never change; only the age number and possibly the bucket.

This is the same class of defect as F-118 (the charge's "performed" date shown as the UTC day, fixed in A1), and F-030's 5-hour offset in the owner's log.

## 3. The constraint that makes this a decision, not a fix

Issued statements are immutable and verified (`BillingService.Documents.cs`):
- At issue, the content hash (SHA-256 of the canonical JSON, which **includes each charge's `AgeDays` and `Bucket`**) and `TemplateVersion` (currently `1`) are stored on the `FinancialDocument` row.
- A **reprint** rebuilds the content with the current builder at the stored cut-offs and compares it with the stored hash; on a mismatch it refuses with `document_content_changed` and logs `billing_document_hash_mismatch` (`ContractViolation`).
- The reprint path does **not** currently choose a builder by the stored `TemplateVersion`; it always uses the current one.

So simply changing line 47 would make every already-issued statement that contains a charge affected by the window in section 2 **refuse to reprint**. That is unacceptable: an issued financial document must reprint exactly as issued.

## 4. Options

| Option | What changes | Issued documents | Risk |
|---|---|---|---|
| **A. Practice-local days, versioned** (agent's recommendation) | New rule counts practice-local calendar days. `TemplateVersion` becomes `2` for new previews/issues. The builder keeps the version-1 rule and the reprint path builds with the document's **stored** `TemplateVersion`. | Version-1 documents reprint byte-identical (hash unchanged). Version-2 documents use practice days. | Small, contained change in the billing document builder; touches an approved financial-document contract (ALV-007-C02), hence this request. A preview token issued under v1 and submitted after the release is refused (`token.V` mismatch already handled) and the user previews again. |
| B. Practice-local days, unversioned | Change line 47 only. | Affected issued documents fail to reprint (`document_content_changed`). | **Not acceptable**; listed only to show why A needs the version. |
| C. Keep UTC days | Nothing in code. The rule is documented as a known behaviour: "aging counts UTC days; may differ by one day from the practice calendar for evening services". | Unchanged. | Statements can show an age one day off the practice's calendar, and on boundary days a different bucket; inconsistent with every other date after A1. |

## 5. Context: A1's other findings (no decision needed)

| Item | Cause | Fix in A1 |
|---|---|---|
| F-107 DOB one day early on the printed prescription | `PrintPreview.tsx` line 7: `new Date("1985-03-14")` is read as UTC midnight, the evening before in Chicago. The only client place that mis-formats a date-only value; other screens show DOB as sent. | Shared date-only formatter (the safe one already in `documentText.ts`). |
| F-118 "performed 2026-11-04" for a 11/3 10:42 PM procedure | `BillingService.Timeline.cs` line 38 prints the UTC day. | Practice-local day via `PracticeClock`. |
| Same class (found in A1 sweep) | `DiagnosisService.Structure.cs` line 117 "Periodontal chart of yyyy-MM-dd" uses the UTC day. | Same. |
| F-184 / F-218 "Entered" time moves | Every plan-item change appends a state row; the screen shows the newest row's time and actor as the answer's. Original rows intact. | Show the answer's own row ("Answer entered by X on T") and the last change separately. No schema change. |
| F-100 / Q-5an times ending in :00 | Both are server-clock times at save (`clock.UtcNow`); no user-entered time, no fixed clock in the API. | Recorded NOT_REPRODUCED with a test that seconds are kept. |
| F-030 | 5-hour gap = practice zone vs UTC. | Regression test. |

## 6. Requested from the reviewer

1. Choose A or C (or another approach).
2. If A: confirm it may be done inside ALV-N021 A1 as a template-version change to the ALV-007-C02 financial-document contract, with this proof: v1 reprint byte-identical for an issued document in the affected window (hash unchanged), v2 age by practice day across the evening window, both DST days and a year boundary, the preview-token version mismatch refused, and a negative control (building a v1 document with the v2 rule fails the reprint test).
3. If C: confirm that the known behaviour is recorded in A1's handoff and the billing documentation and that A1 closes without it.
