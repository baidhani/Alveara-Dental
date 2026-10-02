import type { FormStatus } from "../../services/formsApi";
import "./Forms.css";

const LABELS: Record<FormStatus, string> = { Draft: "Draft - unsigned", Signed: "Signed", Void: "Void" };

/** Unsigned/draft/signed/void is always spelled out in words; colour is only a reinforcement. */
export function FormStatusBadge({ status, wasSigned }: { status: FormStatus; wasSigned?: boolean }) {
  const text = status === "Void" && wasSigned ? "Void (was signed)" : LABELS[status];
  return <span className={`alv-form-status alv-form-status--${status.toLowerCase()}`}>{text}</span>;
}
