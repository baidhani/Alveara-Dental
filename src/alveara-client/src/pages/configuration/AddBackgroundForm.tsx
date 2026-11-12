import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ApiError } from "../../services/authApi";
import { addLoginBackground } from "../../services/loginPresentationApi";
import type { AddedBackground } from "../../services/loginPresentationApi";
import { DISPLAY_MODES } from "../login/loginBackgrounds";
import type { LoginDisplayMode } from "../login/loginBackgrounds";

/** The warning the reviewer required (correction 5): shown above the control and repeated here, and it must be acknowledged before an upload. */
export const PUBLIC_IMAGE_WARNING =
  "This image is public. Anyone who opens the sign-in page sees it before signing in. Never use an image that contains patient information, faces, " +
  "clinical documents, screenshots of this application or of patient data, or any other protected or confidential information.";

/**
 * ALV-N020-C01: adds a background from the administrator's computer. PNG or JPEG up to 5 MB; the server checks the real type, size and dimensions, removes
 * all metadata and stores only its own clean copy. The public-image warning must be ticked first (the server refuses without it too).
 */
export function AddBackgroundForm({ onAdded, onCancel }: { onAdded: (added: AddedBackground) => void; onCancel: () => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState("");
  const [mode, setMode] = useState<LoginDisplayMode>("Fill");
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!file) return setError("Choose a PNG or JPEG image.");
    if (name.trim().length === 0) return setError("Give the background a name.");
    if (!acknowledged) return setError("Confirm that the image is public and contains no patient or other confidential information.");
    setBusy(true);
    setError(null);
    try {
      onAdded(await addLoginBackground(file, name.trim(), mode, acknowledged));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "The image could not be added. Check the connection and try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-signin-config__add" onSubmit={submit} aria-label="Add background image">
      <p className="alv-signin-config__warning" role="note"><strong>Public image.</strong> {PUBLIC_IMAGE_WARNING}</p>
      <label>
        Image file (PNG or JPEG, up to 5 MB)
        <input type="file" accept="image/png,image/jpeg,.png,.jpg,.jpeg" onChange={(e) => setFile(e.target.files?.[0] ?? null)} disabled={busy} />
      </label>
      <label>
        Name
        <input type="text" value={name} maxLength={60} onChange={(e) => setName(e.target.value)} disabled={busy} />
      </label>
      <label>
        Suggested display mode
        <select value={mode} onChange={(e) => setMode(e.target.value as LoginDisplayMode)} disabled={busy}>
          {DISPLAY_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </label>
      <label className="alv-signin-config__ack">
        <input type="checkbox" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} disabled={busy} />
        I confirm this image is public and contains no patient or other confidential information.
      </label>
      {error && <p role="alert" className="alv-signin-config__error">{error}</p>}
      <div className="alv-signin-config__actions">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Adding…" : "Add image"}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
