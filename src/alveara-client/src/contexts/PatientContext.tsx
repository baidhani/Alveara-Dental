import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { ApiError } from "../services/authApi";
import { getPatient } from "../services/patientsApi";
import { PatientContext } from "./patientContextStore";
import type { PatientContextState } from "./patientContextStore";

/**
 * ALV-003-C01: the app-wide "patient in context" - the single owner of which patient the shared header and the patient
 * workspace are showing. Later clinical, treatment, document, billing and task stories read the patient from here
 * rather than inventing their own patient state.
 *
 * Switching patients can never leave the previous patient's information on screen:
 * - `selectPatient` replaces the state with "loading" for the NEW id synchronously, so nothing about the old patient survives a render;
 * - every load carries a sequence number and an AbortController, and a response is applied only if it is still the latest
 *   request for the latest selection - a slow answer for patient A arriving after patient B was chosen is discarded;
 * - patient-scoped panels are keyed by patient id by the workspace, so their own local state (forms, lists) is remounted.
 */
export function PatientContextProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<PatientContextState>({ kind: "none" });
  const sequence = useRef(0);
  const abort = useRef<AbortController | null>(null);
  const currentId = useRef<string | null>(null);

  const load = useCallback((patientId: string, keepCurrent: boolean) => {
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    const mine = ++sequence.current;
    currentId.current = patientId;
    if (!keepCurrent) setState({ kind: "loading", patientId });

    getPatient(patientId, controller.signal)
      .then((patient) => {
        if (sequence.current === mine) setState({ kind: "loaded", patientId, patient });
      })
      .catch((err: unknown) => {
        if (sequence.current !== mine || controller.signal.aborted) return;
        if (err instanceof ApiError && err.status === 404) setState({ kind: "not-found", patientId });
        else if (err instanceof ApiError && err.status === 403) setState({ kind: "denied", patientId });
        else setState({ kind: "error", patientId });
      });
  }, []);

  const selectPatient = useCallback(
    (patientId: string) => {
      // Already the patient in context (or loading): never refetch on a re-render. But an ABORTED load is not "loading" - React's
      // StrictMode (dev) unmounts and remounts, and the unmount cleanup below aborts the request, so the re-select must start it again.
      if (currentId.current === patientId && abort.current && !abort.current.signal.aborted) return;
      load(patientId, false);
    },
    [load]
  );

  const clearPatient = useCallback(() => {
    abort.current?.abort();
    sequence.current++;
    currentId.current = null;
    setState({ kind: "none" });
  }, []);

  const reload = useCallback(() => {
    if (currentId.current) load(currentId.current, true);
  }, [load]);

  useEffect(
    () => () => {
      abort.current?.abort();
      currentId.current = null; // an unmounted provider holds no selection; a remount starts clean
    },
    []
  );

  const value = useMemo(() => ({ state, selectPatient, clearPatient, reload }), [state, selectPatient, clearPatient, reload]);
  return <PatientContext.Provider value={value}>{children}</PatientContext.Provider>;
}
