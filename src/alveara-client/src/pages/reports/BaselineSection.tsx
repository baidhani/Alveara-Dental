import { useState } from "react";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { reportErrorMessage, setBaseline } from "../../services/reportsApi";
import type { Baseline } from "../../services/reportsApi";
import { formatMoment } from "../exports/exportFormat";
import { isDate, today } from "./reportFormat";
import type { Outcome } from "./useReport";

interface Props {
  baselines: Outcome<Baseline[]> | null;
  onRetry: () => void;
  onSaved: () => void;
}

const newKey = () => crypto.randomUUID();

/**
 * The baselines and the form to set one. A baseline is a period that is already over, measured once when it is set and stored with each metric's definition version, so it never changes afterwards. The form is
 * shown only to a person who may set one. Pressing Save twice, or again after a dropped connection, makes ONE baseline: the form keeps one key for the baseline being filled in and sends it with every attempt, and
 * makes a new key as soon as anything in the form is changed (a changed baseline is a different request).
 */
export function BaselineSection({ baselines, onRetry, onSaved }: Props) {
  const { hasPermission } = useAuth();
  const [name, setName] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [key, setKey] = useState(newKey);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);

  const trimmed = name.trim();
  const problem = trimmed.length === 0 ? "Give the baseline a name." : trimmed.length > 100 ? "The name is too long. Use at most 100 characters."
    : !isDate(from) || !isDate(to) ? "Choose the first and the last day of the period."
    : from > to ? "The first day must not be after the last day."
    : to >= today() ? "A baseline must be a period that is already over. Choose days before today." : null;

  const change = (set: (v: string) => void) => (e: React.ChangeEvent<HTMLInputElement>) => { set(e.target.value); setKey(newKey()); setSaved(null); setError(null); };

  const save = async () => {
    if (problem) return;
    setBusy(true); setError(null); setSaved(null);
    try {
      const made = await setBaseline({ name: trimmed, from, to }, key);
      setSaved(`Baseline "${made.name}" saved for ${made.from} to ${made.to}.`);
      setName(""); setFrom(""); setTo(""); setKey(newKey());
      onSaved();
    } catch (e) {
      setError(reportErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section aria-labelledby="alv-rep-baselines">
      <h2 id="alv-rep-baselines">Baselines</h2>
      <p className="alv-rep__hint">A baseline is a period that is already over, measured once and kept as it was. A later correction to the data does not change it.</p>
      {baselines === null && <LoadingState label="Loading the baselines…" />}
      {baselines && !baselines.ok && <ErrorState title="The baselines could not be shown" description={baselines.message} action={<Button onClick={onRetry}>Try again</Button>} />}
      {baselines?.ok && (baselines.data.length === 0
        ? <EmptyState title="No baseline has been set" description="Until one is set, nothing can be compared and no change is shown." />
        : (
          <table className="alv-rep__table">
            <caption className="alv-rep__sr">Baselines, newest first</caption>
            <thead><tr><th scope="col">Name</th><th scope="col">Period</th><th scope="col">Set</th><th scope="col">Metrics stored</th></tr></thead>
            <tbody>{baselines.data.map((b) => <tr key={b.id}><th scope="row">{b.name}</th><td>{b.from} to {b.to}</td><td>{formatMoment(b.setAtUtc, b.timeZoneId)}</td><td>{b.metrics.length}</td></tr>)}</tbody>
          </table>
        ))}

      {hasPermission("ManageMeasurementBaselines") && (
        <form className="alv-rep__filters" aria-label="Set a baseline" onSubmit={(e) => { e.preventDefault(); void save(); }}>
          <h3>Set a baseline</h3>
          <label>Name<input type="text" value={name} maxLength={120} onChange={change(setName)} /></label>
          <div className="alv-rep__dates">
            <label>First day<input type="date" value={from} onChange={change(setFrom)} /></label>
            <label>Last day<input type="date" value={to} onChange={change(setTo)} /></label>
          </div>
          {(name !== "" || from !== "" || to !== "") && problem && <p role="alert" className="alv-rep__problem">{problem}</p>}
          {error && <p role="alert" className="alv-rep__problem">{error}</p>}
          {saved && <p role="status" className="alv-rep__hint">{saved}</p>}
          <div className="alv-rep__apply"><Button type="submit" variant="primary" disabled={busy || !!problem}>{busy ? "Saving…" : "Save the baseline"}</Button></div>
        </form>
      )}
    </section>
  );
}
