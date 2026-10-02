# Prompt Library Report

**As of:** 2026-10-01

## Summary

This prompt library contains **5 reusable AI prompts** for automating financial tasks in the accounting application. Prompts are scored on a 0.0–1.0 scale; a score of 0.85+ indicates the prompt is ready for production use.

| Status | Count |
|--------|-------|
| **Library-Ready (0.85+)** | 3 |
| **Draft** | 2 |
| **Total** | 5 |

---

## Library-Ready Prompts

### 1. categorize-transaction
- **Score:** 1.00 (5/5 test cases passed)
- **Model:** claude-opus-5-5
- **What it does:** Classifies business transactions into five expense categories (Software, Meals & Entertainment, Office Supplies, Travel, Uncategorized).
- **For your project:** Automates the most repetitive accounting task—assigning category codes to each transaction. This frees accountants from manual categorization and ensures consistency.
- **Input:** Transaction description, amount, date
- **Output:** Category name

### 2. extract-receipt-data
- **Score:** 1.00 (5/5 test cases passed)
- **Model:** claude-opus-5-5
- **What it does:** Extracts key financial data from receipt or invoice text (vendor name, total amount, date, line item count).
- **For your project:** When accountants upload photos of receipts, this prompt pulls structured data automatically, so they don't have to re-type it.
- **Input:** Receipt text or OCR'd receipt image
- **Output:** Vendor, amount, date, item count (as JSON)

### 3. validate-invoice
- **Score:** 1.00 (5/5 test cases passed)
- **Model:** claude-sonnet-5
- **What it does:** Checks whether an invoice has all required fields (vendor name, address, invoice number, date, amount, line items) before it enters the accounting system.
- **For your project:** Acts as a quality gate—catches incomplete or malformed invoices before they create downstream errors during reconciliation or reporting.
- **Input:** Invoice details
- **Output:** Valid/invalid flag, list of missing fields

---

## Draft Prompts (Refinement in Progress)

### 4. flag-anomaly
- **Current Score:** Functional (risk scores 100% accurate; confidence reasons vary slightly)
- **Model:** claude-sonnet-5
- **What it does:** Detects suspicious or unusual transactions by comparing them to the business's spending history.
- **For your project:** Helps accountants spot data-entry errors, duplicate charges, or potential fraud before they're recorded.
- **Input:** Transaction amount, merchant, category, historical spending patterns
- **Output:** Risk score (1–5), brief reason
- **Status:** Risk detection logic is solid. Minor variations in confidence explanation text; approved for careful use. To move to library-ready: stabilize confidence explanations or simplify to boolean "flagged/not flagged."

### 5. detect-duplicate-transaction
- **Current Score:** 0.80 (4/5 test cases passed)
- **Model:** claude-opus-5-5
- **What it does:** Determines whether two transactions are the same purchase or two separate transactions (e.g., catches accidental duplicate charges).
- **For your project:** Prevents double-billing and catches processing errors that result in the same transaction appearing twice.
- **Input:** Two transaction records (vendor, amount, date each)
- **Output:** Duplicate yes/no, confidence 0.0–1.0
- **Status:** Core logic (is_duplicate) is 100% correct. Confidence scores vary by ~0.02–0.05 between runs; the determination itself is reliable. To move to library-ready: accept narrow confidence variance or run multiple times and average, or mark confidence as advisory only.

---

## Next Steps

### Short term (refine to library-ready):
1. **flag-anomaly:** Simplify the reason field or accept "confidence varies ±0.05" as an acceptable quirk.
2. **detect-duplicate-transaction:** Document that confidence scores are advisory (the binary duplicate detection is precise); or add averaging across multiple runs.

### Medium term (new prompts to add):
1. **reconcile-entry** — Match vendor invoices to bank statements and payments
2. **extract-vendor-info** — Parse and structure vendor details from invoices for the vendor database
3. **classify-deduction** — Classify business expenses as deductible or non-deductible (for tax planning)
4. **summarize-account** — Produce monthly account summaries with totals, top items, and anomalies

### Long term (system improvements):
- Store prompt scores in a versioned database rather than inline in test files
- Integrate scoring into CI/CD so scores auto-update on each run
- Add A/B testing harness for comparing prompt versions
- Set up dashboards tracking accuracy and latency of each prompt in production

---

## How to Use This Library

1. **For finance teams:** Prompts 1–3 are production-ready; use them to automate categorization, data extraction, and invoice validation.
2. **For accountants:** Prompts 4–5 are advisory/experimental; use them to flag transactions for manual review, but do not rely on them for final decisions yet.
3. **For developers:** Use `scripts/score_prompt.py` to evaluate any changes to a prompt. Run `scripts/check_library.py` to see which prompts are ready.

See `CONTRIBUTING.md` for instructions on adding new prompts to the library.
