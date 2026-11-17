import { Navigate, useLocation, useParams } from "react-router-dom";
import { TemplatesPage } from "./TemplatesPage";
import { backToPatient, templatesPath } from "./templatesPath";

/** ALV-N022 A3 (Q-5j): the practice-level note templates page (/clinical/templates). */
export function PracticeTemplatesPage() {
  const { search } = useLocation();
  return <TemplatesPage backTo={backToPatient(search)} />;
}

/** ALV-N022 A3 (Q-5j): the old patient URL (/patients/:id/clinical/templates) still works: it redirects to the practice page, keeping the way back to that patient. */
export function LegacyPatientTemplatesRedirect() {
  const { patientId } = useParams();
  return <Navigate to={templatesPath(patientId)} replace />;
}
