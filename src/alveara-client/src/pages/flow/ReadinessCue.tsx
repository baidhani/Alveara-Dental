import type { CheckInReadiness } from "../../services/visitsApi";
import { READINESS_WORDS } from "./visitLabels";

/**
 * ALV-011-C01: the check-in form cue on a visit card. It says exactly what the server reported - which required forms are really signed on their current
 * wording - and never more: no required forms reads as "none required" (not as something completed), and a signature on older wording is named as such.
 * Looking at a form never changes it; this cue only reads.
 */
export function ReadinessCue({ readiness }: { readiness: CheckInReadiness }) {
  if (readiness.requiredCount === 0) return <p className="flow-cue flow-cue--none">No forms are required at check-in.</p>;
  if (readiness.ready) return <p className="flow-cue flow-cue--ready">Required forms complete ({readiness.completeCount} of {readiness.requiredCount}).</p>;
  const open = readiness.items.filter((i) => i.status !== "Complete");
  return (
    <div className="flow-cue flow-cue--open">
      <p className="flow-cue__summary">Forms: {readiness.completeCount} of {readiness.requiredCount} complete.</p>
      <ul className="flow-cue__list">
        {open.map((i) => (
          <li key={i.templateId}>
            {i.title} — {READINESS_WORDS[i.status]}
            {i.status === "SignedEarlierVersion" && ` (version ${i.signedVersionNumber}; version ${i.currentVersionNumber} is current)`}
          </li>
        ))}
      </ul>
    </div>
  );
}
