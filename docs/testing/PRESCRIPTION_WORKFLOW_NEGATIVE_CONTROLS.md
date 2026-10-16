# ALV-016-C01 negative controls (prescription workflow)

**Purpose:** prove the workflow's new guards are held by tests, not just present. Each control breaks one guard on purpose and runs the workflow's own tests; a control is **killed** when at least one test fails. Risk declaration: HIGH (patient-safety records, an audit trail, a changed allergy or medication list, immutability triggers).

**Commands.** Backend, per control the narrowest class set that owns the guard: `dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~PrescriptionWorkflowServiceTests|FullyQualifiedName~PrescriptionWorkflowApiTests"` (service and controller guards), `...~PrescriptionWorkflowApiTests` (route attributes), `...~PrescriptionWorkflowSchemaTests` (migration triggers), `...~PrescriptionWorkflowRulesTests|...~PrescriptionWorkflowServiceTests` (rule ceilings). Frontend: `npx vitest run src/pages/prescription src/App.patientWorkspaceRoutes.test.tsx`. Clean baselines passed before the controls (frontend 66 of 66). Each mutant changed exactly one place, the tests ran, and the file was restored byte for byte.

| # | The guard that was broken | Result |
|---|---|---|
| W01 | finalize ignores a changed allergy or medication list | killed |
| W02 | an acknowledgement is accepted for lists that were not on screen | killed |
| W03 | the draft never reports that the lists changed | killed |
| W04 | finalize does not require instructions, quantity and refills | killed |
| W05 | finalize does not require the prescriber's review of an allergy match | killed |
| W06 | an unreadable safety context counts as no allergies (fails open) | killed |
| W07 | an amendment does not mark the original superseded | killed |
| W08 | an amendment can replace a prescription that already ended | killed |
| W09 | cancelling needs no reason | killed |
| W10 | an ended prescription can be ended again (the service-level check removed) | **survived, equivalent** (see below) |
| W11 | a print is not logged | killed |
| W12 | a prescription saved before the workflow can be printed | killed |
| W13 | a finalized draft can be discarded | **survived at first**; test `A_finalized_draft_cannot_be_discarded_and_nothing_about_it_changes` added; killed on re-run |
| W14 | discarding needs no CSRF token | **survived at first**; the CSRF test now covers every change route; killed on re-run |
| W15 | discontinuing needs no CSRF token | survived at first; same fix; killed on re-run |
| W16 | acknowledging the lists needs no CSRF token | survived at first; same fix; killed on re-run |
| W17 | the details trigger does nothing | killed |
| W18 | the events trigger does nothing | killed |
| W19 | a finalized draft can change | killed |
| W20 | a draft can be deleted | killed |
| W21 | a supersession can cross patients | killed |
| W22 | the migration rolls back over recorded drafts | killed |
| W23 | the quantity typing ceiling never applies | killed |
| W24 | refills have no ceiling | killed |
| F01 | finalizing opens the confirmation despite problems | killed |
| F02 | a changed list does not stop the finalization | killed |
| F03 | a print failure is not shown | killed |
| F04 | a prescription without details can be printed | killed |
| F05 | an ended prescription offers actions | killed |
| F06 | a box that is not a number is sent | killed |
| F07 | the not-configured banner says something else | killed |

31 controls; 26 killed on the first run, 4 survivors (W13 to W16) exposed real test gaps and are now killed, and 1 (W10) is an equivalent mutant.

**W10, equivalent.** The check removed (`!CanEnd(status)` in `EndAsync`) is the early, friendly refusal. Without it the request proceeds, the database's one-ending-event unique index refuses the second event, and the handler re-reads the status and refuses with the same code (`prescription_not_active`, 409) and the same message, with nothing saved. The observable behaviour is identical, so no test can tell them apart; the index and the re-read are the real guard and are exercised by the simultaneous-cancellation test and the schema test `A_prescription_is_finalized_once_and_ends_at_most_once`.

**Method note.** Restoring a mutated file leaves an older timestamp, so an incremental build can keep a mutant's binary; after the controls the sources were touched and rebuilt before the full regression, and the new tests were run on the clean build before the controls were recorded.
