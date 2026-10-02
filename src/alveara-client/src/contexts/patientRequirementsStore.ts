import { createContext, useContext } from "react";
import type { PatientRequirements } from "../services/patientsApi";

/** The practice's extra required patient fields. The default (nothing extra) is STORY-003's behavior, so a form rendered outside the app shell behaves exactly as before. */
export const PatientRequirementsContext = createContext<PatientRequirements>({ requireEmail: false, requireSex: false });

export const usePatientRequirements = () => useContext(PatientRequirementsContext);
