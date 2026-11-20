import { useEffect, useState } from "react";
import { PageHeader } from "../components/PageHeader";
import { SafeLink } from "../components/SafeLink";
import { EmptyState, ErrorState, LoadingState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";
import { getDemographicReview } from "../services/patientsApi";
import { ApiError } from "../services/authApi";
import type { DemographicReview } from "../services/patientsApi";
import "./PatientWorkspace.css";

type State = { kind: "loading" } | { kind: "denied" } | { kind: "error" } | { kind: "loaded"; review: DemographicReview };

const FIELD: Record<string, string> = { sex: "Sex", state: "State (United States)" };

/**
 * ALV-N025 (F-032, F-033; owner decisions DEM-1, DEM-2): the patients whose stored Sex or US State is not on its list. Recognizable variants were standardized by
 * the system; what is listed here needs a person. Each row opens the patient, where the old value is shown with a prompt to choose a supported one; a patient leaves
 * this list once corrected. Needs EditPatients (the route and the server both check it).
 */
export function DemographicReviewPage() {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    getDemographicReview(controller.signal)
      .then((review) => setState({ kind: "loaded", review }))
      .catch((err) => {
        if (controller.signal.aborted) return;
        setState(err instanceof ApiError && err.status === 403 ? { kind: "denied" } : { kind: "error" });
      });
    return () => controller.abort();
  }, []);

  return (
    <>
      <PageHeader
        title="Review demographics"
        description="Patients whose stored sex or US state is not one of the supported values. Open the patient, choose a supported value and save; the patient then leaves this list."
      />
      {state.kind === "loading" && <LoadingState label="Loading the review list…" />}
      {state.kind === "denied" && <PermissionDenied requiredPermission="EditPatients" />}
      {state.kind === "error" && <ErrorState title="Could not load the review list" />}
      {state.kind === "loaded" && state.review.items.length === 0 && (
        <EmptyState title="Nothing to review" description="Every stored sex and US state is a supported value." />
      )}
      {state.kind === "loaded" && state.review.items.length > 0 && (
        <>
          <table className="alv-workspace__table">
            <caption className="alv-workspace__caption">Stored values that are not on their list</caption>
            <thead>
              <tr>
                <th scope="col">Patient</th>
                <th scope="col">MRN</th>
                <th scope="col">Field</th>
                <th scope="col">Stored value</th>
              </tr>
            </thead>
            <tbody>
              {state.review.items.map((i) => (
                <tr key={`${i.patientId}-${i.field}`}>
                  <td><SafeLink to={`/patients/${i.patientId}`}>{i.displayName}</SafeLink></td>
                  <td>{i.mrn}</td>
                  <td>{FIELD[i.field] ?? i.field}</td>
                  <td>{i.value}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {state.review.truncated && <p className="alv-workspace__note">Showing the first 500 patients; correct these to see more.</p>}
        </>
      )}
    </>
  );
}
