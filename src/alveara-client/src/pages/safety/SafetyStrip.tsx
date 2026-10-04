import { useEffect, useState } from "react";
import { SafeLink } from "../../components/SafeLink";
import { useAuth } from "../../contexts/AuthContext";
import { SAFETY_CHANGED_EVENT, getSafetySummary } from "../../services/safetyApi";
import type { SafetySummary } from "../../services/safetyApi";
import { plural, safetyHeadline } from "./safetyText";
import "./Safety.css";

interface Props {
  patientId: string;
  /** The class for the link to the safety details: the header and the workspace style their links differently. */
  linkClassName?: string;
  /** The landmark's name. Two strips can be on one screen (the header's and an open encounter's), and landmarks must be told apart. */
  label?: string;
}

/**
 * ALV-N011: the persistent patient-safety area - on every patient screen (in the patient header) and at the top of an encounter, so safety information is in view BEFORE
 * diagnosis, planning, treatment and prescribing. It shows counts and the highest severity, then what is NOT established (a record section never reviewed, unknown, or changed
 * since it was confirmed), alerts the signed-in person has not acknowledged, and items needing review - all in words. "Nothing recorded" is said as exactly that, and every gap
 * adds "nothing listed here does not mean none". It only draws for people who may read clinical documentation; a failed read says so (it never shows an empty, reassuring strip).
 * It re-reads when the safety panel changes something.
 */
export function SafetyStrip({ patientId, linkClassName = "alv-workspace__link", label = "Patient safety" }: Props) {
  const { hasPermission } = useAuth();
  const allowed = hasPermission("ViewClinicalDocumentation");
  const [summary, setSummary] = useState<SafetySummary | null>(null);
  const [failed, setFailed] = useState(false);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const onChanged = (e: Event) => {
      const changed = (e as CustomEvent<{ patientId: string }>).detail?.patientId;
      if (!changed || changed === patientId) setTick((t) => t + 1);
    };
    window.addEventListener(SAFETY_CHANGED_EVENT, onChanged);
    return () => window.removeEventListener(SAFETY_CHANGED_EVENT, onChanged);
  }, [patientId]);

  useEffect(() => {
    if (!allowed) return;
    const controller = new AbortController();
    getSafetySummary(patientId, controller.signal)
      .then((s) => {
        setSummary(s);
        setFailed(false);
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [allowed, patientId, tick]);

  if (!allowed) return null;
  if (failed) {
    return (
      <p className="alv-safety-strip alv-safety-strip--error" role="alert">
        Patient safety information could not be loaded, so do not assume there is none.{" "}
        <button type="button" className="alv-safety-strip__retry" onClick={() => setTick((t) => t + 1)}>Try again</button>
      </p>
    );
  }
  if (summary === null || summary.patientId !== patientId) return <p className="alv-safety-strip alv-safety-strip--loading" role="status">Loading patient safety information…</p>;

  const urgent = summary.highestSeverity === "Critical" || summary.highestSeverity === "High";
  const level = summary.activeAlertCount + summary.activeAllergyCount > 0 ? (urgent ? "high" : "some") : summary.gaps.length > 0 ? "gaps" : "none";
  return (
    <section className={`alv-safety-strip alv-safety-strip--${level}`} aria-label={label}>
      <p className="alv-safety-strip__line">
        <strong>Patient safety:</strong> {safetyHeadline(summary)}
      </p>
      {(summary.unacknowledgedAlertCount > 0 || summary.needsAttentionCount > 0 || summary.gaps.length > 0) && (
        <ul className="alv-safety-strip__notes">
          {summary.unacknowledgedAlertCount > 0 && <li>{plural(summary.unacknowledgedAlertCount, "alert", "alerts")} you have not acknowledged yet.</li>}
          {summary.needsAttentionCount > 0 && <li>{plural(summary.needsAttentionCount, "item needs", "items need")} review.</li>}
          {summary.gaps.map((g) => <li key={g.section}>{g.message}</li>)}
        </ul>
      )}
      <p className="alv-safety-strip__more">
        <SafeLink to={`/patients/${patientId}/safety`} className={linkClassName}>Open safety details</SafeLink>
      </p>
    </section>
  );
}
