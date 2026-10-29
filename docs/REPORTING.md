# Reporting and outcome measurement (ALV-N006)

Reports answer "what happened in the practice between these two days" from the authoritative records, never from a copy. Every number can be opened to list the records behind it, every total can be proved against its source, and a measured result is never described as more than the numbers show. This page is for someone who has to use, support or change the reports.

## Who may see what

Each group of reports has its own permission. A person sees only the groups they hold, and the server checks the permission again on every call (including exports and drill-downs).

| Permission | Holds it by default | Reports |
|---|---|---|
| `ViewOperationalReports` | Admin, OfficeManager, FrontDesk | Schedule, Patient flow, Recall and follow-up, Cancellations and no-shows |
| `ViewFinancialReports` | Admin, OfficeManager, Billing | Production and charges, Collections and payments, Balances |
| `ViewClinicalReports` | Admin, OfficeManager, Dentist | Treatment-plan status, Completed procedures, Provider activity |
| `ViewMeasurementReports` | Admin, OfficeManager | Measurement and baselines (metrics, baselines, comparison) |
| `ManageMeasurementBaselines` | Admin, OfficeManager | Setting a baseline |

None is protected: an administrator can change them in the permission editor. The financial and clinical groups are separate on purpose (billing never sees clinical detail; the dentist never sees money).

## The reports

All take a practice-local date range (`from`, `to`, `yyyy-MM-dd`, both required, at most 366 days). Dates are the practice's days in its own time zone, converted to a half-open UTC range, so a day is never split or counted twice. Provider and location filters apply only where they are honest; elsewhere the server refuses them with `filter_not_supported` and the hub does not send them.

| Report | What it counts | Provider / location filter |
|---|---|---|
| Schedule | appointments starting in the range by status, day and provider, and reschedule events | provider and location |
| Patient flow | the same appointments by visit state, and the transitions between states | provider and location |
| Recall and follow-up | follow-up tasks due in the range by outcome (done, done late, open overdue, ...) | none |
| Cancellations and no-shows | cancelled and no-show appointments by lead time, provider and appointment type | provider and location |
| Production and charges | charges, charge reversals and net production, by provider credited and procedure code | provider |
| Collections and payments | payments, reversals, refunds and net collections, by method | none |
| Balances | what patients owed at the start and end of the range and the change between | none |
| Treatment-plan status | plans and items by acceptance, scheduling and completion | none |
| Completed procedures | procedures completed by day, provider and code | provider |
| Provider activity | booked, cancelled, no-show, visits completed and procedures completed per provider | provider |

Reading is consistent: when the database allows snapshot isolation a report is read inside one snapshot, so its numbers agree with each other even while the practice works; otherwise it is read table by table, and the report says so (`readConsistency`). A range with nothing in it says "nothing to report" instead of a screen of zeros.

### Proof that totals are right

* **Financial reports** classify every ledger entry into exactly one line (charges, charge reversals, payments, payment reversals, refunds, refund reversals, adjustments, adjustment reversals, loaded opening balances, unclassified) and show the lines next to an independent sum of the ledger for the same entries; the report says whether they agree. A loaded opening balance is its own line and is never production or collections.
* **Operational and clinical reports** carry a `reconciled` flag: the parts add up to the whole, and each drill-down total equals the cell it came from because both use the same predicate.
* A report that would read more than 200,000 source rows is refused (`report_too_large`, 413) rather than computed from a cut-off set.

### Drilling down

Any number that is not zero opens the records behind it: ids, times and statuses only, never a name, a note or a free-text value. Lists page by keyset (no skipped or repeated rows) and ask for 100 rows at a time (at most 500).

## Measurement, baselines and comparison

The measurement page shows seven measured metrics, each as *counted out of what* with a definition version:

| Metric | Counted | Out of |
|---|---|---|
| Scheduling conflicts | booking requests refused because the provider, patient or room was taken | booking requests booked or refused |
| Billing corrections | ledger entries that reverse an earlier entry | ledger entries that are not reversals (opening balances left out) |
| Duplicate patient merges | merges completed | patients registered |
| Clinical notes not finalized | notes still not finalized | notes at least 7 days old |
| Follow-ups not done on time | tasks open or completed after their due time | tasks due in the period whose due time has passed |
| Cancellations | appointments cancelled | appointments starting in the period |
| No-shows | appointments marked no-show | appointments starting in the period |

A metric with nothing in its denominator shows **Not available**, never 0%.

**Not yet measurable**, with the reason, and never given a number: time spent entering the same information twice, time staff spend on nonclinical administrative work, and the reduction in nonclinical overhead. The 20 to 30 percent overhead reduction is a product target, not a measurement.

**Definition versions.** Each metric's definition has a version. Anything that changes what a number means (the numerator, the denominator, the source or a rule) must raise it. The hand-checked values pinned in `MetricsTests` fail when a definition changes without the version changing.

**Baselines.** A baseline is a period that is already over, measured once and stored with each metric's definition version. It cannot be edited or deleted (database triggers refuse both) and a later correction to the data does not change it. Setting one needs `ManageMeasurementBaselines`, a CSRF token and an `Idempotency-Key`; pressing Save twice makes one baseline.

**Comparison.** The page compares the period with the latest baseline (or a chosen one). A change is shown for a metric only if all of these hold, otherwise the metric shows its own value, the reason in words and no change:

1. the baseline period ended before this period started;
2. the metric has the same definition version in both;
3. both denominators hold at least 30 records (`Reporting:MinimumComparisonSample`).

A valid change is worded **Higher** or **Lower** (or **The same**), by percentage points and as a share of the baseline rate. It is never called an improvement, and every comparison carries the sentence: *a difference between two periods shows that the numbers differ; it does not show what caused the difference.* With no baseline at all the page says so and shows no change.

## Export and print

Every report has a **Download CSV** button (and the comparison has its own). The file is made by the server from the same report the screen shows, with the same filter, so its numbers are the screen's. The first lines say exactly what the file covers (report, definition version, period, time zone, provider-filter count, when made, how read), then the summary, then one table per list.

* Needs the same permission as the report; the server checks it again.
* Each export is recorded in the audit log (`ReportExported`: who, which report, days, lines; never a value). If it cannot be recorded, no file is handed over (503 `audit_unavailable`). A run is recorded as `ReportViewed`, so an export leaves two entries.
* A file over `Reporting:MaxExportRows` (default 50,000 lines) is refused (413 `export_too_large`), never cut.
* Text that starts with `=`, `+`, `-`, `@`, a tab or a return gets a leading apostrophe so a spreadsheet cannot run it as a formula; numbers are never changed. The file is UTF-8 with a byte-order mark, CRLF lines.
* **Print** opens the browser's print dialog; the menu, the filter bar and the buttons are left off the paper.

## The API

All routes are `GET` under `/api/reports` except setting a baseline. They return `no-store`, and a refusal carries a stable `error` code the screens turn into words.

* Reports: `schedule`, `patient-flow`, `follow-ups`, `cancellations`, `production`, `collections`, `balances`, `treatment-plans`, `completed-procedures`, `provider-activity`, `measurement/metrics`, `measurement/comparison`. Add `/export` for the CSV.
* Drill-downs: `schedule/appointments`, `patient-flow/appointments`, `follow-ups/tasks`, `cancellations/appointments`, `production/entries`, `collections/entries`, `balances/accounts`, `treatment-plans/items`, `completed-procedures/procedures`, `provider-activity/appointments`, `provider-activity/procedures`.
* Baselines: `GET measurement/baselines`, `GET measurement/baselines/{id}`, `POST measurement/baselines` (`name`, `from`, `to`; `Idempotency-Key` header).
* Stable codes include `dates_required`, `invalid_range`, `range_too_large`, `too_many_providers`, `unknown_provider`, `unknown_location`, `filter_not_supported`, `report_too_large`, `export_too_large`, `audit_unavailable`, `period_not_complete`, `key_reused`.

## Failure modes

| What fails | What happens |
|---|---|
| The audit entry cannot be saved | the report or file is not returned (503 `audit_unavailable`); nothing is delivered that cannot be accounted for |
| The range is too long, or reads too many rows | refused with a stable code; nothing is shortened silently |
| The database is not in snapshot mode | the report reads table by table and says so |
| A baseline save is repeated | the same baseline is returned (idempotent by `Idempotency-Key`); a key reused for a different baseline is refused (`key_reused`) |
| Two periods cannot honestly be compared | no change is shown; the reason is |

Not handled: forecasting, benchmarking against other practices and predicting; those are later work. The reports never state or imply that an operational change caused a result.

## Settings

`Reporting:MaxRangeDays` (366), `Reporting:MaxProviders` (50), `Reporting:MaxSourceRows` (200,000), `Reporting:MaxExportRows` (50,000), `Reporting:MinimumComparisonSample` (30), `Reporting:DefaultDrillRows` (100), `Reporting:MaxDrillRows` (500).

## How it is verified

* Backend: reconciliation, filter and authorization tests for every report (`OperationalReports*`, `FinancialReports*`, `BalancesReportTests`, `ClinicalReports*`, `ProviderActivityTests`, `FollowUpReportTests`, `CancellationReportTests`), metric and definition-version tests (`MetricsTests`), baseline and comparison tests including the missing-baseline cases (`MeasurementBaselineTests`, `MeasurementApiTests`), export tests (`ReportCsvTests`, `ReportExportApiTests`) and the performance smoke test (`ReportScaleTests`: a year of 4,380 appointments and 5,000 ledger entries; fixed small number of database commands per report; timings judged only when `ALVERA_PERF_EVIDENCE` names an output folder).
* Client: the hub, every view, the measurement panel and the export actions (`ReportsHubPage.test.tsx`, `OperationalViews.test.tsx`, `FinancialClinicalViews.test.tsx`, `MeasurementView.test.tsx`, `ReportActions.test.tsx`), each with an axe check.
