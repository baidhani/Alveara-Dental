# ALV-N022 A3-R01: next-story impact

- **Migration:** one more (`AddTypedClearanceDocumentAndPerioDiagnosisLinks`); the ALV-N028 full run and the Windows installer path (ALV-N013) apply it. Its Down is refused once a typed link exists (by design).
- **API shape:** clearance receive/attach take `documentId` (free text refused); a perio Diagnosis link takes `diagnosisId`. A script or spec that sent free text must send ids (safety, perio-sessions and a3-display already do).
- **Legacy values:** existing free-text clearance references and perio diagnosis references stay as labelled legacy text; no conversion.
- **Route:** note templates are at `/clinical/templates`; the old patient URL redirects.
- **Walkthroughs** the reviewer may want in Stage 2: a3-display, safety, perio-sessions, and the neighbours listed in TEST_RESULTS.md.
- **Open observations (not A3 scope):** the sticky clinical status line covers part of the odontogram when the page is scrolled; the clinical note box accepts typing before its saved text has loaded (the `ClinicalNotes.test.tsx` timing).
