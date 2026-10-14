# Prescriptions (STORY-016)

**Requirement:** REQ-016 - the system must support prescriptions with medication, dosage, and allergy checks.
**Where it lives:** the patient workspace's **Prescriptions** tab (`/patients/:id/prescriptions`), beside the Diagnoses and Safety tabs.
**Who can use it:** reading needs `ViewPrescriptions` (dentist, hygienist, assistant, admin). Prescribing needs `ManagePrescriptions` (dentist, admin) and a CSRF token. Front desk, billing and the office manager have no tab and are refused (403) by the API. Both permissions were appended to the `Permission` enum; no existing permission changed.

## What it does, and what it deliberately does not

A prescription is one medication, one dose, one frequency, written by one prescriber for one patient, **never changed or deleted** once saved.

**"Dosage accuracy" here means the entry is well formed, not that the dose is clinically right.** This application has no drug-knowledge source, so it cannot know a safe dose. The checks are documented local rules (`PrescriptionRules`):

- the medication is 2 to 200 characters of letters, digits and ordinary punctuation;
- the dose is a positive number with at most 3 decimals, in a known unit (mg, mcg, g, mL, tablet, capsule, drop, puff, unit);
- the dose is not above a **typing-error ceiling** for that unit (for example 5000 mg). The ceiling catches slips such as an extra zero. It is **not** a clinical limit and the message says so;
- the frequency is one of a fixed list (Once, Once daily ... Every 12 hours, At bedtime, As needed).

Every problem is listed together with its field and a stable code, and one problem means nothing is saved (`validation_failed`, 400). The screen applies the same rules first and keeps everything typed.

**The allergy check compares the medication's NAME with the patient's active recorded allergies** (word by word, ignoring case and punctuation). It does not know drug families, brand names or cross-reactions (it does not know that amoxicillin is a penicillin), so "no recorded match" is **never** a clearance and is worded that way everywhere. It also does not check drug interactions.

| Result | Meaning | What the prescriber must do |
|---|---|---|
| `RecordedAllergyMatch` | the name shares a word with a recorded active allergy | give their own words (`reviewNote`) or change the medication |
| `NotEstablished` | the allergy list was never reviewed (or is unknown) | give their own words that they asked the patient |
| `NoRecordedMatch` | the list is established and nothing matched by name | nothing; still not a clearance |

The allergy list comes from the shared patient-safety context (ALV-N011). **It fails closed:** if the context cannot be read, nothing is saved (`allergy_check_unavailable`, 503) and the screen says the check could not be completed, never "no allergies".

## How the promises are kept

| Promise | How |
|---|---|
| Allergy and dosage checked when prescribing | `PrescriptionService.RecordAsync` runs `PrescriptionRules.Check` and the allergy assessment before any write; `POST .../prescriptions/check` previews both without saving. |
| An incorrect entry prompts for correction | 400 `validation_failed` with `problems`; the form lists them with a link to each box, marks the box invalid, moves focus and keeps what was typed. A refused save writes nothing. |
| Every entry is logged with user and timestamp | The prescription row (prescriber user id, UTC time, correlation id) and a PHI-free `PrescriptionRecorded` audit entry are saved in **one** `SaveChanges`. If the audit write fails nothing is saved and the person is told (`save_failed`, 503, no internals). The audit entry never contains the medication, dose or allergy. |
| Repeats and retries are safe | A save carries an idempotency key, unique per patient: same key and entry returns the saved one (`replayed: true`), same key with a different entry is `idempotency_key_reused` (409), simultaneous saves make one. |
| Nothing is lost or rewritten | `Prescriptions` has an INSTEAD OF UPDATE/DELETE trigger (error 51174); the migration's Down refuses while rows exist; check constraints repeat the rules so a bad row cannot be written around the service. |

## API

- `GET /api/prescriptions/options` - units, frequencies, ceilings and the two limitation sentences. `ViewPrescriptions`.
- `GET /api/patients/{id}/prescriptions` - the patient's prescriptions, newest first. `ViewPrescriptions`.
- `POST /api/patients/{id}/prescriptions/check` - body `{ idempotencyKey, medication, doseValue, doseUnit, frequency, reviewNote }`; returns problems, the allergy assessment and whether a review note is required. Saves nothing. `ManagePrescriptions` plus CSRF.
- `POST /api/patients/{id}/prescriptions` - same body; returns `{ prescription, replayed }`. `ManagePrescriptions` plus CSRF.
- Refusals carry `error`, `message`, `problems` and `assessment`: `validation_failed` (400), `patient_not_found` (404), `idempotency_key_reused` and `allergy_review_required` (409), `allergy_check_unavailable` and `save_failed` (503).

There is no route that changes or deletes a prescription.

## Failure modes handled, and not

Handled: malformed entry, allergy match, unestablished allergy list, unreadable safety context, failed audit write, replay, conflicting replay, concurrent saves, missing permission, missing CSRF token.
Not handled: drug interactions, clinical appropriateness of a dose, drug-family or brand-name allergy matching, encounter linkage, correcting or finalizing a prescription (ALV-016-C01 owns the correction lifecycle). Retry policy: none server-side (a single local database transaction); the client may repeat the same request safely.

## Schema (migration `AddPrescriptions`)

`Prescriptions` (patient, medication, dose value and unit, frequency, allergy outcome, list state and matches, review note, idempotency key, request hash, correlation id, prescriber, created time). Trigger 51174 (never updated or deleted); the next free number is 51175.
