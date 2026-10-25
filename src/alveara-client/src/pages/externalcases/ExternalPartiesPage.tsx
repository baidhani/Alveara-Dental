import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { caseErrorMessage, CONTACT_MAX, createParty, isStaleCaseChange, listParties, PARTY_NAME_MAX, setPartyActive, updateParty } from "../../services/externalCasesApi";
import type { ExternalParty, PartyKind } from "../../services/externalCasesApi";
import { partyKindLabel } from "./externalCaseFormat";
import "../followups/followups.css";

/**
 * ALV-009-C02: the list of outside parties (referral providers and lab vendors) the practice sends work to. A party is added once per kind and name, edited with the version the screen showed, and made inactive instead of being
 * deleted, so the cases already sent to it keep it and new ones stop offering it. A change by someone else meanwhile is explained and the list reloaded rather than overwritten; a failed load says it could not load.
 */
export function ExternalPartiesPage() {
  const [parties, setParties] = useState<ExternalParty[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [kind, setKind] = useState<PartyKind>("LabVendor");
  const [name, setName] = useState("");
  const [contact, setContact] = useState("");
  const [editing, setEditing] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editContact, setEditContact] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback((signal?: AbortSignal) => {
    listParties(undefined, true, signal).then((p) => { setParties(p); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, []);
  useEffect(() => { const controller = new AbortController(); load(controller.signal); return () => controller.abort(); }, [load]);

  const fail = (err: unknown) => { setError(caseErrorMessage(err)); setStale(isStaleCaseChange(err)); };
  const reload = () => { setError(null); setStale(false); setEditing(null); setStatus("Reloaded the list."); load(); };

  async function add(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const n = name.trim(); const c = contact.trim();
    if (n === "" || n.length > PARTY_NAME_MAX) { setError(`Write the name (up to ${PARTY_NAME_MAX} characters).`); return; }
    if (c.length > CONTACT_MAX) { setError(`Keep the contact details to ${CONTACT_MAX} characters.`); return; }
    setBusy(true); setError(null); setStatus(null);
    try { await createParty(kind, n, c === "" ? null : c); setName(""); setContact(""); setStatus(`Added: ${n}.`); load(); } catch (err) { fail(err); } finally { setBusy(false); }
  }

  async function save(p: ExternalParty) {
    const n = editName.trim(); const c = editContact.trim();
    if (n === "" || n.length > PARTY_NAME_MAX || c.length > CONTACT_MAX) { setError("Check the name and the contact details."); return; }
    setBusy(true); setError(null); setStatus(null);
    try { await updateParty(p.id, n, c === "" ? null : c, p.rowVersion); setEditing(null); setStatus(`Saved: ${n}.`); load(); } catch (err) { fail(err); } finally { setBusy(false); }
  }

  async function toggle(p: ExternalParty) {
    setBusy(true); setError(null); setStatus(null);
    try { await setPartyActive(p.id, !p.isActive, p.rowVersion); setStatus(`${p.name} is now ${p.isActive ? "inactive" : "active"}.`); load(); } catch (err) { fail(err); } finally { setBusy(false); }
  }

  if (failed && parties === null) return <ErrorState title="Could not load the outside parties" description="Check your connection and reload the page." />;
  if (parties === null) return <LoadingState label="Loading the outside parties…" />;

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-parties-title">
      <h1 id="alv-parties-title" className="alv-workspace__section-title">Outside parties</h1>
      <p className="alv-clinical__meta">The referral providers and dental laboratories the practice sends work to. A party is never deleted: make it inactive and it stops being offered for new cases while the cases already sent to it keep it.</p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? ""}</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {stale && <Button type="button" onClick={reload}>Reload the list</Button>}

      <form className="alv-fu-form" onSubmit={add} aria-label="Add an outside party" noValidate>
        <div className="alv-form-field">
          <label htmlFor="alv-party-kind" className="alv-form-field__label">Kind</label>
          <select id="alv-party-kind" value={kind} onChange={(e) => setKind(e.target.value as PartyKind)}><option value="LabVendor">Lab vendor</option><option value="ReferralProvider">Referral provider</option></select>
        </div>
        <div className="alv-form-field">
          <label htmlFor="alv-party-name" className="alv-form-field__label">Name *</label>
          <input id="alv-party-name" className="alv-form-field__input" value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
        </div>
        <div className="alv-form-field">
          <label htmlFor="alv-party-contact" className="alv-form-field__label">Contact details (optional)</label>
          <input id="alv-party-contact" className="alv-form-field__input" value={contact} onChange={(e) => setContact(e.target.value)} autoComplete="off" />
        </div>
        <div className="alv-fu-actions"><Button type="submit" variant="primary" disabled={busy}>Add the party</Button></div>
      </form>

      <table className="alv-fu-table">
        <caption className="alv-fu-sr">Outside parties</caption>
        <thead><tr><th scope="col">Name</th><th scope="col">Kind</th><th scope="col">Contact</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
        <tbody>
          {parties.map((p) => (
            <tr key={p.id}>
              {editing === p.id ? (
                <>
                  <td><label className="alv-fu-sr" htmlFor={`edit-name-${p.id}`}>Name of {p.name}</label><input id={`edit-name-${p.id}`} className="alv-form-field__input" value={editName} onChange={(e) => setEditName(e.target.value)} /></td>
                  <td>{partyKindLabel(p.kind)}</td>
                  <td><label className="alv-fu-sr" htmlFor={`edit-contact-${p.id}`}>Contact details of {p.name}</label><input id={`edit-contact-${p.id}`} className="alv-form-field__input" value={editContact} onChange={(e) => setEditContact(e.target.value)} /></td>
                  <td>{p.isActive ? "Active" : "Inactive"}</td>
                  <td className="alv-fu-actions"><Button type="button" variant="primary" onClick={() => save(p)} disabled={busy}>Save</Button><Button type="button" onClick={() => setEditing(null)}>Cancel</Button></td>
                </>
              ) : (
                <>
                  <td><strong>{p.name}</strong></td><td>{partyKindLabel(p.kind)}</td><td>{p.contactDetails ?? "None"}</td><td>{p.isActive ? "Active" : "Inactive"}</td>
                  <td className="alv-fu-actions">
                    <Button type="button" onClick={() => { setEditing(p.id); setEditName(p.name); setEditContact(p.contactDetails ?? ""); setError(null); setStatus(null); }} aria-label={`Edit ${p.name}`}>Edit</Button>
                    <Button type="button" onClick={() => toggle(p)} disabled={busy} aria-label={`${p.isActive ? "Make inactive" : "Make active"}: ${p.name}`}>{p.isActive ? "Make inactive" : "Make active"}</Button>
                  </td>
                </>
              )}
            </tr>
          ))}
        </tbody>
      </table>
      {parties.length === 0 && <p>No outside parties yet. Add the first one above.</p>}
    </section>
  );
}
