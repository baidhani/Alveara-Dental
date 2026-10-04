# Interactive odontogram (STORY-006)

**Requirement:** REQ-008 — an interactive odontogram with tooth and surface selection, supporting existing, diagnosed, planned and completed states.
**Where it lives:** the patient workspace's **Odontogram** tab (`/patients/:id/odontogram`).
**Who can use it:** anyone who may read clinical documentation (`ViewClinicalDocumentation`: dentist, hygienist, assistant, admin) can see the chart and each finding's history; only roles with `ManageClinicalNotes` (dentist, hygienist, admin) can record, move or withdraw a finding. Front desk, billing and the practice manager have no access.

---

## For the reviewer: the numbering finding and what was decided

**Finding.** The master execution plan has no story that covers showing the odontogram in Palmer or FDI numbering, or a practice-level preference for which numbering system to use. Item 22 (`ALV-006-C01`) says only "Universal numbering/lettering default with an abstraction that can later render FDI/ISO without changing tooth identity" and "numbering/lettering is presentation metadata, not the tooth's database identity". Palmer is mentioned nowhere. Search of the plan, `docs/` and `.colaberry/plan.json` for *Palmer, FDI, numbering, nomenclature* found only those two item-22 sentences.

**What STORY-006 did about it.**

| Decision | What was built | Why |
|---|---|---|
| The stored identity of a tooth is its **FDI / ISO 3950 two-digit string** (`"16"`, `"48"`, `"55"`) | 52 keys: permanent 11–18, 21–28, 31–38, 41–48; primary 51–55, 61–65, 71–75, 81–85. The database refuses anything else (case-sensitive check). | One identity that never changes with how a practice reads the chart. A string, not a number, so future identities (supernumerary teeth, areas) can be added without migrating existing findings. |
| **Universal (default), FDI and Palmer are display-only** | One mapper on each side (`Architecture/Odontogram/ToothNumbering.cs`, `pages/odontogram/toothNumbering.ts`). One-to-one over all 52 teeth, reads back to the same key, held to the same published reference teeth in both languages. | This is the "abstraction that can later render FDI/ISO" item 22 asks for, built now and including Palmer. |
| The chart takes the numbering system as a **parameter, defaulting to Universal** | `OdontogramPanel numbering="Universal" | "Fdi" | "Palmer"`. | No preference setting exists yet. |
| The **preference setting is not built** (recorded limit) | No setting, no UI to change the system, nothing stored. | Where it is stored and who changes it (per practice? per user?) belongs to practice configuration, which is outside this story. |

**What the reviewer may want to do with the master execution plan.**
1. Decide which story owns the numbering **preference** (storage, who may change it, the control that sets it) and whether it is per practice or per user. As planned, nothing owns it.
2. Decide whether item 22 should say that the display abstraction **already exists** (built here for Universal, FDI and Palmer) so it is not rebuilt, and should add Palmer to its wording.
3. Note that item 22 still owns migrating the **fixed list of six conditions** to a table (see limits).

## What it does

- **The chart** draws the 32 permanent teeth as a dentist looks at the patient (the patient's right on the viewer's left). Each tooth is a button with its number in the chosen system and, under it, the state of what is recorded on it **as a word**; the border style (solid, dashed, dotted, double) says the same but never alone. A tooth with findings in several states shows the one that most needs attention first (Diagnosed, then Planned, then Completed, then Existing) with a count of the rest.
- **Selecting a tooth** shows what is recorded on it by surface, names the tooth in all three systems ("Also written FDI 16 · Palmer UR6") so a chart read in another system can be matched, and lists each finding with who recorded it and last changed it. **Selecting a surface** narrows the list to that surface plus the whole-tooth findings. Only surfaces that exist on the tooth are offered: Occlusal and Buccal on back teeth, Incisal and Facial on front teeth, Mesial, Distal and Lingual on all.
- **Recording** a finding: condition and state are *chosen*, never pre-filled with a guess. A surface is asked for only when the condition is about one surface. The tooth is sent as its FDI key whatever number is shown.
- **Moving a finding forward:** Diagnosed → Planned → Completed, or Diagnosed straight to Completed (work done at the visit). Existing and Completed are final; to correct one, withdraw it and record it again.
- **Withdrawing** a wrong entry needs a reason. The finding leaves the chart and stays in its history, never deleted.
- **History** per finding: every version oldest first, with who, when and why.

### The six conditions (fixed for now)

| Condition | Applies to |
|---|---|
| Caries, Restoration | one surface |
| Crown, Missing tooth, Implant, Root canal | the whole tooth (no surface) |

### Lifecycle states

| State | Meaning |
|---|---|
| Existing | already in the mouth |
| Diagnosed | found, not yet planned |
| Planned | treatment planned |
| Completed | treatment completed |

## How the promises are kept

| Promise | How |
|---|---|
| Saved with the state the clinician chose (acceptance 1) | The form has no default for condition or state; the service stores exactly what was sent; the database checks the state list case-sensitively. |
| A completed treatment updates the odontogram (acceptance 2) | The chart is always read from the findings. Completing a finding, or recording work as Completed, changes that finding; there is no second copy to fall out of step. |
| Every update is logged with user and time (acceptance 3) | The change, its history version and its audit entry are staged and saved **once**. If the audit write fails, nothing is saved (tested with a trigger that refuses the audit insert). The audit text never names the tooth, condition or reason. |
| A wrong tooth or surface | The server refuses a tooth that is not one of the 52 keys, a surface that does not exist on that kind of tooth, a surface on a whole-tooth condition and a missing surface on a surface condition — each with the field named, and nothing stored. The database enforces the same rules. |
| Concurrency | A change echoes the finding's row version; a stale one is the shared 409 (`concurrency_conflict`). Two people recording the same finding at the same moment end with one finding (a unique index). The screen shows the shared conflict banner and a reload that refreshes in place. |
| Repeats are safe | Recording the same active finding again, moving to the state it already has and withdrawing a withdrawn finding change nothing. A dropped connection after the server stored a change is retried as a quiet repeat. |
| Nothing is invented | An empty chart says "nothing recorded" and never implies healthy teeth; a failed load says so instead of showing an empty chart; findings on teeth the chart does not draw are listed, never hidden. |

## API

| Call | Permission | Notes |
|---|---|---|
| `GET api/patients/{id}/odontogram` | ViewClinicalDocumentation | active findings only |
| `GET api/odontogram/findings/{id}/history` | ViewClinicalDocumentation | every version |
| `POST api/patients/{id}/odontogram/findings` | ManageClinicalNotes + CSRF | `{toothKey, surface, condition, state}` |
| `POST api/odontogram/findings/{id}/state` | ManageClinicalNotes + CSRF | `{state, rowVersion}` |
| `POST api/odontogram/findings/{id}/withdraw` | ManageClinicalNotes + CSRF | `{reason, rowVersion}` |

Refusals return `{error, message, fieldErrors}`: `validation_failed` (400), `reason_required` (400), `row_version_required` / `row_version_invalid` (400), `patient_not_found` / `finding_not_found` (404), `finding_exists`, `invalid_transition`, `finding_withdrawn` (409), and the shared `concurrency_conflict` (409).

## Schema (migration `AddOdontogram`)

`ToothFindings` and an append-only `ToothFindingVersions`. Case-sensitive checks for the tooth key, condition, state, status and surface; a surface that exists on that kind of tooth; surface present exactly when the condition needs one; a withdrawn finding must have who, when and a reason; one **active** finding per patient, tooth, surface and condition (a withdrawn one can be entered again). Triggers 51055 (a finding is never deleted) and 51056 (history is append-only).

## Limits (recorded, not hidden)

- **No numbering preference setting** (see the finding above). The mapper supports Universal, FDI and Palmer; nothing chooses between them.
- **Permanent teeth only on the chart.** Findings on primary teeth can be recorded through the API and are listed under the chart, but there is no primary or mixed-dentition screen.
- **Not covered by the FDI key:** supernumerary teeth and findings about an area rather than one tooth (a quadrant, an arch, the whole mouth).
- **The six conditions are fixed** in code and in the database check. Item 22 (`ALV-006-C01`) moves them to a table.
- **Complete is one click** with no confirmation. It is undone by withdrawing and re-recording, and the history keeps every step.
- **A finding does not link to a diagnosis, treatment plan or procedure** (later stories). Completing a finding here does not create a charge.
- **Reading the chart is not audited**; only changes are.
- **The shared conflict banner** prints the entity name it is given; the odontogram passes "tooth finding". Other screens that pass a raw entity type (for example the safety panel) show it run together in lower case; the shared banner is outside this story and was not changed.

## Verification

- Backend (real SQL Server): `ToothNumberingTests` 74, `OdontogramSchemaTests` 48, `OdontogramServiceTests` 65, `OdontogramApiTests` 22.
- Frontend: `toothNumbering.test.ts` 74, `Odontogram.test.tsx` 21, `OdontogramWrite.test.tsx` 20, `odontogramContrast.test.ts` 25.
- Real browser, real API, fresh database: `e2e/odontogram-real-backend.spec.ts` (11 steps) — see `docs/testing/REAL_BACKEND_E2E.md`; evidence and screenshots are in `docs/testing/odontogram-walkthrough/`.
