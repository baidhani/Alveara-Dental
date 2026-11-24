# ALV-N027: owner decision record

Recorded 2026-11-23 (session CC-20261123-q4vn) from the owner's answers to `docs/design/ALV-N027_FINDING_SURVEY.md` and the walkthrough findings in `docs/stabilization/UNASSIGNED_FINDINGS.md`. Wording below is the owner's ruling; where the agent summarises, it says so.

## Sequence (owner)
1. **Dedicated account / Practice Owner story** (PD-04). This is the next implementation item; it gets its own brief and independent review. Story id: to be assigned (working name **ACCT**).
2. **ALV-N027:** usability, navigation, configuration and visual fixes. Surveying and drafting may continue, but implementation does not start until ACCT is independently approved and closed.
3. **Setup-wizard story** (Practice Owner's guided setup).
4. **Release-readiness work** (ALV-N028).

## Findings: where each one goes (owner)
| Finding | Ruling |
|---|---|
| U-023 | ALV-N027, **first**. A missing-file download must never expose raw JSON or error content. |
| U-024, U-025, U-026, U-028, U-029, U-030, U-031 | ALV-N027 |
| U-027 | ALV-N027, **presentation only**; MRG-1 stands (no change to the merge logic) |
| U-034 | ALV-N027: Clinical record copy goes to the Clinical tab; Export patient file and Download readable record go to a clearly labelled "Patient file and exports" section in Details |
| U-035, U-010, U-013, U-014, U-021 | ALV-N027 |
| U-033 | **Not approved for ALV-N027.** A browser view of the server's drives and folders could expose infrastructure paths and creates a dangerous configuration surface. It needs its own security design. A future solution is limited to explicitly approved backup roots, validated on the server, with no general filesystem browsing. |
| U-009, U-012 | Separate later design stories |
| U-018, U-019 | ALV-N028. U-019 is a release-readiness and privacy item, not a cosmetic one. |
| U-032 | Its own later data-source story. No outside city dataset in ALV-N027. |

## ALV-N027 decisions (owner)
- **PD-04:** replaced by the architecture direction in `docs/design/ALV-N027_PD-04_ACCOUNT_OWNER_AND_SETUP_ARCHITECTURE.md`, recorded there unchanged, and moved to the dedicated ACCT story.
- **PD-31:**
  - Providers are Dentist and Hygienist.
  - Specialty is a **searchable standard list** (General Dentistry, Dental Hygiene and the ADA-recognized dental specialties) **plus "Other / custom specialty"**:
    - choosing a standard value stores its stable standard code;
    - "Other" shows a required free-text field;
    - custom values are visibly labelled as custom;
    - there is no unrestricted editable combobox where a typo could look like a standard specialty;
    - the list stays extensible later for jurisdiction-specific deployments.
- **PD-34:** "Deactivate", "Inactive" and "Reactivate" in user-facing language. Internal, API and audit identifiers stay unchanged unless a compatibility review explicitly authorizes changing them.
- **PD-37:**
  - Options are MM/DD/YYYY, YYYY-MM-DD and DD/MM/YYYY; the initial default is MM/DD/YYYY.
  - All printed materials use an unambiguous written form such as "14 Mar 1985".
  - It is a practice setting, not a guess from the browser's locale.
- **N27-1:** prescription workflow unification (D-04) and tooth drawings (E-01) stay out of ALV-N027. The shared dark-theme correction is approved.
- **N27-2 to N27-9:** approved as proposed:
  - N27-2: hidden notifications can be shown and unhidden (reversible);
  - N27-3: names in Sent;
  - N27-4: completing a task marks its own reminder read;
  - N27-5: the subject stays optional;
  - N27-6: the patient bar is hidden by context;
  - N27-7: blocked time is shown;
  - N27-8: grouped menus and workspace tabs, and "Add …" buttons that open forms;
  - N27-9: permission-aware dashboard widgets.

## Next (owner)
Before implementation, a detailed pre-implementation brief for ALV-N027. It covers the raw finding entries and exact scope boundaries, and has a separate security design section for PD-04, which now lives in the ACCT story's own brief. Both briefs go to independent review. No code before approval.
