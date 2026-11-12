import { useCallback, useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../services/authApi";
import { adminBackgroundImageUrl, getAdminLoginPresentation, saveLoginPresentation, setLoginBackgroundActive } from "../../services/loginPresentationApi";
import type { AdminLoginPresentation, SaveLoginPresentation } from "../../services/loginPresentationApi";
import { bundledBackground, DISPLAY_MODES } from "../login/loginBackgrounds";
import type { LoginDisplayMode } from "../login/loginBackgrounds";
import { AddBackgroundForm, PUBLIC_IMAGE_WARNING } from "./AddBackgroundForm";
import "./SignInScreenTab.css";

const MODE_NOTES: Record<LoginDisplayMode, string> = {
  Tile: "Repeats the image at its natural size.",
  Stretch: "Fills the page exactly. May distort the image.",
  Fill: "Keeps proportions; edges may be cropped. Recommended for photographs.",
};
const NOTICE_MAX = 1000;

type Draft = Omit<SaveLoginPresentation, "rowVersion">;

function draftOf(p: AdminLoginPresentation): Draft {
  return {
    backgroundKind: p.backgroundKind, bundledKey: p.bundledKey, addedImageId: p.addedImageId, displayMode: p.displayMode,
    footerNoticeText: p.footerNoticeText, useDefaultNotice: p.footerNoticeText === null, showFooterNotice: p.showFooterNotice,
  };
}

/**
 * ALV-N020-C01: Practice configuration -> Sign-in screen, for the holder of the account permission ManageLoginPresentation (the server enforces it on every
 * call). Pick a background (bundled or added), its display mode, and the footer notice; add a background from this computer; make an added one unavailable
 * (never deleted; the one shown now cannot be made unavailable).
 */
export function SignInScreenTab({ onDirtyChange }: { onDirtyChange?: (dirty: boolean) => void }) {
  const [data, setData] = useState<AdminLoginPresentation | null>(null);
  const [failed, setFailed] = useState(false);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: "ok" | "error"; text: string } | null>(null);

  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let live = true;
    getAdminLoginPresentation()
      .then((fresh) => { if (live) { setData(fresh); setDraft(draftOf(fresh)); } })
      .catch(() => { if (live) setFailed(true); });
    return () => { live = false; };
  }, [reloadKey]);
  const load = useCallback(() => { setFailed(false); setReloadKey((k) => k + 1); }, []);

  const dirty = !!data && !!draft && JSON.stringify(draft) !== JSON.stringify(draftOf(data));
  useUnsavedChangesWarning(dirty);
  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);

  if (failed) return <ErrorState title="Could not load the sign-in screen settings" action={<Button onClick={load}>Retry</Button>} />;
  if (!data || !draft) return <LoadingState label="Loading sign-in screen settings…" />;

  const selectedBundled = draft.backgroundKind === "Bundled" ? data.bundled.find((b) => b.key === draft.bundledKey) : undefined;
  const tileAllowed = draft.backgroundKind === "Added" || (selectedBundled?.tileAllowed ?? true);
  const shownId = data.backgroundKind === "Added" ? data.addedImageId : null;
  const update = (patch: Partial<Draft>) => { setDraft({ ...draft, ...patch }); setMessage(null); };

  async function save() {
    if (!draft || !data) return;
    setBusy(true);
    try {
      const saved = await saveLoginPresentation({ ...draft, rowVersion: data.rowVersion });
      setData(saved);
      setDraft(draftOf(saved));
      setMessage({ kind: "ok", text: "Sign-in screen saved." });
    } catch (err) {
      const conflict = err instanceof ApiError && err.status === 409 && err.code !== "background_unavailable";
      setMessage({ kind: "error", text: conflict ? "Someone else changed these settings. Reload to see the latest version." : err instanceof ApiError ? err.message : "The settings could not be saved." });
    } finally {
      setBusy(false);
    }
  }

  async function setAvailable(id: string, available: boolean, rowVersion: string) {
    setBusy(true);
    try {
      await setLoginBackgroundActive(id, available, rowVersion);
      load();
      setMessage({ kind: "ok", text: available ? "Background made available." : "Background made unavailable." });
    } catch (err) {
      setMessage({ kind: "error", text: err instanceof ApiError ? err.message : "The change could not be saved." });
    } finally {
      setBusy(false);
    }
  }

  const active = data.added.filter((a) => a.isActive);
  const inactive = data.added.filter((a) => !a.isActive);

  return (
    <section className="alv-signin-config" aria-labelledby="signin-config-heading">
      <h2 id="signin-config-heading">Sign-in screen</h2>
      {message && <p role={message.kind === "error" ? "alert" : "status"} className={`alv-signin-config__message alv-signin-config__message--${message.kind}`}>{message.text}</p>}

      <fieldset>
        <legend>Background</legend>
        <div className="alv-signin-config__grid" role="radiogroup" aria-label="Background">
          {data.bundled.map((b) => (
            <label key={b.key} className="alv-signin-config__choice">
              <input type="radio" name="background" checked={draft.backgroundKind === "Bundled" && draft.bundledKey === b.key}
                onChange={() => update({ backgroundKind: "Bundled", bundledKey: b.key, addedImageId: null, displayMode: b.tileAllowed ? draft.displayMode : "Fill" })} />
              <img src={bundledBackground(b.key)?.url} alt="" width={96} height={96} />
              <span>{b.displayName} <small>(built in)</small></span>
            </label>
          ))}
          {active.map((a) => (
            <div key={a.id} className="alv-signin-config__added">
              <label className="alv-signin-config__choice">
                <input type="radio" name="background" checked={draft.backgroundKind === "Added" && draft.addedImageId === a.id}
                  onChange={() => update({ backgroundKind: "Added", bundledKey: null, addedImageId: a.id, displayMode: a.suggestedDisplayMode })} />
                <img src={adminBackgroundImageUrl(a.id)} alt="" width={96} height={96} />
                <span>{a.displayName}</span>
              </label>
              <Button type="button" onClick={() => setAvailable(a.id, false, a.rowVersion)} disabled={busy || a.id === shownId}
                aria-label={`Make ${a.displayName} unavailable`}>Make unavailable</Button>
            </div>
          ))}
        </div>
        <p className="alv-signin-config__hint">The background shown now cannot be made unavailable; choose another one and save first. {active.length} of {data.maxActiveAdded} added backgrounds available.</p>
        {inactive.length > 0 && (
          <ul className="alv-signin-config__inactive" aria-label="Unavailable backgrounds">
            {inactive.map((a) => (
              <li key={a.id}>{a.displayName} <Button type="button" onClick={() => setAvailable(a.id, true, a.rowVersion)} disabled={busy}>Make available</Button></li>
            ))}
          </ul>
        )}
        <p className="alv-signin-config__warning" role="note"><strong>Public image.</strong> {PUBLIC_IMAGE_WARNING}</p>
        {adding
          ? <AddBackgroundForm onCancel={() => setAdding(false)} onAdded={(a) => { setAdding(false); load(); setMessage({ kind: "ok", text: `"${a.displayName}" added.` }); }} />
          : <Button type="button" onClick={() => setAdding(true)} disabled={busy}>Add background image</Button>}
      </fieldset>

      <fieldset>
        <legend>Display mode</legend>
        {DISPLAY_MODES.map((m) => (
          <label key={m} className="alv-signin-config__mode">
            <input type="radio" name="display-mode" value={m} checked={draft.displayMode === m} disabled={m === "Tile" && !tileAllowed} onChange={() => update({ displayMode: m })} />
            <span><strong>{m}</strong> — {m === "Tile" && !tileAllowed ? "This background cannot be tiled." : MODE_NOTES[m]}</span>
          </label>
        ))}
      </fieldset>

      <fieldset>
        <legend>Footer notice</legend>
        <label className="alv-signin-config__mode">
          <input type="checkbox" checked={draft.showFooterNotice} onChange={(e) => update({ showFooterNotice: e.target.checked })} />
          Show footer notice on login screen
        </label>
        {draft.useDefaultNotice ? (
          <>
            <p>Using the deployment default:</p>
            <p className="alv-signin-config__preview">{data.deploymentDefaultNotice ?? "(no deployment default is set)"}</p>
            <Button type="button" onClick={() => update({ useDefaultNotice: false, footerNoticeText: data.deploymentDefaultNotice ?? "" })}>Write a custom notice</Button>
          </>
        ) : (
          <>
            <label>
              Notice text (plain text, up to {NOTICE_MAX.toLocaleString()} characters)
              <textarea rows={8} maxLength={NOTICE_MAX} value={draft.footerNoticeText ?? ""} onChange={(e) => update({ footerNoticeText: e.target.value })} />
            </label>
            <p className="alv-signin-config__hint" aria-live="polite">{(draft.footerNoticeText ?? "").length} / {NOTICE_MAX}</p>
            <Button type="button" onClick={() => update({ useDefaultNotice: true, footerNoticeText: null })}>Use deployment default</Button>
          </>
        )}
      </fieldset>

      <div className="alv-signin-config__actions">
        <Button type="button" variant="primary" onClick={save} disabled={busy || !dirty}>{busy ? "Saving…" : "Save"}</Button>
        <Button type="button" onClick={() => { setDraft(draftOf(data)); setMessage(null); }} disabled={busy || !dirty}>Discard changes</Button>
      </div>
    </section>
  );
}
