import { createContext, useContext } from "react";
import type { PatientDetail } from "../services/patientsApi";

export type PatientContextState =
  | { kind: "none" }
  | { kind: "loading"; patientId: string }
  | { kind: "loaded"; patientId: string; patient: PatientDetail }
  | { kind: "not-found"; patientId: string }
  | { kind: "denied"; patientId: string }
  | { kind: "error"; patientId: string };

interface PatientContextValue {
  state: PatientContextState;
  /** Make `patientId` the patient in context. The previous patient is dropped IMMEDIATELY (before the new one loads). */
  selectPatient: (patientId: string) => void;
  /** Drop the patient in context. */
  clearPatient: () => void;
  /** Re-read the current patient (e.g. after an edit, or to resolve a concurrency conflict). */
  reload: () => void;
}

export const PatientContext = createContext<PatientContextValue | null>(null);

export function usePatientContext(): PatientContextValue {
  const ctx = useContext(PatientContext);
  if (!ctx) throw new Error("usePatientContext must be used inside <PatientContextProvider> (the app shell provides it).");
  return ctx;
}
