import type { Diagnosis } from "../../services/diagnosisApi";
import { displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { REGION_LABELS, sourceText } from "./diagnosisStructureRules";

const when = (iso: string) => new Date(iso).toLocaleString();
const LINK_LABELS: Record<string, string> = { Finding: "Finding", PerioExam: "Periodontal chart" };

/**
 * ALV-013-C01: the context a diagnosis was made in, as chips: the tooth or oral region, the coding (system and code, as typed; never presented as checked), where it came from when it was not entered here,
 * and every finding or periodontal chart it is linked to, with who linked it and when. A chip is always text, so no state is carried by colour alone. Nothing here says anything about treatment plans:
 * that reference is shown separately, as unresolved.
 */
export function DiagnosisChips({ diagnosis: d, numbering }: { diagnosis: Diagnosis; numbering: NumberingSystem }) {
  const source = sourceText(d.source, d.sourceNote);
  const links = d.links ?? [];
  return (
    <>
      <ul className="alv-dx__chips" aria-label="Context of this diagnosis">
        {d.toothKey && <li className="alv-dx__chip">Tooth {displayTooth(d.toothKey, numbering)} ({toothName(d.toothKey)})</li>}
        {d.regionKey && <li className="alv-dx__chip">Region: {REGION_LABELS[d.regionKey] ?? d.regionKey}</li>}
        {d.codingSystem && d.code && <li className="alv-dx__chip">{d.codingSystem} {d.code}</li>}
        {source && <li className="alv-dx__chip">{source}</li>}
      </ul>
      {links.length > 0 && (
        <ul className="alv-dx__links" aria-label="Linked records">
          {links.map((l) => (
            <li key={`${l.linkType}${l.targetId}`}>
              <strong>{LINK_LABELS[l.linkType] ?? l.linkType}:</strong> {l.summary}
              <span className="alv-clinical__meta"> · linked by {l.linkedByName ?? "a staff member"}, {when(l.linkedAtUtc)}</span>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
