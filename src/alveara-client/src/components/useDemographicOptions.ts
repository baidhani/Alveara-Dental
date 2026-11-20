import { useEffect, useState } from "react";
import { getDemographicOptions } from "../services/patientsApi";
import type { DemographicOptions } from "../services/patientsApi";

/** Used until the lists arrive, or if they cannot be loaded: the Sex list, and State typed as text (the server still checks every value). */
export const FALLBACK_DEMOGRAPHIC_OPTIONS: DemographicOptions = {
  sexValues: ["Female", "Male", "Other", "Unknown/unspecified"],
  usStates: [],
  countries: [{ code: "US", name: "United States" }],
  defaultCountry: "US",
};

let cached: DemographicOptions | null = null;

/**
 * ALV-N025 (F-032, F-033): the demographic value lists (GET /api/patients/demographic-options), read once per page load and kept; a failed read is not kept, so the
 * next form tries again. The form never blocks on them: until they arrive it offers the fallback, and the server remains the authority.
 */
export function useDemographicOptions(): DemographicOptions {
  const [options, setOptions] = useState<DemographicOptions>(cached ?? FALLBACK_DEMOGRAPHIC_OPTIONS);
  useEffect(() => {
    if (cached) return;
    let live = true;
    getDemographicOptions()
      .then((o) => {
        if (!o || !Array.isArray(o.sexValues) || !Array.isArray(o.usStates) || !Array.isArray(o.countries)) return;
        cached = o;
        if (live) setOptions(o);
      })
      .catch(() => { /* the fallback stays; the server still validates every value */ });
    return () => { live = false; };
  }, []);
  return options;
}

/** Test-only: forget the kept lists. */
export function resetDemographicOptionsForTest() {
  cached = null;
}
