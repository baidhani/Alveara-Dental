# ALV-007-C02 R02 - Demo evidence

The walkthrough is `src/alveara-client/e2e/billing-statements-real-backend.spec.ts`: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh LocalDB database, one signed-in session per role (administrator, dentist, billing, front desk). 9 of 9 passed in the final sweep (the R02 walkthrough has the new source step) (`artifacts/regression/statements-run-output.txt`); its JSON results are `artifacts/walkthrough/billing-statements-evidence.json`. Axe (WCAG 2 A and AA) ran on seven states in both themes with 0 critical or serious violations. Every screenshot below was opened and checked.

| # | Screenshot (`artifacts/walkthrough/`) | What it shows |
|---|---|---|
| 01 | `01-refund-form-names-gaps-before-sending.png` | The refund form states, before anything is sent, that the money goes back to the original payer only, how much can still be refunded and that it is taken first from unapplied money and then from the newest applications; submitting empty names the gaps and sends nothing |
| 02 | `02-over-refund-refused-beside-the-amount.png` | An over-refund refused beside the amount with what was typed kept |
| 03 | `03-refund-recorded-releases-shown.png` | The refund recorded: the balance, the Refund entry linked to payment 3, the payment's standing (refunded $50.00), the charges it paid open again, and no Reverse or Correct button on a refunded payment |
| 04 | `04-move-refused-nothing-recorded.png` | A move to a charge with too little left refused beside its field; nothing recorded |
| 05 | `05-move-recorded.png` | The move recorded: the two charges' paid and open amounts changed, the balance unchanged |
| 06 | `06-receipt-issued.png` | A receipt for payment 5 previewed and issued (Receipt 1) and shown as issued |
| 07 | `07-payment-corrected.png` | Payment 5 corrected: shown as reversed by entry 6, the reversal, and the replacement (replaces entry 5) starting unapplied |
| 08 | `08-receipt-reprinted-as-issued.png` | Receipt 1 reprinted with the original $40.00 and a separate note that payment 5 has since been reversed |
| 09 | `09-timeline-and-aging.png` | What is open by age and the timeline in order, each charge naming the completed procedure behind it |
| 10 | `10-statement-preview.png` | The statement preview (not issued), addressed to the responsible party, with lines, balance, open charges by age and unapplied payments |
| 11 | `11-stale-preview-nothing-issued.png` | A name changed on another screen: issuing is refused as stale, the screen says nothing was issued and shows the new preview (with the new name) to review |
| 12 | `12-statement-print-view.png` | The print view (print media emulated): only the statement is drawn |
| 13 | `13-save-failed-nothing-recorded.png` | With the audit log made unwritable a refund records nothing, says so and keeps what was typed; the same request then works |
| 14 | `14-read-only-dentist.png` | A dentist reads receipts and statements, is offered no way to change anything, and the API answers 403 to a refund, a correction and an issue |
| 15 | `15-stale-second-screen-conflict.png` | An out-of-date second screen refused as a conflict; nothing recorded |
| 16 | `16-charge-source-for-a-billing-reader.png` | **R02:** the billing user opens "View source of charge 1" from the timeline: the procedure, the charge, and (these procedures were completed with the old state-only button) the plain statement that there is no completion record to show; there is no clinical-history link; the API still answers 403 to that user for the clinical record |

Also exercised by the walkthrough and not drawn in a screenshot: the statement exported as CSV through the API (9 rows: header, 7 entries, the total), the receipt's CSV refused, the issued-documents list, and the final ledger verification (`consistent: true`, no findings, balance $70.00, 8 entries, 4 allocations).

The first run of the walkthrough failed twice in axe (dark theme): the timeline time stamps and the CSV link had too little contrast. Both are fixed (the app's theme tokens and its theme-aware link class) and the final run is clean.
