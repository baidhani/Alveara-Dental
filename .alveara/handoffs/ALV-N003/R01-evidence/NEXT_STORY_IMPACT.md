# ALV-N003 R01 — Next Story Impact

## Interfaces later stories build on

- **`SchedulingConfiguration`** (`GetSnapshotAsync`, `CheckProviderAvailabilityAsync`) and `GET api/config/scheduling[/availability-check]` are the scheduling story's consumption seam: active providers (with weekly hours), active operatories, active appointment types with default durations, practice time zone. A booking flow should validate every slot through `CheckProviderAvailabilityAsync` rather than re-deriving hours/blocks.
- **Historical resolution:** appointments/records referencing a provider, operatory or appointment type store its id and resolve it through the services' `Get…Async` (returns inactive rows); never filter those lookups by `IsActive`. Only *offering* new bookings uses the active-only snapshot.
- **`ConfigEntityPanel`/`ConfigurationWrite`** are the reusable configuration pattern. A domain story that owns its own settings (note templates, document categories, recall defaults, payment methods) supplies fields/columns/API calls to `ConfigEntityPanel` and uses `ConfigurationWrite` (same-save audit + row version + unique-violation mapping); it adds its own tab to the hub or its own page.
- **Time rules to follow:** local wall-clock for recurring hours (`DayOfWeek` + `TimeOnly`); UTC instants for one-off events converted through `IPracticeClock.FromPracticeLocal` with the *Reject* DST policy — surface `invalid_local_time` to the user.
- **`ManagePracticeConfiguration`** (Admin, OfficeManager) gates configuration; scheduling reads need `ViewSchedule`.
- **Provider selection UX:** providers are `ProviderProfile` rows reached through a staff profile; `displayName` comes from the staff profile.

## Assumptions / migrations / unresolved decisions

- One active location in first release (service + filtered unique index). Adding multi-location later means dropping `UX_PracticeLocations_SingleActive` and giving operatories/providers explicit location choice; `Operatory.LocationId` is already stored.
- The migration adds unique indexes on `StaffProfiles.DisplayName` and `ProviderProfiles.StaffProfileId`; any environment with pre-existing duplicate staff rows must be cleaned before upgrading (none exist in this build).
- Weekly availability is replaced atomically as a whole and is not individually version-tokened (last-writer-wins on that schedule); a future scheduling-heavy story may want a schedule-level version if concurrent editing becomes real.
- No measurement events were emitted (no agreed success metric depends on configuration).

## Gate A

Row A8 ("Practice/staff/provider/operatory/scheduling configuration works") can be evaluated once this story is approved; it was not evaluated or marked here. Gate A as a whole still waits for `ALV-N004`.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 10 (`ALV-N004`) begins. `ALV-N004`'s backup/restore scope should include the new configuration tables as persistent assets of the release.
