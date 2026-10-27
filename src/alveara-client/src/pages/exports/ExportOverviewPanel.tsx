import type { ExportOverview } from "../../services/patientExportsApi";
import { groupTitle } from "./exportFormat";

/**
 * What a package holds and what it deliberately leaves out, read from the server (so it can never drift from what the package really does). Open by choice, not by default, because most people export often and
 * have read it; but it is one keystroke away, and the reason for every exclusion is stated.
 */
export function ExportOverviewPanel({ overview }: { overview: ExportOverview }) {
  return (
    <details className="alv-export__details">
      <summary>What is in a package and what is left out</summary>
      <h3>In the package</h3>
      <ul>{overview.domains.map((d) => <li key={d.key}>{d.title}</li>)}</ul>
      <p>The package also holds a readable page for each patient, a manifest that lists every file with its hash, and a README. Document files are included when you choose to.</p>
      <h3>Left out on purpose</h3>
      <ul>{overview.excluded.map((g) => <li key={g.key}><strong>{groupTitle(g.key)}.</strong> {g.reason}</li>)}</ul>
    </details>
  );
}
