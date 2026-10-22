/**
 * ALV-CONTROL-C01: the one place that gives every execution-control status a human-readable label.
 *
 * The authoritative set of statuses is section 1 of .alveara/HANDOFF_SCHEMA.md. Every status it allows has an entry here; the repository test
 * tests/status-labels.test.mjs and the Architecture Map spec fail if the schema gains a status without a label, or this table gains one the schema does not allow.
 * The Architecture Map (map.js) and the Engineering stories list (app.js) both read this table, so a status reads the same everywhere. A status that is somehow not
 * in the table is still shown (as its raw text) rather than hidden, so the page never loses a story.
 */
const EXECUTION_STATUS_LABELS = Object.freeze({
  PLANNED: "Planned",
  READY: "Ready",
  IN_PROGRESS: "In progress",
  AWAITING_REVIEW: "Awaiting review",
  AWAITING_REGRESSION: "Awaiting regression",
  COMPLETE: "Complete",
  BLOCKED: "Blocked",
  CHANGES_REQUIRED: "Changes required",
  DEFERRED: "Deferred",
  REOPENED: "Reopened",
  REVALIDATION_REQUIRED: "Revalidation required",
});
