# Odontogram (STORY-006 and ALV-006-C01)

**Requirement:** REQ-008 - an interactive odontogram with tooth and surface selection, supporting existing, diagnosed, planned and completed states. `STORY-006` built the course odontogram; `ALV-006-C01` extends it to primary and mixed dentition, a condition catalogue a practice can extend, tooth-state validation, a longitudinal history per tooth and links to diagnoses, plans and procedures.
**Where it lives:** the patient workspace's **Odontogram** tab (`/patients/:id/odontogram`).
**Who can use it:** anyone who may read clinical documentation (`ViewClinicalDocumentation`: dentist, hygienist, assistant, admin) can see the chart, each finding's history, a tooth's whole history and the condition catalogue; roles with `ManageClinicalNotes` (dentist, hygienist, admin) can record, move, withdraw and link findings; only roles with `ManageClinicalTemplates` (dentist, admin - the permission that already governs the practice's clinical configuration) can add, retire and reactivate a condition. Front desk, billing and the practice manager have no access.

---

## For the reviewer: the numbering finding and what was decided

**Finding.** The master execution plan has no story that covers showing the odontogram in Palmer or FDI numbering, or a practice-level preference for which numbering system to use. Item 22 (`ALV-006-C01`) says only "Universal numbering/lettering default with an abstraction that can later render FDI/ISO without changing tooth identity" and "numbering/lettering is presentation metadata, not the tooth's database identity". Palmer is mentioned nowhere.

**What was decided (in STORY-006, unchanged by ALV-006-C01).**

| Decision | What was built | Why |
|---|---|---|
| A tooth's stored identity is its **FDI / ISO 3950 two-digit string** (`"16"`, `"55"`) | 52 keys (permanent 11-18, 21-28, 31-38, 41-48; primary 51-55, 61-65, 71-75, 81-85); the database refuses anything else | One identity that never changes with how a practice reads the chart. This is what item 22's "anatomical tooth identity independent of display numbering" asks for. |
| **Universal (default), FDI and Palmer are display-only** | One mapper on each side, one-to-one over all 52 teeth, held to the same published reference teeth in both languages; the chart takes the system as a parameter, default Universal | The abstraction item 22 asks for, including primary teeth (A-T) and Palmer. |
| The **preference setting is not built** | No setting, no UI, nothing stored; a test asserts the server holds no numbering column | Where it is stored and who changes it belongs to practice configuration. |

**For the master execution plan.** (1) Decide which story owns the numbering **preference** (storage, who may change it, per practice or per user); as planned nothing does. (2) Item 22's numbering bullet is already delivered (the display abstraction for Universal, FDI and Palmer, permanent and primary); it can be removed from that item's scope and Palmer added to the wording of whichever story owns the preference. (3) Item 22 names "extensible condition types" but not who may extend them or how. Following rule 13 of the standard prompt rules (configuration belongs to the domain story that consumes it) the catalogue lives in this story, and this attempt chose `ManageClinicalTemplates` (dentist and administrator) as the permission that changes it; the reviewer may want that choice confirmed or the plan's wording updated.

## What it does

**Dentition.** The chart shows **Permanent**, **Primary** or **Mixed** teeth. These are three *views* of the same findings: switching changes only what is drawn, makes no request, changes nothing recorded and hides nothing from the history. A patient who already has a finding on a primary tooth opens as Mixed, otherwise as Permanent. Findings on teeth the current view does not draw are listed under the chart, never hidden. A mixed chart draws four arches (upper permanent, upper primary, lower primary, lower permanent). Primary teeth follow the same surface rules as permanent ones (a primary incisor has Incisal and Facial surfaces, a primary molar Occlusal and Buccal).

**The chart.** Each tooth is a button with its number in the chosen system and, under it, the state of what is recorded on it **as a word**; the border style (solid, dashed, dotted, double) says the same but never alone. A tooth with findings in several states shows the one that most needs attention first (Diagnosed, Planned, Completed, Existing) with a count of the rest. **Arrow keys** move between teeth (left and right within an arch, Home and End to its ends, up and down to the arch above or below); Enter or Space selects; every tooth is still a normal Tab stop.

**A tooth.** Selecting one names it in all three systems, offers only the surfaces that exist on it, lists what is recorded with who recorded and last changed it, shows the records each finding is **linked** to, and has a **Tooth history**: every finding ever recorded on the tooth (withdrawn ones included) and every change to each, oldest first, with who, when and why. Selecting a surface narrows the list to that surface plus the whole-tooth findings.

**Recording.** Condition and state are *chosen*, never pre-filled. The conditions offered are the practice's active catalogue, narrowed to those that apply to the tooth's dentition. A surface is asked for only when the condition is about one surface. The tooth is sent as its FDI key whatever number is shown. A finding moves forward only (Diagnosed to Planned to Completed, or Diagnosed straight to Completed); Existing and Completed are final; a wrong entry is **withdrawn** with a reason and kept in the history.

**The condition catalogue.** The six course conditions are now rows (a `ConditionTypes` table seeded with the same six), and a person who manages clinical templates can **add** a condition (label, code, recorded on one surface or the whole tooth, permanent / primary / both, effect on the tooth), **retire** one with a reason and **reactivate** it. Retiring stops *new* findings of that kind; every existing finding keeps reading as it did and the retired condition stays listed. A condition's code, label, scope, dentition and effect cannot be changed once it exists (a database trigger refuses it), so history cannot be re-described.

| Condition | Recorded on | Applies to | Effect |
|---|---|---|---|
| Caries, Restoration | one surface | permanent and primary teeth | none |
| Crown, Root canal | the whole tooth | permanent and primary teeth | none |
| Missing tooth | the whole tooth | permanent and primary teeth | the tooth is absent |
| Implant | the whole tooth | permanent teeth only | stands in for a missing tooth |

**Tooth-state validation.** An implant is refused on a primary tooth (a condition must apply to the tooth's dentition); while a tooth is recorded as missing (an Existing or Completed finding whose condition has the Absent effect) no other finding can be recorded on it except an implant or another Absent condition; a Planned or Diagnosed extraction does not make the tooth absent yet; withdrawing the missing-tooth entry makes the tooth present again.

**Links.** A finding can be linked to a diagnosis, a treatment plan or a procedure **by reference** (those stories come later, so the reference is opaque text, up to 100 characters). A link is append-only, unique per finding, type and reference, shown beside the finding, recorded in the finding's history and the tooth's timeline, and **does not change the finding**, so linking never invalidates someone else's edit.

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
| Saved with the state the clinician chose | The form has no default for condition or state; the service stores exactly what was sent; the database checks the state list case-sensitively. |
| A completed treatment updates the odontogram | The chart is always read from the findings; completing a finding changes that finding; there is no second copy. |
| Every update is logged with user and time | The change, its history version and its audit entry are staged and saved **once**; if the audit write fails nothing is saved. Audit text never names the tooth, condition, reason, label or link. |
| Numbering is presentation, not identity | The stored key is FDI; no numbering column exists on findings, their history or the chart response; the mapper is display and parse only. |
| History is never silently lost | Findings are never deleted (a trigger refuses it); their history, a condition's history and links are append-only (triggers refuse any edit or delete); a retired condition keeps its label; the tooth timeline reads every version of every finding, withdrawn or not. |
| A finding names a real condition, exactly | A foreign key to the catalogue on a case-sensitive code; the surface must match the scope stored on the finding; the unique active finding per tooth, surface and condition stays. |
| Concurrency | A change echoes the row version; a stale one is the shared 409. Two people recording the same finding, creating the same condition code or making the same link at the same moment end with one. A stale condition change shows the conflict banner naming a "condition type". |
| Repeats are safe | Recording the same active finding again, moving to the state it already has, withdrawing a withdrawn finding, creating the identical condition, retiring a retired one, reactivating an active one and linking the same thing again change nothing. |
| Nothing is invented | An empty chart says "nothing recorded" and never implies healthy teeth; a failed load says so; a failed catalogue load says findings cannot be recorded now; findings on undrawn teeth are listed. |

## API

| Call | Permission | Notes |
|---|---|---|
| `GET api/patients/{id}/odontogram` | ViewClinicalDocumentation | active findings, each with its condition label, scope and links |
| `GET api/patients/{id}/odontogram/teeth/{toothKey}/history` | ViewClinicalDocumentation | the tooth's timeline, withdrawn findings included |
| `GET api/odontogram/findings/{id}/history` | ViewClinicalDocumentation | one finding's versions |
| `POST api/patients/{id}/odontogram/findings` | ManageClinicalNotes + CSRF | `{toothKey, surface, condition, state}` |
| `POST api/odontogram/findings/{id}/state` | ManageClinicalNotes + CSRF | `{state, rowVersion}` |
| `POST api/odontogram/findings/{id}/withdraw` | ManageClinicalNotes + CSRF | `{reason, rowVersion}` |
| `POST api/odontogram/findings/{id}/links` | ManageClinicalNotes + CSRF | `{linkType, reference}` |
| `GET api/odontogram/condition-types` | ViewClinicalDocumentation | the whole catalogue, retired included |
| `GET api/odontogram/condition-types/{id}/history` | ViewClinicalDocumentation | created, retired, reactivated |
| `POST api/odontogram/condition-types` | ManageClinicalTemplates + CSRF | `{code, label, scope, appliesTo, toothEffect}` |
| `POST api/odontogram/condition-types/{id}/retire` · `/reactivate` | ManageClinicalTemplates + CSRF | `{reason, rowVersion}` |

Refusals return `{error, message, fieldErrors}`: `validation_failed` and `reason_required` and `row_version_required` / `row_version_invalid` (400); `patient_not_found`, `finding_not_found`, `condition_not_found` (404); `finding_exists`, `invalid_transition`, `finding_withdrawn`, `condition_exists`, `condition_inactive`, `tooth_absent` (409); and the shared `concurrency_conflict` (409).

## Schema (migrations `AddOdontogram`, `AddConditionCatalogueAndLinks`)

`ToothFindings` and an append-only `ToothFindingVersions`; `ConditionTypes` (seeded), an append-only `ConditionTypeEvents`, and an append-only `ToothFindingLinks`. Case-sensitive checks throughout; a finding's `Condition` is a case-sensitive foreign key to `ConditionTypes.Code` and carries the `ConditionScope` it was recorded under; triggers 51055 (a finding is never deleted), 51056 (finding history append-only), 51057 (a condition is never deleted), 51058 (condition history append-only), 51059 (links append-only), 51060 (a condition's code, label, scope, dentition and effect are fixed once created). The upgrade fills in the scope of findings recorded before the catalogue existed and keeps every finding and version (tested by migrating a database that holds findings).

## Limits (recorded, not hidden)

- **No numbering preference setting** (see the finding above).
- **Not covered by the FDI key:** supernumerary teeth and findings about an area rather than one tooth (a quadrant, an arch, the whole mouth).
- **A tooth is "absent" only through the Missing / Absent condition.** The chart does not infer absence from a patient's age or from a primary tooth that should have exfoliated, and moving a Missing finding to Completed later does not retire other findings already on that tooth (they stay, with their history).
- **The absent-tooth check is not transactional:** two people recording a missing tooth and another finding on the same tooth at the same instant could both succeed.
- **No screen to add a link.** The link API, its display and its history exist; there is nothing to link to yet (diagnoses, plans and procedures are later stories). The reference is opaque text.
- **A finding is not linked to a charge:** completing one does not create billing.
- **Condition definitions are immutable.** A mistake in a label or scope is corrected by retiring the condition and adding a new one.
- **Complete is one click** with no confirmation; it is undone by withdrawing and re-recording, and the history keeps every step.
- **Reading the chart, the catalogue and a tooth's history is not audited**; only changes are.
- **The shared conflict banner** prints the entity name it is given; the odontogram passes "tooth finding" and "condition type". Other screens that pass a raw entity type show it run together in lower case; the shared banner is outside this story.
- **Specialty odontogram visualizations** (out of scope in the plan) can extend the shared identity and history model later.

## Verification

- Backend (real SQL Server): the STORY-006 tests (`ToothNumberingTests`, `OdontogramSchemaTests`, `OdontogramServiceTests`, `OdontogramApiTests`) pass unchanged in meaning; new: `OdontogramCatalogueSchemaTests` and `OdontogramCatalogueMigrationTests`, `ConditionTypeServiceTests`, `OdontogramHistoryAndLinksTests`, `OdontogramCatalogueApiTests`.
- Frontend: the STORY-006 tests (`Odontogram`, `OdontogramWrite`, `toothNumbering`, `odontogramContrast`) pass; new: `OdontogramLongitudinal.test.tsx`.
- Real browser, real API, fresh database: `e2e/odontogram-real-backend.spec.ts` (the original STORY-006 walkthrough, unchanged, as the parent regression) and `e2e/odontogram-longitudinal-real-backend.spec.ts` - see `docs/testing/REAL_BACKEND_E2E.md`.
