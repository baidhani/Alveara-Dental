/* Alveara Dental Command Center
   Reads .colaberry/plan.json, progress.json, manifest.json at runtime.
   Nothing here hard-codes plan content — sample mode uses its own bundled sample files. */

const TABS = [
  { id: "overview", label: "Overview", built: true },
  { id: "outcomes", label: "Outcomes", built: true },
  { id: "users", label: "Users & Use Case", built: true },
  { id: "guardrails", label: "Guardrails", built: true },
  { id: "systems", label: "Systems", built: true },
  { id: "pm", label: "Project Management", built: true },
  { id: "agents", label: "AI Agents", built: true },
  { id: "kb", label: "Knowledge Base", built: true },
  { id: "datamodel", label: "Data Model", built: true },
];

// Story ownership is a fact about our own backlog (from the Execution Plan), not something
// plan.agents[] carries yet. Kept separate from fetched data on purpose — see Tab 7.
const STORY_OWNERS = [
  { name: "Security Team", owns: ["STORY-001"] },
  { name: "Audit Team", owns: ["STORY-002"] },
  { name: "Patient Management Team", owns: ["STORY-003"] },
  { name: "Scheduling Team", owns: ["STORY-004"] },
  { name: "Clinical Documentation Team", owns: ["STORY-005", "STORY-006", "STORY-008"] },
  { name: "Billing Team", owns: ["STORY-007"] },
  { name: "Follow-up Team", owns: ["STORY-009"] },
  { name: "Document Management Team", owns: ["STORY-010"] },
  { name: "Receptionist", owns: ["STORY-011"] },
  { name: "Dentist", owns: ["STORY-012", "STORY-015"] },
  { name: "Clinician", owns: ["STORY-013", "STORY-016"] },
  { name: "Billing Manager", owns: ["STORY-014"] },
  { name: "System Administrator", owns: ["STORY-017"] },
  { name: "Data Manager", owns: ["STORY-018"] },
];

// Starting data-model sketch derived from the requirements register. Not final — Tab 9 says so.
const DATA_MODEL_ENTITIES = [
  { name: "User Account", note: "Login identity — distinct from a staff or provider profile.", fields: ["id", "username", "password_hash", "session_timeout_minutes", "role_ids[]"] },
  { name: "Role", note: "RBAC role: dentist, hygienist, assistant, front desk, billing, office manager, admin.", fields: ["id", "name", "permissions[]"] },
  { name: "Audit Event", note: "Records account/role changes with user and timestamp.", fields: ["id", "actor_user_id", "action", "target_id", "occurred_at"] },
  { name: "Patient", note: "Demographics, contact info, household relationships.", fields: ["id", "name", "date_of_birth", "contact_info", "household_id"] },
  { name: "Household", note: "Groups related patients/guarantors.", fields: ["id", "members[]", "guarantor_patient_id"] },
  { name: "Provider", note: "Clinical provider profile — linked to, not identical with, a user account.", fields: ["id", "user_id", "name", "specialty"] },
  { name: "Operatory", note: "Physical treatment room/chair used for scheduling.", fields: ["id", "name"] },
  { name: "Appointment", note: "Scheduled visit — provider, operatory, type, duration.", fields: ["id", "patient_id", "provider_id", "operatory_id", "type", "starts_at", "ends_at", "state"] },
  { name: "Clinical Note", note: "Structured medical/dental history, allergies, medications.", fields: ["id", "patient_id", "encounter_id", "content", "signed_at"] },
  { name: "Odontogram Entry", note: "Tooth/surface state: existing, diagnosed, planned, completed.", fields: ["id", "patient_id", "tooth", "surface", "state"] },
  { name: "Perio Chart Entry", note: "Probing depth, recession, bleeding per site.", fields: ["id", "patient_id", "tooth", "site", "probing_depth", "recession", "bleeding"] },
  { name: "Diagnosis", note: "Linked to patient, encounter, and treatment plan.", fields: ["id", "patient_id", "encounter_id", "code", "description"] },
  { name: "Procedure Code", note: "Fee catalog entry.", fields: ["id", "code", "description", "fee"] },
  { name: "Treatment Plan", note: "Proposed procedures with fee estimates, linked to diagnoses.", fields: ["id", "patient_id", "diagnosis_ids[]", "proposed_procedure_ids[]", "estimated_total"] },
  { name: "Charge", note: "Billing charge generated from a completed procedure.", fields: ["id", "patient_id", "procedure_id", "amount", "created_at"] },
  { name: "Payment", note: "Payment/adjustment against a patient's financial history.", fields: ["id", "patient_id", "amount", "kind", "created_at"] },
  { name: "Document", note: "Imported/categorized document or form, with e-signature.", fields: ["id", "patient_id", "category", "signed_by", "signed_at"] },
  { name: "Prescription", note: "Medication, dosage, allergy-checked.", fields: ["id", "patient_id", "medication", "dosage", "allergy_check_passed"] },
  { name: "Recall Task", note: "Follow-up task with recall interval and reminder history.", fields: ["id", "patient_id", "due_on", "completed_at"] },
];

const state = {
  mode: "sample", // "sample" | "real"
  activeTab: "overview",
  plan: null,
  progress: null,
  manifest: null,
  dataMissing: false, // true when real mode found no .colaberry data files
  theme: "light", // "light" | "dark"
};

function initTheme() {
  let saved = null;
  try {
    saved = localStorage.getItem("alveara-theme");
  } catch (err) {
    /* localStorage unavailable — fall back to system preference each load */
  }
  if (saved === "light" || saved === "dark") {
    state.theme = saved;
  } else {
    state.theme = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light";
  }
  applyTheme();
}

function applyTheme() {
  document.documentElement.setAttribute("data-theme", state.theme);
}

function renderThemeToggle() {
  const el = document.getElementById("theme-toggle");
  el.innerHTML = `
    <button data-theme-choice="light" class="${state.theme === "light" ? "active" : ""}">☀ Light</button>
    <button data-theme-choice="dark" class="${state.theme === "dark" ? "active" : ""}">🌙 Dark</button>
  `;
  el.querySelectorAll("button").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.theme = btn.dataset.themeChoice;
      applyTheme();
      try {
        localStorage.setItem("alveara-theme", state.theme);
      } catch (err) {
        /* non-fatal: theme choice just won't persist across reloads */
      }
      renderThemeToggle();
    });
  });
}

async function fetchJson(path) {
  const res = await fetch(path, { cache: "no-store" });
  if (!res.ok) throw new Error(`${path} -> ${res.status}`);
  return res.json();
}

async function loadData() {
  if (state.mode === "sample") {
    const [plan, progress, manifest] = await Promise.all([
      fetchJson("assets/sample/plan.json"),
      fetchJson("assets/sample/progress.json"),
      fetchJson("assets/sample/manifest.json"),
    ]);
    state.plan = plan;
    state.progress = progress;
    state.manifest = manifest;
    state.dataMissing = false;
    return;
  }

  // Real mode: read from .colaberry/, written by the platform on sync.
  try {
    const [plan, progress, manifest] = await Promise.all([
      fetchJson(".colaberry/plan.json"),
      fetchJson(".colaberry/progress.json"),
      fetchJson(".colaberry/manifest.json"),
    ]);
    state.plan = plan;
    state.progress = progress;
    state.manifest = manifest;
    state.dataMissing = false;
  } catch (err) {
    state.plan = null;
    state.progress = null;
    state.manifest = null;
    state.dataMissing = true;
  }
}

function formatDataStamp() {
  const el = document.getElementById("data-stamp");
  if (state.dataMissing || !state.manifest || !state.manifest.generated_at) {
    el.textContent = "Data as of: not yet synced from the portal";
    el.className = "data-stamp unknown";
    return;
  }
  const generated = new Date(state.manifest.generated_at);
  const now = new Date();
  const ageMs = now - generated;
  const ageDays = ageMs / (1000 * 60 * 60 * 24);

  const absolute = generated.toLocaleDateString(undefined, {
    year: "numeric", month: "long", day: "numeric",
  });
  const relative = formatRelativeAge(ageMs);
  el.textContent = `Data as of ${absolute} (${relative})`;

  if (ageDays > 7) {
    el.className = "data-stamp warn";
    el.textContent += " — sync from the portal to refresh";
  } else {
    el.className = "data-stamp";
  }
}

function formatRelativeAge(ms) {
  const mins = Math.floor(ms / 60000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins} minute${mins === 1 ? "" : "s"} ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? "" : "s"} ago`;
  const days = Math.floor(hours / 24);
  return `${days} day${days === 1 ? "" : "s"} ago`;
}

function sampleBanner() {
  return state.mode === "sample"
    ? '<div class="sample-banner">SAMPLE DATA — not from your real project</div>'
    : "";
}

function renderModeToggle() {
  const el = document.getElementById("mode-toggle");
  el.innerHTML = `
    <button data-mode="sample" class="${state.mode === "sample" ? "active" : ""}">Sample</button>
    <button data-mode="real" class="${state.mode === "real" ? "active" : ""}">Real</button>
  `;
  el.querySelectorAll("button").forEach((btn) => {
    btn.addEventListener("click", async () => {
      state.mode = btn.dataset.mode;
      await loadData();
      renderAll();
    });
  });
}

function renderTabsNav() {
  const el = document.getElementById("tabs-nav");
  el.innerHTML = TABS.map(
    (t) => `<button data-tab="${t.id}" class="${state.activeTab === t.id ? "active" : ""}">${t.label}</button>`
  ).join("");
  el.querySelectorAll("button").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.activeTab = btn.dataset.tab;
      renderAll();
    });
  });
}

function renderNotBuilt(label) {
  return `
    <div class="not-built">
      <p><strong>${label}</strong> — Not built yet.</p>
      <p>Say <strong>"build the rest"</strong> when the Overview tab looks right, and this tab will be built next.</p>
    </div>
  `;
}

function renderOverview() {
  const plan = state.plan;
  const progress = state.progress;

  if (!plan || !progress) {
    return `
      ${sampleBanner()}
      <h1 class="tab-title">Overview</h1>
      <p class="tab-desc">The single screen you'd show someone in thirty seconds.</p>
      <div class="empty-state">No project data available yet. Switch to Sample, or sync from the portal.</div>
    `;
  }

  const totals = progress.totals || {};
  const demoRelease = (plan.releases || []).find((r) => r.key === plan.schedule?.demo_release_key);

  return `
    ${sampleBanner()}
    <h1 class="tab-title">Overview</h1>
    <p class="tab-desc">${escapeHtml(plan.project?.name || "")} — ${escapeHtml(plan.project?.descriptor || "")}</p>

    <div class="card-grid">
      <div class="card" data-detail="overview-release">
        <h3>Current release</h3>
        <div class="metric">${escapeHtml(demoRelease?.key || plan.schedule?.demo_release_key || "—")}</div>
        <div class="sub">${escapeHtml(demoRelease?.name || "")}</div>
      </div>
      <div class="card" data-detail="overview-stories">
        <h3>Stories verified</h3>
        <div class="metric">${num(totals.stories_verified)} / ${num(totals.stories_total)}</div>
        <div class="sub">of planned first-release stories</div>
      </div>
      <div class="card" data-detail="overview-criteria">
        <h3>Criteria passed</h3>
        <div class="metric">${num(totals.criteria_passed)} / ${num(totals.criteria_total)}</div>
        <div class="sub">acceptance criteria confirmed</div>
      </div>
      <div class="card" data-detail="overview-points">
        <h3>Points awarded</h3>
        <div class="metric">${num(totals.points_awarded)}</div>
        <div class="sub">course points earned so far</div>
      </div>
    </div>

    <div id="detail-panel"></div>
  `;
}

function num(v) {
  return typeof v === "number" ? v : "0";
}

function escapeHtml(s) {
  if (s == null) return "";
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function renderDetail(rawId) {
  const panel = document.getElementById("detail-panel");
  if (!panel) return;
  const plan = state.plan;
  const progress = state.progress;
  let html = "";

  // Legacy overview-* ids (no colon) plus tab:key ids share one dispatcher.
  const [tab, key] = rawId.includes(":") ? rawId.split(/:(.+)/) : [rawId, null];

  if (tab === "overview-release") {
    html = `<h3>Releases</h3><ul>${(plan.releases || [])
      .map((r) => `<li><strong>${escapeHtml(r.key)}</strong> — ${escapeHtml(r.name)} (${r.story_ids.length} stories)${r.is_demo_target ? " — demo target" : ""}</li>`)
      .join("")}</ul>`;
  } else if (tab === "overview-stories" || tab === "overview-criteria") {
    const rows = (progress.stories || [])
      .map((s) => `<tr><td>${escapeHtml(s.id)}</td><td>${escapeHtml(s.verification?.state)}</td></tr>`)
      .join("");
    html = `<h3>Story verification state</h3><table class="trace-table"><thead><tr><th>Story</th><th>State</th></tr></thead><tbody>${rows}</tbody></table>`;
  } else if (tab === "overview-points") {
    html = `<h3>Points by story</h3><ul>${(progress.stories || [])
      .map((s) => `<li>${escapeHtml(s.id)}: ${num(s.verification?.points_awarded)}</li>`)
      .join("")}</ul>`;
  } else if (tab === "outcomes") {
    const m = (plan.derived?.measures || [])[Number(key)];
    html = m ? `<h3>${escapeHtml(m.id)}</h3><p>${escapeHtml(m.statement)}</p>` : `<p>No detail available.</p>`;
  } else if (tab === "users") {
    const role = decodeURIComponent(key);
    const stories = storiesForRole(role);
    html = `<h3>${escapeHtml(role)}</h3>` + (stories.length
      ? `<ul>${stories.map((s) => `<li><strong>${escapeHtml(s.id)}</strong> — ${escapeHtml(s.narrative)}</li>`).join("")}</ul>`
      : `<p>No story narrative currently matches this role.</p>`);
  } else if (tab === "guardrails") {
    const g = (plan.derived?.guardrails || []).find((x) => x.id === key);
    const req = (plan.requirements || []).find((r) => r.id === key);
    const fulfilled = req?.fulfilled_by || [];
    html = `<h3>${escapeHtml(key)}</h3><p>${escapeHtml(g?.statement || "")}</p>` +
      (fulfilled.length ? `<p>Owning stories: ${fulfilled.map(verifiedBadge).join(" ")}</p>` : `<p>No story currently owns this guardrail.</p>`);
  } else if (tab === "pm") {
    const story = (plan.stories || []).find((s) => s.id === key);
    const prog = (progress.stories || []).find((p) => p.id === key);
    html = story
      ? `<h3>${escapeHtml(story.id)} — ${escapeHtml(story.title)}</h3>
         <p>${escapeHtml(story.narrative)}</p>
         <p>Release: ${escapeHtml(story.release)} · Due: ${escapeHtml(story.due_on)} (first given: ${escapeHtml(story.due_baseline_on)})</p>
         <p>State: ${verifiedBadge(story.id)}${prog?.verification?.commit_sha ? ` · commit ${escapeHtml(prog.verification.commit_sha.slice(0, 7))}` : ""}</p>`
      : `<p>No detail available.</p>`;
  } else if (tab === "agents") {
    const owner = STORY_OWNERS.find((o) => o.name === decodeURIComponent(key));
    html = owner
      ? `<h3>${escapeHtml(owner.name)}</h3><p>Owns: ${owner.owns.map(verifiedBadge).join(" ")}</p>`
      : `<p>No detail available.</p>`;
  } else if (tab === "datamodel") {
    const entity = DATA_MODEL_ENTITIES.find((e) => e.name === decodeURIComponent(key));
    html = entity
      ? `<h3>${escapeHtml(entity.name)}</h3><p>${escapeHtml(entity.note)}</p><ul>${entity.fields.map((f) => `<li><code>${escapeHtml(f)}</code></li>`).join("")}</ul>`
      : `<p>No detail available.</p>`;
  }

  panel.innerHTML = `
    <div class="detail-panel">
      <button class="close-btn" id="close-detail">Close ✕</button>
      ${html}
    </div>
  `;
  document.getElementById("close-detail").addEventListener("click", () => (panel.innerHTML = ""));
}

const TAB_RENDERERS = {
  overview: renderOverview,
  outcomes: renderOutcomes,
  users: renderUsers,
  guardrails: renderGuardrails,
  systems: renderSystems,
  pm: renderPM,
  agents: renderAgents,
  kb: renderKB,
  datamodel: renderDataModel,
};

function renderTabContent() {
  const el = document.getElementById("tab-content");
  const renderer = TAB_RENDERERS[state.activeTab];
  el.innerHTML = renderer ? renderer() : renderNotBuilt(state.activeTab);

  el.querySelectorAll("[data-detail]").forEach((card) => {
    card.addEventListener("click", () => renderDetail(card.dataset.detail));
  });

  if (state.activeTab === "kb") wireKbChat();
}

function requireData(title, desc) {
  return `
    ${sampleBanner()}
    <h1 class="tab-title">${title}</h1>
    <p class="tab-desc">${desc}</p>
    <div class="empty-state">No project data available yet. Switch to Sample, or sync from the portal.</div>
  `;
}

function verifiedBadge(storyId) {
  const s = (state.progress?.stories || []).find((x) => x.id === storyId);
  const st = s?.verification?.state || "not_started";
  const cls = st === "verified" ? "verified" : "";
  return `<span class="tag ${cls}">${escapeHtml(storyId)}: ${escapeHtml(st)}</span>`;
}

function renderOutcomes() {
  const plan = state.plan;
  if (!plan) return requireData("Outcomes", "The numbers this project has to move.");
  const measures = plan.derived?.measures || [];

  const body = measures.length
    ? `<div class="card-grid">${measures
        .map((m, i) => `<div class="card" data-detail="outcomes:${i}"><h3>${escapeHtml(m.id)}</h3><div class="sub">${escapeHtml(m.statement)}</div></div>`)
        .join("")}</div>`
    : `<div class="empty-state">No numeric target is defined in the plan yet. This tab will show one card per measure once <code>plan.derived.measures</code> carries them.</div>`;

  return `
    ${sampleBanner()}
    <h1 class="tab-title">Outcomes</h1>
    <p class="tab-desc">The numbers this project has to move.</p>
    ${body}
    <div id="detail-panel"></div>
  `;
}

function renderUsers() {
  const plan = state.plan;
  if (!plan) return requireData("Users & Use Case", "Who this is for and what they're trying to get done.");
  const roles = plan.derived?.roles || [];

  return `
    ${sampleBanner()}
    <h1 class="tab-title">Users & Use Case</h1>
    <p class="tab-desc">Roles taken from your own stories' "As a &lt;role&gt;, I want …" narratives.</p>
    <div class="card-grid">
      ${roles
        .map((r) => `<div class="card" data-detail="users:${encodeURIComponent(r)}"><h3>Role</h3><div class="metric" style="font-size:1.15rem">${escapeHtml(r)}</div></div>`)
        .join("")}
    </div>
    <div id="detail-panel"></div>
  `;
}

function storiesForRole(role) {
  const plan = state.plan;
  const norm = role.toLowerCase();
  return (plan.stories || []).filter((s) => {
    const m = /^as (?:a|an) ([^,]+),/i.exec(s.narrative || "");
    if (!m) return false;
    return m[1].trim().toLowerCase().includes(norm) || norm.includes(m[1].trim().toLowerCase());
  });
}

function renderGuardrails() {
  const plan = state.plan;
  if (!plan) return requireData("Guardrails", "What must never happen.");
  const guardrails = plan.derived?.guardrails || [];

  const cards = guardrails
    .map((g) => {
      const req = (plan.requirements || []).find((r) => r.id === g.id);
      const fulfilledBy = req?.fulfilled_by || [];
      const allVerified = fulfilledBy.length > 0 && fulfilledBy.every((id) => {
        const s = (state.progress?.stories || []).find((x) => x.id === id);
        return s?.verification?.state === "verified";
      });
      const status = fulfilledBy.length === 0
        ? "No story owns this guardrail yet"
        : allVerified
        ? "Enforced — all owning stories verified"
        : "A promise made, not yet kept";
      const dotColor = fulfilledBy.length === 0 ? "" : allVerified ? "background:var(--color-success)" : "background:var(--color-warning)";
      return `
        <div class="card" data-detail="guardrails:${g.id}">
          <h3>${escapeHtml(g.id)}</h3>
          <div class="sub" style="margin-bottom:8px">${escapeHtml(g.statement)}</div>
          <div><span class="status-dot" style="${dotColor}"></span>${escapeHtml(status)}</div>
        </div>
      `;
    })
    .join("");

  return `
    ${sampleBanner()}
    <h1 class="tab-title">Guardrails</h1>
    <p class="tab-desc">Promises this system makes, and whether anything currently enforces them.</p>
    <div class="card-grid">${cards || '<div class="empty-state">No SAFE requirements declared.</div>'}</div>
    <div id="detail-panel"></div>
  `;
}

function renderSystems() {
  const plan = state.plan;
  if (!plan) return requireData("Systems", "What this connects to.");
  const systems = plan.derived?.systems || [];

  const body = systems.length
    ? `<div class="card-grid">${systems
        .map((s) => `<div class="card"><span class="status-dot"></span>${escapeHtml(s)}<div class="sub">not checked from here</div></div>`)
        .join("")}</div>`
    : `<div class="empty-state">Your plan names no external system yet. Every indicator here will render grey and labelled "not checked from here" until your own running system reports otherwise.</div>`;

  return `
    ${sampleBanner()}
    <h1 class="tab-title">Systems</h1>
    <p class="tab-desc">What this project connects to — and whether that connection is actually live.</p>
    ${body}
  `;
}

function renderPM() {
  const plan = state.plan;
  const progress = state.progress;
  if (!plan || !progress) return requireData("Project Management", "Releases, tasks, and due dates.");

  const releases = plan.releases || [];
  const maxStories = Math.max(1, ...releases.map((r) => r.story_ids.length));

  const ganttRows = releases
    .map((r) => {
      const widthPct = Math.round((r.story_ids.length / maxStories) * 100);
      return `
        <div style="margin-bottom:10px">
          <div style="display:flex;justify-content:space-between;font-size:0.8rem;margin-bottom:4px">
            <span><strong>${escapeHtml(r.key)}</strong> ${escapeHtml(r.name)}${r.is_demo_target ? ' <span class="tag must">demo target</span>' : ""}</span>
            <span class="sub">${escapeHtml(r.starts_on)} → ${escapeHtml(r.ends_on)}</span>
          </div>
          <div style="background:var(--color-bg);border:1px solid var(--color-border);border-radius:999px;height:10px;overflow:hidden">
            <div style="width:${widthPct}%;height:100%;background:linear-gradient(90deg,var(--color-primary),var(--color-accent))"></div>
          </div>
        </div>
      `;
    })
    .join("");

  const taskRows = (plan.stories || [])
    .map((s) => {
      const prog = (progress.stories || []).find((p) => p.id === s.id);
      const state_ = prog?.verification?.state || "not_started";
      const slippageDays = s.due_on && s.due_baseline_on
        ? Math.round((new Date(s.due_on) - new Date(s.due_baseline_on)) / 86400000)
        : 0;
      return `
        <tr data-detail="pm:${s.id}" style="cursor:pointer">
          <td>${escapeHtml(s.id)}</td>
          <td>${escapeHtml(s.title)}</td>
          <td>${escapeHtml(s.release)}</td>
          <td>${escapeHtml(s.due_on)}${slippageDays ? ` <span class="tag gap">${slippageDays > 0 ? "+" : ""}${slippageDays}d slip</span>` : ""}</td>
          <td><span class="tag ${state_ === "verified" ? "verified" : ""}">${escapeHtml(state_)}</span></td>
        </tr>
      `;
    })
    .join("");

  return `
    ${sampleBanner()}
    <h1 class="tab-title">Project Management</h1>
    <p class="tab-desc">Build ${escapeHtml(plan.schedule?.build_start)} → ${escapeHtml(plan.schedule?.build_end)}. Demo day ${escapeHtml(plan.schedule?.demo_day)}.</p>
    <div class="detail-panel" style="margin-bottom:20px">${ganttRows}</div>
    <table class="trace-table">
      <thead><tr><th>Story</th><th>Title</th><th>Release</th><th>Due</th><th>State</th></tr></thead>
      <tbody>${taskRows}</tbody>
    </table>
    <div id="detail-panel"></div>
  `;
}

function renderAgents() {
  return `
    ${sampleBanner()}
    <h1 class="tab-title">AI Agents</h1>
    <p class="tab-desc">Your plan does not carry a scoped agent roster yet — these are story <em>owners</em>, not AI agents.</p>
    <div class="card-grid">
      ${STORY_OWNERS.map((o) => `
        <div class="card" data-detail="agents:${encodeURIComponent(o.name)}">
          <h3>${escapeHtml(o.name)}</h3>
          <div class="sub">Owns: ${o.owns.map(escapeHtml).join(", ")}</div>
          <div class="sub" style="margin-top:6px">Skills: no skills registered yet</div>
          <div class="sub">no runs recorded</div>
        </div>
      `).join("")}
    </div>
    <div id="detail-panel"></div>
  `;
}

function renderKB() {
  const plan = state.plan;
  if (!plan) return requireData("Knowledge Base", "Everything the project knows about itself.");
  const requirements = plan.requirements || [];

  const rows = requirements
    .map((r) => {
      const fulfilled = r.fulfilled_by || [];
      const isGap = r.priority === "must" && fulfilled.length === 0;
      return `
        <tr>
          <td>${escapeHtml(r.id)}</td>
          <td>${escapeHtml(r.statement)}</td>
          <td><span class="tag ${r.priority === "must" ? "must" : ""}">${escapeHtml(r.priority)}</span></td>
          <td>${fulfilled.length ? fulfilled.map(verifiedBadge).join(" ") : isGap ? '<span class="tag gap">gap — no owning story</span>' : "—"}</td>
        </tr>
      `;
    })
    .join("");

  return `
    ${sampleBanner()}
    <h1 class="tab-title">Knowledge Base</h1>
    <p class="tab-desc">Requirements traceability, and a chat panel scoped to this page's data.</p>
    <table class="trace-table">
      <thead><tr><th>Requirement</th><th>Statement</th><th>Priority</th><th>Fulfilled by</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>

    <div class="detail-panel" style="margin-top:24px">
      <h3 style="margin-top:0">Ask about this project's data</h3>
      <div id="kb-log" style="font-size:0.88rem;margin-bottom:12px;max-height:220px;overflow-y:auto"></div>
      <div style="display:flex;gap:8px">
        <input id="kb-input" type="text" placeholder="e.g. REQ-005, STORY-004, how many verified?"
          style="flex:1;padding:9px 12px;border-radius:var(--radius-sm);border:1px solid var(--color-border);background:var(--color-bg);color:var(--color-text)" />
        <button id="kb-ask" style="padding:9px 16px;border:none;border-radius:var(--radius-sm);background:var(--color-primary);color:#fff;cursor:pointer">Ask</button>
      </div>
    </div>
  `;
}

function wireKbChat() {
  const input = document.getElementById("kb-input");
  const btn = document.getElementById("kb-ask");
  const log = document.getElementById("kb-log");
  if (!input || !btn) return;

  const ask = () => {
    const q = input.value.trim();
    if (!q) return;
    const answer = answerKbQuestion(q);
    log.innerHTML += `<div style="margin-bottom:10px"><strong>You:</strong> ${escapeHtml(q)}<br><strong>Command Center:</strong> ${answer}</div>`;
    log.scrollTop = log.scrollHeight;
    input.value = "";
  };

  btn.addEventListener("click", ask);
  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter") ask();
  });
}

function answerKbQuestion(q) {
  const plan = state.plan;
  const progress = state.progress;
  if (!plan || !progress) return "No project data is loaded — switch to Sample or sync from the portal. (Knowledge Base tab)";

  const reqMatch = /REQ-\d+/i.exec(q);
  if (reqMatch) {
    const id = reqMatch[0].toUpperCase();
    const req = (plan.requirements || []).find((r) => r.id === id);
    if (!req) return `I don't have ${escapeHtml(id)} in the requirements register. (Knowledge Base tab)`;
    const fulfilled = req.fulfilled_by?.length ? req.fulfilled_by.join(", ") : "no story yet";
    return `${escapeHtml(id)}: "${escapeHtml(req.statement)}" — fulfilled by ${escapeHtml(fulfilled)}. (Knowledge Base tab)`;
  }

  const storyMatch = /STORY-\d+/i.exec(q);
  if (storyMatch) {
    const id = storyMatch[0].toUpperCase();
    const story = (plan.stories || []).find((s) => s.id === id);
    if (!story) return `I don't have ${escapeHtml(id)} in the plan. (Knowledge Base tab)`;
    const prog = (progress.stories || []).find((p) => p.id === id);
    return `${escapeHtml(id)}: "${escapeHtml(story.title)}" — release ${escapeHtml(story.release)}, state ${escapeHtml(prog?.verification?.state || "not_started")}. (Knowledge Base tab)`;
  }

  if (/how many.*verif|verified.*count/i.test(q)) {
    const t = progress.totals || {};
    return `${num(t.stories_verified)} of ${num(t.stories_total)} stories are verified. (Knowledge Base tab, from progress.totals)`;
  }

  if (/gap|missing|no story/i.test(q)) {
    const gaps = (plan.requirements || []).filter((r) => r.priority === "must" && (!r.fulfilled_by || r.fulfilled_by.length === 0));
    return gaps.length
      ? `Requirements with no owning story: ${gaps.map((g) => g.id).join(", ")}. (Knowledge Base tab)`
      : `Every "must" requirement currently has an owning story. (Knowledge Base tab)`;
  }

  return `I can only answer from the data on this page — try a requirement id (REQ-00X), a story id (STORY-0XX), or a question about verified counts or gaps. (Knowledge Base tab)`;
}

function renderDataModel() {
  return `
    ${sampleBanner()}
    <h1 class="tab-title">Data Model</h1>
    <p class="tab-desc">A starting sketch derived from the requirements register — not the final answer. Review before tables are created.</p>
    <div class="card-grid">
      ${DATA_MODEL_ENTITIES.map((e) => `
        <div class="card" data-detail="datamodel:${encodeURIComponent(e.name)}">
          <h3>${escapeHtml(e.name)}</h3>
          <div class="sub">${escapeHtml(e.note)}</div>
        </div>
      `).join("")}
    </div>
    <div id="detail-panel"></div>
  `;
}

function renderAll() {
  renderThemeToggle();
  renderModeToggle();
  renderTabsNav();
  formatDataStamp();
  renderTabContent();
}

(async function init() {
  initTheme();
  await loadData();
  renderAll();
})();
