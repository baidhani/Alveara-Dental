import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { useAuth } from "./AuthContext";
import { PatientRequirementsContext } from "./patientRequirementsStore";
import { getRegistrationSettings } from "../services/patientsApi";
import type { PatientRequirements } from "../services/patientsApi";

/** Fired by the settings page after a successful save so every open form picks up the new requirements without a reload. */
export const PATIENT_REQUIREMENTS_CHANGED = "alveara:patient-requirements-changed";

const NONE: PatientRequirements = { requireEmail: false, requireSex: false };

/**
 * ALV-003-C01: loads the practice's extra required patient fields once for the signed-in shell (only for users who can see patients) and
 * serves them to the registration and details forms. The forms use them to mark and prompt; the SERVER enforces them regardless, so a
 * failed or slow load only means the form falls back to the always-required fields and the server's per-field message does the rest.
 */
export function PatientRequirementsProvider({ children }: { children: ReactNode }) {
  const { hasPermission } = useAuth();
  const canSee = hasPermission("ViewPatientRecords");
  const [requirements, setRequirements] = useState<PatientRequirements>(NONE);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    const onChanged = () => setReloadToken((n) => n + 1);
    window.addEventListener(PATIENT_REQUIREMENTS_CHANGED, onChanged);
    return () => window.removeEventListener(PATIENT_REQUIREMENTS_CHANGED, onChanged);
  }, []);

  useEffect(() => {
    if (!canSee) return;
    const controller = new AbortController();
    getRegistrationSettings(controller.signal)
      .then((s) => setRequirements({ requireEmail: s.requireEmail, requireSex: s.requireSex }))
      .catch(() => {
        // Not fatal and not silent in effect: the server still enforces the real requirements on save.
        if (!controller.signal.aborted) setRequirements(NONE);
      });
    return () => controller.abort();
  }, [canSee, reloadToken]);

  return <PatientRequirementsContext.Provider value={requirements}>{children}</PatientRequirementsContext.Provider>;
}
