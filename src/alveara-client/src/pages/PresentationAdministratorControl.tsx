import { useEffect, useState } from "react";
import { Button } from "../components/Button";
import { ApiError } from "../services/authApi";
import { getPresentationAdministrator, setPresentationAdministrator } from "../services/loginPresentationApi";

/**
 * ALV-N020-C01 (review BRIEFING-R03 Q-3): Security administration -> an account -> "Sign-in presentation administrator". The ordinary, audited path to grant
 * or revoke the account permission ManageLoginPresentation; shown to ManageUsers holders (the server enforces ManageUsers and CSRF, and refuses a disabled or
 * Unassigned account). It is an ordinary permission of a named account: not a hidden or special account.
 */
export function PresentationAdministratorControl({ accountId, username }: { accountId: string; username: string }) {
  const [granted, setGranted] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: "ok" | "error"; text: string } | null>(null);

  useEffect(() => {
    let live = true;
    getPresentationAdministrator(accountId)
      .then((r) => { if (live) setGranted(r.granted); })
      .catch(() => { if (live) setMessage({ kind: "error", text: "Could not read whether this account is the sign-in presentation administrator." }); });
    return () => { live = false; };
  }, [accountId]);

  async function change(next: boolean) {
    setBusy(true);
    setMessage(null);
    try {
      const r = await setPresentationAdministrator(accountId, next);
      setGranted(r.granted);
      setMessage({ kind: "ok", text: next ? `${username} can now change the sign-in screen.` : `${username} can no longer change the sign-in screen.` });
    } catch (err) {
      setMessage({ kind: "error", text: err instanceof ApiError ? err.message : "The change could not be saved." });
    } finally {
      setBusy(false);
    }
  }

  return (
    <section style={{ marginTop: "1.5rem" }} aria-labelledby="presentation-admin-heading">
      <h2 id="presentation-admin-heading">Sign-in presentation administrator</h2>
      <p>Changes the sign-in screen: its background, display mode and footer notice. Each grant and revoke is recorded in the audit log.</p>
      {granted === null && !message && <p>Checking…</p>}
      {granted !== null && (
        <>
          <p role="status">{granted ? "This account is a sign-in presentation administrator." : "This account is not a sign-in presentation administrator."}</p>
          <Button variant="secondary" disabled={busy} onClick={() => change(!granted)}>
            {granted ? "Remove sign-in presentation permission" : "Make sign-in presentation administrator"}
          </Button>
        </>
      )}
      {message && <p role={message.kind === "error" ? "alert" : "status"}>{message.text}</p>}
    </section>
  );
}
