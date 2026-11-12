# ALV-N020 R01: next-story impact

Next scheduled item: ALV-N021 (item 58), not started and not authorized until ALV-N020 is `COMPLETE`.

- **Chain:** `chain/stage-1.spec.ts` now runs after stage 0 in every accumulated chain run and takes about 6-7 minutes (step 1.12 waits for a real 5-minute session). Later stages can reuse its accounts (`chain-front`, `chain-billing`, `chain-dentist` (disabled after F-021), `chain-pending`) and the two patients (Brian Baker, Alice Anderson).
- **Tracked exceptions:** F-037, F-021 and F-155 pass; the evidence builder marks them stale, so they can be removed from `docs/stabilization/TRACKED_EXCEPTIONS.json` by whoever next maintains it.
- **Session behavior:** a network error or any 5xx during a refresh no longer signs a person out. Tests in later stories that expect a sign-out on a 5xx must expect the connection notice instead; only a 401 ends a session.
- **Return destination:** a first-visit deep link lands on `/` after sign-in; only the same user after an expiry returns to the page.
- **Staff inactivation:** needs ManageUsers as well as configuration authority; a practice manager can no longer inactivate a staff profile (they can reactivate one).
- **Notifications:** a fourth kind, `staff.inactivation_failed`, exists. Code that switches on notification kinds should handle it; source keys must not contain a hyphen (`CK_Notifications_SourceKey`).
- **Migration:** `20261112140957_AddStaffInactivationFailedNotification` is the latest; any later migration that rolls back through it on a database that holds such a notification will be refused (51275).
- **Test baseline:** the next broad backend run (ALV-N028) gains the new classes `StaffInactivationServiceTests`, `StaffInactivationAlertTests`, `StaffInactivationNotificationSchemaTests` and the added cases in `AccountServiceLoginTests`, `ConfigurationApiTests`, `RealMigrationInventoryTests`.
