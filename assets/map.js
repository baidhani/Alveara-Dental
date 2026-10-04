/* Alveara Dental Command Center - Architecture & Execution Map tab.
   Everything on the map is read at runtime from the same files the other tabs read:
     .alveara/EXECUTION_STATUS.json  status, acceptance counts, commits, review, phase, dependencies, gate, story type
     .colaberry/plan.json            titles of the portal's stories
     .colaberry/progress.json        how many criteria of a portal story pass so far
     .alveara/story_catalog.json     the two display facts no live file carries: a subsystem per story and a title for stories not in the plan
   Nothing about a story's progress is written in this file. The architecture overview below is a description of the product's layers, not data.
   Relies on app.js for `state` and `escapeHtml`; both are only used when the tab is drawn, after app.js has loaded. */

const MAP_TYPE_LABELS = { course: "Portal story", companion: "Engineering companion", new_production: "Engineering story" };
const MAP_TYPE_MARKERS = { course: "PORTAL", companion: "COMPANION", new_production: "ENGINEERING" };
const MAP_TYPE_ICONS = { course: "\u2691", companion: "\u26D3", new_production: "\u2699" }; // flag, chain, gear

/** A story type's marker: its own icon and colour, with the type written as a word so the type never depends on colour or on the icon being drawn. */
function mapMarker(storyType) {
  const icon = MAP_TYPE_ICONS[storyType];
  return `<span class="map-marker ${escapeHtml(storyType)}">${icon ? `<span aria-hidden="true">${icon}</span> ` : ""}${escapeHtml(MAP_TYPE_MARKERS[storyType] || "STORY")}</span>`;
}
const MAP_STATUS_LABELS = {
  PLANNED: "Planned", AWAITING_REVIEW: "Awaiting review", CHANGES_REQUIRED: "Changes required", BLOCKED: "Blocked", REOPENED: "Reopened", COMPLETE: "Complete",
};
const MAP_FILTERS = [
  { key: "phase", label: "Phase", all: "All phases" },
  { key: "type", label: "Story type", all: "All story types" },
  { key: "subsystem", label: "Subsystem", all: "All subsystems" },
  { key: "gate", label: "Quality gate", all: "All gates" },
  { key: "state", label: "Delivery state", all: "All delivery states" },
];
const MAP_LAYERS = [
  { name: "Experience", text: "Application shell, role-aware navigation, patient workspace, scheduler, visit board, clinical and administrative workflows" },
  { name: "Domain and API", text: "Authentication, RBAC, audit, patient and household, scheduling, encounters, clinical records, safety, finance and operations APIs" },
  { name: "Durable platform", text: "EF Core data store, migrations, immutable lifecycle controls, background jobs, encrypted backup, document storage, release and rollback controls" },
];

const mapUi = { filters: { phase: "", type: "", subsystem: "", gate: "", state: "" }, architectureOpen: false, rows: [] };

/** One display record per ledger record, in ledger order: the ledger's own facts plus the joined title, subsystem and (for portal stories) criteria progress. */
function mapRecords() {
  const records = (state.ledger?.records || []).slice().sort((a, b) => (a.num ?? 0) - (b.num ?? 0));
  const catalog = new Map((state.catalog?.stories || []).map((s) => [s.storyId, s]));
  const planTitles = new Map((state.plan?.stories || []).map((s) => [s.id, s.title]));
  const progress = new Map((state.progress?.stories || []).map((s) => [s.id, s]));
  const gateless = "Pre-gate";
  return records.map((r) => {
    const entry = catalog.get(r.storyId);
    const prog = r.storyType === "course" ? progress.get(r.storyId) : null;
    const criteriaPassed = prog ? (prog.criteria || []).filter((c) => c.passed).length : 0;
    const criteriaTotal = prog ? (prog.criteria || []).length || r.acceptance?.total || 0 : 0;
    // A portal story the portal has not yet verified but whose criteria have started to pass is in progress; every other state is the ledger's own word.
    const inProgress = r.status === "PLANNED" && criteriaPassed > 0;
    return {
      ...r,
      title: planTitles.get(r.storyId) || entry?.title || null,
      subsystem: entry?.subsystem || "Unclassified",
      gate: (r.qualityGateContribution || []).length ? r.qualityGateContribution.join(", ") : gateless,
      typeLabel: MAP_TYPE_LABELS[r.storyType] || r.storyType,
      stateLabel: inProgress ? "In progress" : MAP_STATUS_LABELS[r.status] || r.status,
      stateClass: inProgress ? "progress" : (r.status || "").toLowerCase().replace(/_/g, "-"),
      criteriaPassed,
      criteriaTotal,
    };
  });
}

function mapFieldValue(r, key) {
  return { phase: r.phase, type: r.typeLabel, subsystem: r.subsystem, gate: r.gate, state: r.stateLabel }[key];
}

function mapVisible(r) {
  return MAP_FILTERS.every((f) => !mapUi.filters[f.key] || mapUi.filters[f.key] === mapFieldValue(r, f.key));
}

/** Phases in the order the ledger first reaches them, so a new phase appears without code changes. */
function mapPhaseOrder(rows) {
  const first = new Map();
  rows.forEach((r) => { if (!first.has(r.phase)) first.set(r.phase, r.num ?? 0); });
  return [...first.entries()].sort((a, b) => a[1] - b[1]).map((e) => e[0]);
}

/** Legend keys that double as filters: one per delivery state and story type that exists in the data. Clicking one applies that filter; clicking it again clears it. */
function mapLegend(rows) {
  const states = new Map();
  rows.forEach((r) => { if (!states.has(r.stateLabel)) states.set(r.stateLabel, r.stateClass); });
  const types = new Map();
  rows.forEach((r) => { if (!types.has(r.typeLabel)) types.set(r.typeLabel, r.storyType); });
  const button = (filter, value, inner) =>
    `<button type="button" class="map-legend-button" data-map-legend="${filter}" data-map-value="${escapeHtml(value)}" aria-pressed="${mapUi.filters[filter] === value}">${inner}</button>`;
  const stateButtons = [...states.entries()].map(([label, cls]) => button("state", label, `<i class="map-dot ${escapeHtml(cls)}" aria-hidden="true"></i>${escapeHtml(label)}`));
  const typeButtons = [...types.entries()].map(([label, type]) =>
    button("type", label, `${mapMarker(type)} ${escapeHtml(label.toLowerCase())}`));
  return `<div class="map-legend" role="group" aria-label="Legend: select a key to filter the map">${[...stateButtons, ...typeButtons].join("")}</div>`;
}

function mapCard(r) {
  const title = r.title ? escapeHtml(r.title) : `<em>No title. Add ${escapeHtml(r.storyId)} to .alveara/story_catalog.json.</em>`;
  const progress = r.criteriaTotal ? ` <span class="map-badge">${r.criteriaPassed} of ${r.criteriaTotal} criteria</span>` : "";
  return `
    <button type="button" class="map-node ${escapeHtml(r.stateClass)}" data-map-story="${escapeHtml(r.storyId)}" aria-haspopup="dialog">
      <span class="map-node-head"><span class="map-order">ITEM ${String(r.num).padStart(2, "0")}</span>${mapMarker(r.storyType)}</span>
      <span class="map-id">${escapeHtml(r.storyId)}</span>
      <span class="map-title">${title}</span>
      <span class="map-badges"><span class="map-badge">${escapeHtml(r.subsystem)}</span><span class="map-badge">${escapeHtml(r.gate)}</span>${progress}</span>
      <span class="map-state">${escapeHtml(r.stateLabel)}</span>
    </button>`;
}

function renderMap() {
  const heading = `<h1 class="tab-title">Architecture &amp; Execution Map</h1>`;
  if (!state.ledger) {
    return `${heading}<div class="empty-state">The engineering ledger (.alveara/EXECUTION_STATUS.json) could not be loaded, so the map cannot be drawn. Nothing is shown rather than a stale copy.</div>`;
  }
  const rows = mapRecords();
  mapUi.rows = rows;
  const completions = rows.map((r) => r.completedAt).filter(Boolean).sort();
  const latest = completions.length ? new Date(completions[completions.length - 1]).toLocaleDateString(undefined, { year: "numeric", month: "long", day: "numeric" }) : "none yet";
  const missing = rows.filter((r) => !r.title || r.subsystem === "Unclassified").length;
  const catalogNote = !state.catalog
    ? `<div class="detail-panel map-warning" role="status">The story catalog (.alveara/story_catalog.json) could not be loaded, so subsystems are shown as Unclassified and engineering story titles are missing.</div>`
    : missing
      ? `<div class="detail-panel map-warning" role="status">${missing} ${missing === 1 ? "story has" : "stories have"} no catalog entry or title. They are marked on the map.</div>`
      : "";

  // an option that no longer exists in the data (for example after a sync) must not leave an invisible filter on
  MAP_FILTERS.forEach((f) => {
    if (mapUi.filters[f.key] && !rows.some((r) => mapFieldValue(r, f.key) === mapUi.filters[f.key])) mapUi.filters[f.key] = "";
  });
  const visible = rows.filter(mapVisible);
  const selects = MAP_FILTERS.map((f) => {
    const values = [...new Set(rows.map((r) => mapFieldValue(r, f.key)))];
    return `<label>${f.label}<select id="map-filter-${f.key}" data-map-filter="${f.key}"><option value="">${f.all}</option>${values
      .map((v) => `<option${mapUi.filters[f.key] === v ? " selected" : ""}>${escapeHtml(v)}</option>`).join("")}</select></label>`;
  }).join("");
  const counts = new Map();
  visible.forEach((r) => counts.set(r.stateLabel, (counts.get(r.stateLabel) || 0) + 1));
  const stats = [`<span class="map-stat">${visible.length} visible ${visible.length === 1 ? "story" : "stories"}</span>`,
    ...[...counts.entries()].map(([label, n]) => `<span class="map-stat">${n} ${escapeHtml(label.toLowerCase())}</span>`),
    `<button type="button" class="map-reset" data-map-reset>Reset filters</button>`].join("");
  const phases = mapPhaseOrder(rows).map((p) => {
    const inPhase = visible.filter((r) => r.phase === p);
    return inPhase.length ? `<section class="map-phase" aria-label="${escapeHtml(p)}"><h3>${escapeHtml(p)} <small>(${inPhase.length})</small></h3>${inPhase.map(mapCard).join("")}</section>` : "";
  }).join("");
  const layers = MAP_LAYERS.map((l) => `<div class="map-layer"><strong>${escapeHtml(l.name)}</strong><span>${escapeHtml(l.text)}</span></div>`).join("");
  const open = mapUi.architectureOpen;

  return `
    ${heading}
    <p class="tab-desc">Every portal and engineering story in the first-release plan, read live from the execution ledger, the portal's plan and progress files, and the story catalog. Latest completion recorded: ${escapeHtml(latest)}. Filters change what is shown here only.</p>
    ${catalogNote}
    <div class="detail-panel">
      <div class="map-filters">${selects}</div>
      <div class="map-stats" role="status" aria-live="polite">${stats}</div>
      ${mapLegend(rows)}
    </div>
    <div class="detail-panel">
      <div class="map-section-head"><h2>Application architecture</h2><button type="button" class="map-toggle" data-map-architecture aria-expanded="${open}" aria-controls="map-architecture">${open ? "Hide architecture" : "Show architecture"}</button></div>
      <div id="map-architecture" class="map-architecture"${open ? "" : " hidden"}>${layers}</div>
    </div>
    <h2 class="map-flow-title">Execution flow</h2>
    <div class="map-flow">${phases || `<div class="empty-state">No story matches these filters.</div>`}</div>
    <dialog id="map-dialog" class="map-dialog" aria-labelledby="map-dialog-title"></dialog>`;
}

function mapDetailHtml(r) {
  const deps = (r.dependencies || []).length ? r.dependencies.map((d) => `<span class="map-dep">${escapeHtml(d)}</span>`).join("") : "No predecessor";
  const sha = (v) => (v ? `<code>${escapeHtml(String(v).slice(0, 7))}</code>` : "—");
  const review = r.review?.decision && r.review.decision !== "pending"
    ? r.review.decision.replace(/_/g, " ") + (r.review.decisionArtifact ? ` (${r.review.decisionArtifact})` : "")
    : "—";
  const tests = r.tests?.summary ? `<dt>Tests</dt><dd>${escapeHtml(r.tests.summary)}</dd>` : "";
  const completed = r.completedAt ? new Date(r.completedAt).toLocaleString() : "—";
  const criteria = r.criteriaTotal ? `${r.criteriaPassed} of ${r.criteriaTotal} criteria passing (portal progress file)` : `${r.acceptance?.passed ?? 0} of ${r.acceptance?.total ?? 0} (ledger)`;
  const blocking = (r.blockingIssues || []).length ? `<dt>Blocking issues</dt><dd>${r.blockingIssues.map((b) => escapeHtml(typeof b === "string" ? b : b.summary || JSON.stringify(b))).join("; ")}</dd>` : "";
  return `
    <button type="button" class="map-close" data-map-close aria-label="Close story details">&times;</button>
    <div class="map-type-row"><span class="map-order">ITEM ${String(r.num).padStart(2, "0")}</span> ${mapMarker(r.storyType)}</div>
    <h2 id="map-dialog-title">${escapeHtml(r.storyId)} — ${r.title ? escapeHtml(r.title) : "No title in the catalog"}</h2>
    <dl class="map-detail-grid">
      <dt>Execution order</dt><dd>#${escapeHtml(r.num)}</dd>
      <dt>Phase</dt><dd>${escapeHtml(r.phase)}</dd>
      <dt>Story type</dt><dd>${escapeHtml(r.typeLabel)}${r.optional ? " · Optional" : ""}${r.parentCourseStory ? ` · extends ${escapeHtml(r.parentCourseStory)}` : ""}</dd>
      <dt>Subsystem</dt><dd>${escapeHtml(r.subsystem)}</dd>
      <dt>Quality gate</dt><dd>${escapeHtml(r.gate)}</dd>
      <dt>Delivery state</dt><dd><strong>${escapeHtml(r.stateLabel)}</strong>${r.attempt ? ` · attempt ${escapeHtml(r.attempt)}` : ""}</dd>
      <dt>Dependencies</dt><dd class="map-deps">${deps}</dd>
      <dt>Acceptance</dt><dd>${escapeHtml(criteria)}</dd>
      <dt>Review</dt><dd>${escapeHtml(review)}</dd>
      ${tests}
      <dt>Implementation</dt><dd>${sha(r.implementationCommit)}</dd>
      <dt>Evidence</dt><dd>${sha(r.evidenceCommit)}</dd>
      <dt>Completed</dt><dd>${escapeHtml(completed)}</dd>
      ${blocking}
    </dl>`;
}

/** Fills a <dialog> with one story's details and shows it modally; Escape, the close button or a click outside close it, and focus returns to what opened it. */
function showStoryDialog(dialog, r, opener) {
  dialog.innerHTML = mapDetailHtml(r);
  dialog.className = `map-dialog ${r.stateClass}`;
  dialog.querySelector("[data-map-close]").addEventListener("click", () => dialog.close());
  dialog.addEventListener("click", (event) => { if (event.target === dialog) dialog.close(); }, { once: true });
  dialog.addEventListener("close", () => { if (opener?.isConnected) opener.focus(); }, { once: true });
  dialog.showModal();
}

/** Opens the story dialog from outside the map (Project Management). The dialog is made for the moment and removed when it closes. */
function openStoryDialog(storyId, opener) {
  const r = mapRecords().find((x) => x.storyId === storyId);
  if (!r) return;
  const dialog = document.createElement("dialog");
  dialog.id = "map-dialog";
  dialog.setAttribute("aria-labelledby", "map-dialog-title");
  document.body.appendChild(dialog);
  dialog.addEventListener("close", () => dialog.remove());
  showStoryDialog(dialog, r, opener);
}

/** Binds the controls drawn by renderMap(); called by app.js after the tab's HTML is in the page. */
function wireMap(root) {
  const redraw = (focusSelector) => {
    renderTabContent();
    if (focusSelector) document.querySelector(focusSelector)?.focus(); // the page is redrawn, so put keyboard focus back where the person was
  };
  root.querySelectorAll("[data-map-filter]").forEach((select) => {
    select.addEventListener("change", () => {
      mapUi.filters[select.dataset.mapFilter] = select.value;
      redraw(`#map-filter-${select.dataset.mapFilter}`);
    });
  });
  root.querySelectorAll("[data-map-legend]").forEach((button) => {
    button.addEventListener("click", () => {
      const filter = button.dataset.mapLegend;
      const value = button.dataset.mapValue;
      mapUi.filters[filter] = mapUi.filters[filter] === value ? "" : value;
      redraw(`[data-map-legend="${filter}"][data-map-value="${CSS.escape(value)}"]`);
    });
  });
  root.querySelector("[data-map-reset]")?.addEventListener("click", () => {
    Object.keys(mapUi.filters).forEach((k) => { mapUi.filters[k] = ""; });
    redraw("#map-filter-phase");
  });
  root.querySelector("[data-map-architecture]")?.addEventListener("click", () => {
    mapUi.architectureOpen = !mapUi.architectureOpen;
    redraw("[data-map-architecture]");
  });
  const dialog = root.querySelector("#map-dialog");
  root.querySelectorAll("[data-map-story]").forEach((card) => {
    card.addEventListener("click", () => {
      const r = mapUi.rows.find((x) => x.storyId === card.dataset.mapStory);
      if (!r || !dialog) return;
      showStoryDialog(dialog, r, card);
    });
  });
}
