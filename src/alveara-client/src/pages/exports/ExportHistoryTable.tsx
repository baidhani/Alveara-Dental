import { SafeLink } from "../../components/SafeLink";
import type { ExportHistoryItem } from "../../services/patientExportsApi";
import { formatBytes } from "../backup/backupMessages";
import { formatMoment, resultOf } from "./exportFormat";

interface Props { items: ExportHistoryItem[]; timeZone: string | null; canOpenPatients: boolean }

/** One line per export, newest first. The result is a word as well as a colour; a line never names a patient (the patient is a link to the record, for people who may open records). */
export function ExportHistoryTable({ items, timeZone, canOpenPatients }: Props) {
  return (
    <table className="alv-fu-table">
      <caption className="alv-fu-sr">Export history, newest first</caption>
      <thead>
        <tr>
          <th scope="col">When</th><th scope="col">Who</th><th scope="col">What</th><th scope="col">For</th>
          <th scope="col">Result</th><th scope="col">Size</th><th scope="col">File check</th>
        </tr>
      </thead>
      <tbody>
        {items.map((item) => {
          const result = resultOf(item);
          const delivered = item.result === "Complete" || item.result === "Partial";
          return (
            <tr key={item.exportId}>
              <td>{formatMoment(item.requestedAtUtc, timeZone)}</td>
              <td>{item.requestedBy}</td>
              <td>{item.kind === "Package" ? "Package" : "Readable record"}{item.includesDocumentFiles ? " with documents" : ""}</td>
              <td>
                {item.scope === "AllPatients"
                  ? `All patients (${item.patientCount})`
                  : item.patientId && canOpenPatients ? <SafeLink to={`/patients/${item.patientId}`}>Open the patient</SafeLink> : "One patient"}
              </td>
              <td>
                <span className={`alv-export__badge alv-export__badge--${result.tone}`}>{result.label}</span>
                {item.problemCount > 0 && <span> {item.problemCount} {item.problemCount === 1 ? "problem" : "problems"}</span>}
              </td>
              <td>{delivered ? formatBytes(item.sizeBytes) : "-"}</td>
              <td>{item.sha256 ? <code title={item.sha256}>{item.sha256.slice(0, 12)}</code> : "-"}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
