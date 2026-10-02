# Retail Analytics Workflow Plan

## Overview
Automate daily retail performance monitoring by analyzing sales anomalies, inventory issues, and customer trends. Route findings to appropriate stakeholders with a human safety gate for edge cases.

## Stage 1: Input

**Source:** Daily sales data feed + exception notes
- Sales performance metrics (store, category, revenue, units, margin)
- Customer feedback or complaints
- Inventory alerts and supply chain issues
- Promotional performance notes
- Staff availability/scheduling notes

**Format:** Structured data (CSV/JSON) or unstructured text notes from store managers and systems
**Frequency:** Daily, ingested each morning
**Volume:** 50-200 records per day

---

## Stage 2: Structured Output

Claude analyzes each sales finding and returns:

| Field | Type | Purpose |
|-------|------|---------|
| **issue_type** | Category | What happened (stockout, fraud, demand_surge, low_conversion, competitor_activity, quality_issue, staffing_problem) |
| **urgency** | high/medium/low | Does this need action today? |
| **affected_area** | Store/Region/Category | Where this matters |
| **root_cause_hypothesis** | Text | Why Claude thinks this happened |
| **recommended_action** | Text | What should be done |
| **owner** | Team | Who should handle it (Store Manager, Category Manager, Supply Chain, Finance, Loss Prevention) |
| **impact** | Revenue estimate | Rough financial or operational impact |
| **confidence** | 0-1 | How certain Claude is about this analysis |

---

## Stage 3: Quality Gate

**Auto-Cleared (proceed without review):**
- Medium/Low urgency items with confidence > 80%
- Routine findings with clear patterns (demand trends, standard promotional analysis)
- These get logged and summarized in the daily dashboard

**Flagged for Human Review:**
- **HIGH urgency** (any confidence level) — someone needs to act immediately
- **Low confidence** (<75%) — Claude is unsure about root cause or recommendation
- **Ambiguous patterns** — could indicate fraud, could be a data anomaly

**When flagged:** Item lands in analyst queue with Claude's analysis visible for context. Analyst confirms/corrects the categorization and decides next step.

---

## Stage 4: Deliverable

**Output format:** Daily analytics report (one page for leadership, detailed queue for operations)

- **Executive Summary:** 3-5 key findings flagged as HIGH
- **Auto-Cleared Summary:** "27 routine findings logged (promotional lift, seasonal inventory adjustments, etc.)"
- **Analyst Queue:** 5-8 flagged items requiring human judgment with Claude's analysis visible
- **Metrics:** Total findings processed, auto-cleared rate, average analysis time, confidence distribution

**Who sees what:**
- Store managers → their store-specific findings and recommended actions
- Category managers → trend analysis and competitive alerts for their categories
- Supply chain → stock-out risks and demand shifts
- Leadership → exception report (only HIGH urgency or new patterns)

---

## Success Criteria

- Reduce daily analytics review time from 2 hours to 30 minutes (analyst focus on flagged items only)
- Catch 95%+ of true anomalies (fraud, quality issues, critical stockouts)
- Analyst confidence in recommendations > 90% (high confidence threshold keeps false positives low)
- Routine findings auto-clear without human review
- Mistakes surface clearly (if Claude misses something, it's logged with the next day's analysis)
