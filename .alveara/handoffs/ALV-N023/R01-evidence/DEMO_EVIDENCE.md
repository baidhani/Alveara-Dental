# ALV-N023 R01: demo evidence

`treatment-plan-integrity-real-backend` on `f9c0d2a` (fresh database through `scripts/Invoke-RealBackendWalkthrough.ps1`, API on the real clock, Chromium). Set up through the API: practice name/address/phone, one location, one operatory, one appointment type, a dentist who is also the provider (Mon-Fri 08:00-17:00), patient Ann Alder with an encounter, one diagnosis and a two-procedure plan "Crown plan" (CROWN-1, POL-1). 7/7 passed (31 s on the sweep; 34 s on the first green run).

1. **Accept** - `01-accepted-not-booked.png`: the dentist records Ann's acceptance of CROWN-1 on screen; the plan starts "Proposed"; the procedure says "Not booked.".
2. **Book with treatment (OQ-1, DR-1)** - `02-booking-with-treatment.png`: the front desk's New appointment form offers only CROWN-1 under "For accepted treatment (optional)" (no plan title, no diagnosis); ticked and booked for 2030-01-14 09:00 in one save. The appointment's treatment list is exactly `["CROWN-1"]`. `03-plan-booked.png`: the plan shows "Booked: 1/14/2030, 9:00:00 AM with Dr. ..." and the state "In progress"; the plan actions are in their groups (D-03).
3. **Cancel (DR-1)** - `04-cancelled-not-booked.png`: after the appointment is cancelled (API, category "patient") the procedure says "The appointment on ... was cancelled, so it is not booked." and CROWN-1 is offered for booking again.
4. **Target date (PD-15)** - `05-overdue-target.png`: target date 2020-01-14 -> "That date has passed. Save it anyway as an overdue target date?"; nothing saved before confirming; after "Save as overdue" the item shows "Target date 1/14/2020" and "Overdue", and the booking line still says not booked.
5. **Finished and locked (PD-14, CR-3, addendum 01)** - `06-finished-locked.png`: with POL-1 declined and CROWN-1 completed (`complete-procedures`), the plan reads "Closed - all remaining procedures are completed or declined", shows the lock note, and offers only "Revise anyway..."; the API refuses withdraw with 409 `plan_finished`.
6. **Patient copy (F-183)** - `07-patient-copy-header.png`: the copy's header carries "Alder Street Dental", the address and "Phone: 555-010-0999".

Evidence JSON: `artifacts/walkthrough/treatment-plan-integrity-e2e.json`. All data is fake.
