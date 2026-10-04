import { useState } from "react";
import { Button } from "../../components/Button";
import { CONDITION_LABELS, changeFindingState, withdrawFinding } from "../../services/odontogramApi";
import type { Chart, Finding } from "../../services/odontogramApi";
import { ReasonForm } from "../safety/SafetyForms";
import type { SaveResult } from "../safety/SafetyForms";
import { FindingHistory } from "./FindingHistory";
import { describeFinding, stateClass, when } from "./odontogramText";
import { SURFACE_NAMES } from "./toothNumbering";

export type RunChange = (change: () => Promise<Chart>, savedText: string) => Promise<SaveResult>;

interface Props {
  finding: Finding;
  /** How the tooth reads on the chart (in the numbering system shown), for the status line. */
  toothLabel: string;
  canWrite: boolean;
  busy: boolean;
  run: RunChange;
}

/**
 * STORY-006: one finding on a tooth: what it is, its state as a word, who recorded it and last changed it. A finding moves FORWARD only: a Diagnosed one can be planned or completed,
 * a Planned one completed; Existing and Completed are final (to correct one, withdraw it and record it again). Withdrawing needs a reason and keeps the finding in its history. The
 * controls are drawn only for people who may change the chart; the server checks every call regardless.
 */
export function FindingRow({ finding, toothLabel, canWrite, busy, run }: Props) {
  const [withdrawing, setWithdrawing] = useState(false);
  const surfaceName = finding.surface ? SURFACE_NAMES[finding.surface] : null;
  const name = describeFinding(finding.condition, surfaceName);
  const canPlan = finding.state === "Diagnosed";
  const canComplete = finding.state === "Diagnosed" || finding.state === "Planned";
  const move = (state: "Planned" | "Completed") => void run(() => changeFindingState(finding.id, state, finding.rowVersion), `${name} on tooth ${toothLabel} is now ${state}.`);

  return (
    <li className="alv-odonto__finding">
      <p className="alv-odonto__finding-title">
        <strong>{CONDITION_LABELS[finding.condition] ?? finding.condition}</strong>
        <span> · {surfaceName ? `${surfaceName} surface` : "Whole tooth"}</span>
        <span className={`alv-odonto__chip ${stateClass(finding.state)}`}>{finding.state}</span>
      </p>
      <p className="alv-odonto__finding-meta">
        Recorded by {finding.recordedByName ?? "Staff member"} on {when(finding.recordedAtUtc)}
        {finding.updatedAtUtc ? `; last changed by ${finding.updatedByName ?? "Staff member"} on ${when(finding.updatedAtUtc)}` : ""}.
      </p>
      {canWrite && !withdrawing && (
        <div className="alv-clinical__row-actions">
          {canPlan && <Button type="button" onClick={() => move("Planned")} disabled={busy} aria-label={`Plan: ${name}`}>Plan</Button>}
          {canComplete && <Button type="button" onClick={() => move("Completed")} disabled={busy} aria-label={`Complete: ${name}`}>Complete</Button>}
          <Button type="button" onClick={() => setWithdrawing(true)} disabled={busy} aria-label={`Withdraw: ${name}`}>Withdraw</Button>
        </div>
      )}
      {withdrawing && (
        <ReasonForm
          label={`Why is "${name}" being withdrawn?`}
          hint="Use this for an entry that was wrong. It leaves the chart but stays in the history, with who withdrew it and this reason."
          required
          submitLabel="Withdraw finding"
          ariaLabel={`Withdraw finding: ${name}`}
          busy={busy}
          onCancel={() => setWithdrawing(false)}
          onSubmit={async (reason) => {
            const refused = await run(() => withdrawFinding(finding.id, reason, finding.rowVersion), `${name} on tooth ${toothLabel} withdrawn.`);
            if (!refused) setWithdrawing(false);
            return refused;
          }}
        />
      )}
      <FindingHistory id={finding.id} name={name} version={finding.rowVersion} />
    </li>
  );
}
