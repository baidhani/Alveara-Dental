import { useState } from "react";
import { Button } from "../components/Button";
import { ConcurrencyConflictBanner } from "../components/ConcurrencyConflictBanner";
import { PatientPicker } from "../components/PatientPicker";
import { SafeLink } from "../components/SafeLink";
import { useAuth } from "../contexts/AuthContext";
import { ApiError, isConcurrencyConflict } from "../services/authApi";
import type { ConcurrencyConflictProblem } from "../services/authApi";
import { HOUSEHOLD_RELATIONSHIPS, displayName, getPatient, setGuarantor, setHousehold } from "../services/patientsApi";
import type { PatientDetail, PatientSummary } from "../services/patientsApi";
import "./PatientWorkspace.css";

/**
 * ALV-003-C01: the household and guarantor editor. The two are INDEPENDENT on purpose: a household is the people who belong
 * together; the guarantor is who is financially responsible. A guarantor need not live in the household, and household
 * members may have different guarantors. The server enforces the rules (guarantor must be an active, self-responsible
 * patient; one household per patient) and its message is shown next to the control that caused it.
 */
export function HouseholdGuarantorPanel({ patient, onChanged }: { patient: PatientDetail; onChanged: () => void }) {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission("EditPatients");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [guarantorError, setGuarantorError] = useState<string | null>(null);
  const [householdError, setHouseholdError] = useState<string | null>(null);

  const [newGuarantor, setNewGuarantor] = useState<PatientSummary | null>(null);
  const [newMember, setNewMember] = useState<PatientSummary | null>(null);
  const [memberRelationship, setMemberRelationship] = useState("");
  const [ownRelationship, setOwnRelationship] = useState(patient.household?.relationship ?? "");

  const household = patient.household;
  const otherMembers = household?.members.filter((m) => m.id !== patient.id) ?? [];

  async function run(action: () => Promise<unknown>, onError: (m: string) => void, success: string, after?: () => void) {
    if (busy) return;
    setBusy(true);
    onError("");
    setNotice(null);
    setConflict(null);
    try {
      await action();
      setNotice(success);
      after?.();
      onChanged();
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else onError(err instanceof ApiError ? err.message : "Could not save. Check your connection and try again.");
    } finally {
      setBusy(false);
    }
  }

  const saveGuarantor = (id: string | null) =>
    run(() => setGuarantor(patient.id, id, patient.rowVersion), setGuarantorError, id ? "Guarantor set." : "Patient is now responsible for themselves.", () => setNewGuarantor(null));

  // Adding a member changes THAT patient's row, so we read their current version first.
  const addMember = () =>
    run(
      async () => {
        const member = await getPatient(newMember!.id);
        await setHousehold(member.id, patient.id, memberRelationship, member.rowVersion);
      },
      setHouseholdError, "Added to the household.", () => { setNewMember(null); setMemberRelationship(""); }
    );

  const leaveHousehold = () =>
    window.confirm(`Remove ${displayName(patient)} from this household? Their record is not changed otherwise.`)
      ? run(() => setHousehold(patient.id, null, null, patient.rowVersion), setHouseholdError, "Removed from the household.")
      : undefined;

  const saveOwnRelationship = () =>
    run(() => setHousehold(patient.id, otherMembers[0].id, ownRelationship, patient.rowVersion), setHouseholdError, "Relationship saved.");

  return (
    <div className="alv-workspace__panel">
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={onChanged} />}
      <p className={notice ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{notice}</p>
      {!canEdit && <p className="alv-workspace__note">You can view this patient's household and guarantor but not change them.</p>}

      <section aria-labelledby="alv-guarantor-title" className="alv-workspace__block">
        <h2 id="alv-guarantor-title" className="alv-workspace__section-title">Guarantor (responsible party)</h2>
        <p data-testid="current-guarantor">
          {patient.guarantor ? (
            <>
              <SafeLink to={`/patients/${patient.guarantor.id}`} className="alv-workspace__link">{patient.guarantor.displayName}</SafeLink>
              {!patient.guarantor.isActive && " (inactive)"} is responsible for this patient.
            </>
          ) : (
            "This patient is responsible for themselves."
          )}
        </p>
        {patient.guaranteeFor.length > 0 && (
          <div>
            <p className="alv-workspace__note">This patient is the guarantor for:</p>
            <ul className="alv-workspace__list">
              {patient.guaranteeFor.map((p) => (
                <li key={p.id}>
                  <SafeLink to={`/patients/${p.id}`} className="alv-workspace__link">{p.displayName}</SafeLink>
                  {!p.isActive && " (inactive)"}
                </li>
              ))}
            </ul>
          </div>
        )}
        {canEdit && (
          <div className="alv-workspace__controls">
            <PatientPicker key={`guarantor-${patient.rowVersion}`} label="Choose a guarantor" selected={newGuarantor} onSelect={setNewGuarantor} excludeIds={[patient.id]} disabled={busy} />
            <div className="alv-workspace__actions">
              <Button type="button" variant="primary" disabled={!newGuarantor || busy} onClick={() => saveGuarantor(newGuarantor!.id)}>
                Set guarantor
              </Button>
              {patient.guarantor && (
                <Button type="button" disabled={busy} onClick={() => saveGuarantor(null)}>
                  Make self-responsible
                </Button>
              )}
            </div>
            {guarantorError && <p className="alv-form-field__error" role="alert">{guarantorError}</p>}
          </div>
        )}
      </section>

      <section aria-labelledby="alv-household-title" className="alv-workspace__block">
        <h2 id="alv-household-title" className="alv-workspace__section-title">Household</h2>
        {household ? (
          <>
            <table className="alv-workspace__table">
              <caption className="alv-workspace__caption">Household members</caption>
              <thead>
                <tr>
                  <th scope="col">Patient</th>
                  <th scope="col">Relationship</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {household.members.map((m) => (
                  <tr key={m.id}>
                    <td>
                      {m.id === patient.id ? <strong>{m.displayName} (this patient)</strong> : <SafeLink to={`/patients/${m.id}`} className="alv-workspace__link">{m.displayName}</SafeLink>}
                    </td>
                    <td>{m.relationship ?? "—"}</td>
                    <td>{m.isActive ? "Active" : "Inactive"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {canEdit && otherMembers.length > 0 && (
              <div className="alv-workspace__controls">
                <label className="alv-workspace__label" htmlFor="own-relationship">Relationship of {displayName(patient)} to the household</label>
                <div className="alv-workspace__actions">
                  <select id="own-relationship" className="alv-form-field__input alv-workspace__select" value={ownRelationship} onChange={(e) => setOwnRelationship(e.target.value)} disabled={busy}>
                    <option value="">Choose…</option>
                    {HOUSEHOLD_RELATIONSHIPS.map((r) => <option key={r} value={r}>{r}</option>)}
                  </select>
                  <Button type="button" disabled={busy || !ownRelationship || ownRelationship === household.relationship} onClick={saveOwnRelationship}>
                    Save relationship
                  </Button>
                </div>
              </div>
            )}
          </>
        ) : (
          <p>This patient is not in a household.</p>
        )}

        {canEdit && (
          <div className="alv-workspace__controls">
            <h3 className="alv-workspace__subtitle">{household ? "Add another household member" : "Start a household"}</h3>
            <PatientPicker key={`member-${patient.rowVersion}`} label="Choose a patient to add" selected={newMember} onSelect={setNewMember} excludeIds={[patient.id, ...(household?.members.map((m) => m.id) ?? [])]} disabled={busy} />
            <div className="alv-workspace__field">
              <label className="alv-workspace__label" htmlFor="member-relationship">Their relationship to this household</label>
              <select id="member-relationship" className="alv-form-field__input alv-workspace__select" value={memberRelationship} onChange={(e) => setMemberRelationship(e.target.value)} disabled={busy}>
                <option value="">Choose…</option>
                {HOUSEHOLD_RELATIONSHIPS.map((r) => <option key={r} value={r}>{r}</option>)}
              </select>
            </div>
            <div className="alv-workspace__actions">
              <Button type="button" variant="primary" disabled={!newMember || !memberRelationship || busy} onClick={addMember}>
                Add to household
              </Button>
              {household && (
                <Button type="button" variant="danger" disabled={busy} onClick={leaveHousehold}>
                  Remove {patient.firstName} from household
                </Button>
              )}
            </div>
            {householdError && <p className="alv-form-field__error" role="alert">{householdError}</p>}
          </div>
        )}
      </section>
    </div>
  );
}
