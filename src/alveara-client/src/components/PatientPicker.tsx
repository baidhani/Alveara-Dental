import { useEffect, useId, useState } from "react";
import { Button } from "./Button";
import { searchPatients, displayName } from "../services/patientsApi";
import type { PatientSummary } from "../services/patientsApi";
import "./PatientPicker.css";

interface Props {
  label: string;
  /** Patients that cannot be chosen (e.g. the patient being edited). */
  excludeIds?: string[];
  disabled?: boolean;
  selected: PatientSummary | null;
  onSelect: (patient: PatientSummary | null) => void;
}

const DEBOUNCE_MS = 250;

/**
 * Find-and-choose one existing patient (used for the guarantor and for adding a household member). Typing searches the
 * patient directory (name, birth date or phone); each result is a button, so the whole flow works from the keyboard
 * (type, Tab to a result, Enter). Every search aborts the one before it, and a late response from an earlier keystroke is
 * never shown.
 */
export function PatientPicker({ label, excludeIds = [], disabled, selected, onSelect }: Props) {
  const inputId = useId();
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<PatientSummary[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const text = query.trim();
    if (text.length < 2) return; // too short to search; the change handler already cleared the previous results
    const controller = new AbortController();
    const timer = setTimeout(() => {
      searchPatients(text, false, controller.signal)
        .then((rows) => {
          setFailed(false);
          setResults(rows);
        })
        .catch(() => {
          if (!controller.signal.aborted) setFailed(true);
        });
    }, DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query]);

  if (selected) {
    return (
      <div className="alv-patient-picker">
        <p className="alv-patient-picker__selected" data-testid="picker-selected">
          <span className="alv-patient-picker__label">{label}:</span> {displayName(selected)} (born {selected.dateOfBirth})
        </p>
        <Button type="button" onClick={() => { onSelect(null); setQuery(""); setResults(null); }} disabled={disabled}>
          Choose someone else
        </Button>
      </div>
    );
  }

  const visible = (results ?? []).filter((p) => !excludeIds.includes(p.id));
  return (
    <div className="alv-patient-picker">
      <label htmlFor={inputId} className="alv-patient-picker__label">
        {label}
      </label>
      <input
        id={inputId}
        type="search"
        className="alv-form-field__input alv-patient-picker__input"
        value={query}
        disabled={disabled}
        autoComplete="off"
        placeholder="Name, birth date (yyyy-mm-dd) or phone"
        onChange={(e) => {
          setQuery(e.target.value);
          if (e.target.value.trim().length < 2) { setResults(null); setFailed(false); }
        }}
      />
      <p className="alv-patient-picker__status" role="status">
        {failed ? "Search failed. Try again." : results === null ? "" : `${visible.length} ${visible.length === 1 ? "patient" : "patients"} found`}
      </p>
      {visible.length > 0 && (
        <ul className="alv-patient-picker__results" aria-label={`${label} results`}>
          {visible.map((p) => (
            <li key={p.id}>
              <button type="button" className="alv-patient-picker__result" onClick={() => onSelect(p)} disabled={disabled}>
                {displayName(p)} · born {p.dateOfBirth} · {p.phone}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
