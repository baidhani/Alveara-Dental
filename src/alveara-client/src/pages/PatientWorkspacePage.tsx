import { useLayoutEffect } from "react";
import { NavLink, Outlet, useOutletContext, useParams } from "react-router-dom";
import { patientWorkspaceTabs } from "../app/patientWorkspaceTabs";
import { Button } from "../components/Button";
import { PageHeader } from "../components/PageHeader";
import { ErrorState, LoadingState } from "../components/StatePatterns";
import { SafeLink } from "../components/SafeLink";
import { useAuth } from "../contexts/AuthContext";
import { usePatientContext } from "../contexts/patientContextStore";
import type { PatientDetail } from "../services/patientsApi";
import { HouseholdGuarantorPanel } from "./HouseholdGuarantorPanel";
import { PatientDetailsPanel } from "./PatientDetailsPanel";
import { EncounterView } from "./clinical/EncounterView";
import { TemplatesPage } from "./clinical/notes/TemplatesPage";
import { SafetyPanel } from "./safety/SafetyPanel";
import { OdontogramPanel } from "./odontogram/OdontogramPanel";
import { PerioPanel } from "./perio/PerioPanel";
import { DiagnosesPanel } from "./diagnosis/DiagnosesPanel";
import { PrescriptionsPanel } from "./prescription/PrescriptionsPanel";
import { FollowUpsPanel } from "./followups/FollowUpsPanel";
import { PatientExternalCasesPanel } from "./externalcases/PatientExternalCasesPanel";
import { PrescriptionWorkflowPanel } from "./prescription/workflow/PrescriptionWorkflowPanel";
import { TreatmentPlansPanel } from "./treatmentplan/TreatmentPlansPanel";
import { BillingPanel } from "./billing/BillingPanel";
import { PatientClinicalPanel } from "./clinical/PatientClinicalPanel";
import { DocumentsPanel } from "./documents/DocumentsPanel";
import { PatientFormView } from "./forms/PatientFormView";
import { PatientFormsPanel } from "./forms/PatientFormsPanel";
import { PatientHistoryPanel } from "./PatientHistoryPanel";
import { MergeHistoryBanner } from "./patientmerge/MergeHistoryBanner";
import { ExportPatientButton } from "./portability/ExportPatientButton";
import { DownloadReadableRecordButton } from "./exports/DownloadReadableRecordButton";
import "./PatientWorkspace.css";

interface WorkspaceOutlet {
  patient: PatientDetail;
  reload: () => void;
}

/**
 * ALV-003-C01: the persistent patient workspace (/patients/:patientId/...). It makes the URL's patient THE patient in context
 * (the shared header in the shell shows the same one) and renders the workspace tabs from `patientWorkspaceTabs` - the extension
 * point later stories add their tabs to.
 *
 * Patient switching is the safety-critical part:
 * - the context is selected in a layout effect, before the browser paints, so the previous patient's header never shows under the new URL;
 * - nothing from the context is rendered unless it is for THIS URL's patient (`state.patientId === patientId`), so there is no frame
 *   where patient B's URL shows patient A's data;
 * - the tab content is keyed by patient id, so every panel's own state (form values, pickers, history) is discarded on a switch.
 */
export function PatientWorkspacePage() {
  const { patientId = "" } = useParams();
  const { state, selectPatient, reload } = usePatientContext();
  const { hasPermission } = useAuth();

  useLayoutEffect(() => {
    if (patientId) selectPatient(patientId);
  }, [patientId, selectPatient]);

  if (state.kind === "none" || state.patientId !== patientId || state.kind === "loading") {
    return <LoadingState label="Loading patient…" />;
  }
  if (state.kind === "not-found") {
    return (
      <ErrorState
        title="That patient was not found"
        description="The link may be wrong. Search for the patient instead."
        action={<SafeLink to="/patients" className="alv-button alv-button--primary">Find a patient</SafeLink>}
      />
    );
  }
  if (state.kind === "denied") {
    return <ErrorState title="You don't have permission to view this" description="Your role does not hold the permission to view patient records." />;
  }
  if (state.kind === "error") {
    return <ErrorState title="Could not load the patient" action={<Button onClick={reload}>Retry</Button>} />;
  }

  const patient = state.patient;
  const tabs = patientWorkspaceTabs.filter((t) => !t.requiredPermission || hasPermission(t.requiredPermission));

  return (
    <div key={patient.id}>
      <PageHeader
        title="Patient workspace"
        description="Everything about this patient in one place. The patient shown above stays selected while you move between sections."
        actions={!patient.mergedAtUtc && (hasPermission("ExportPatientData") || hasPermission("ExportPatientReadableRecord")) ? (
          <>
            {hasPermission("ExportPatientData") && <ExportPatientButton patientId={patient.id} />}
            {hasPermission("ExportPatientReadableRecord") && <DownloadReadableRecordButton patientId={patient.id} />}
          </>
        ) : undefined}
      />
      <MergeHistoryBanner patientId={patient.id} />
      <nav className="alv-workspace__tabs" aria-label="Patient workspace sections">
        {tabs.map((tab) => (
          <NavLink
            key={tab.id}
            to={tab.path ? `/patients/${patient.id}/${tab.path}` : `/patients/${patient.id}`}
            end={tab.path === ""}
            className={({ isActive }) => `alv-workspace__tab${isActive ? " alv-workspace__tab--active" : ""}`}
          >
            {tab.label}
          </NavLink>
        ))}
      </nav>
      <Outlet context={{ patient, reload } satisfies WorkspaceOutlet} />
    </div>
  );
}

export function PatientDetailsTab() {
  const { patient, reload } = useOutletContext<WorkspaceOutlet>();
  return <PatientDetailsPanel patient={patient} onChanged={reload} />;
}

export function PatientHouseholdTab() {
  const { patient, reload } = useOutletContext<WorkspaceOutlet>();
  return <HouseholdGuarantorPanel patient={patient} onChanged={reload} />;
}

export function PatientFormsTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  return <PatientFormsPanel patient={patient} />;
}

export function PatientDocumentsTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <DocumentsPanel patient={patient} canImport={hasPermission("ImportDocuments")} />;
}

export function PatientFormTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { formId = "" } = useParams();
  return <PatientFormView key={formId} patient={patient} formId={formId} />;
}

export function PatientClinicalTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  return <PatientClinicalPanel patient={patient} />;
}

export function PatientSafetyTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  return <SafetyPanel patient={patient} />;
}

export function PatientOdontogramTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <OdontogramPanel patient={patient} canWrite={hasPermission("ManageClinicalNotes")} canManageConditions={hasPermission("ManageClinicalTemplates")} />;
}

export function PatientPerioTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <PerioPanel patient={patient} canWrite={hasPermission("ManageClinicalNotes")} />;
}

export function PatientDiagnosesTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <DiagnosesPanel patient={patient} canWrite={hasPermission("ManageClinicalNotes")} />;
}

export function PatientPrescriptionsTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <PrescriptionsPanel patient={patient} canPrescribe={hasPermission("ManagePrescriptions")} />;
}

/** STORY-009: the patient's follow-up tasks. Anyone who may view them sees the list; people who may manage them can also create, edit, complete and cancel. */
export function PatientFollowUpsTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <FollowUpsPanel patientId={patient.id} canManage={hasPermission("ManageFollowUps")} />;
}

/** ALV-009-C02: the patient's referrals and lab cases. Anyone who may view them reads the list and the timeline; people who may manage them can also start a case. */
export function PatientExternalCasesTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <PatientExternalCasesPanel patientId={patient.id} canManage={hasPermission("ManageExternalCases")} />;
}

export function PatientPrescriptionComposerTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <PrescriptionWorkflowPanel patient={patient} canPrescribe={hasPermission("ManagePrescriptions")} />;
}

export function PatientTreatmentPlanTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <TreatmentPlansPanel patient={patient} canWrite={hasPermission("ManageTreatmentPlans")} />;
}

export function PatientBillingTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { hasPermission } = useAuth();
  return <BillingPanel patient={patient} canWrite={hasPermission("ManageBilling")} canViewClinical={hasPermission("ViewClinicalDocumentation")} />;
}

export function PatientClinicalTemplatesTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  return <TemplatesPage patient={patient} />;
}

export function PatientEncounterTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  const { encounterId = "" } = useParams();
  return <EncounterView key={encounterId} patient={patient} encounterId={encounterId} />;
}

export function PatientHistoryTab() {
  const { patient } = useOutletContext<WorkspaceOutlet>();
  return <PatientHistoryPanel patient={patient} />;
}
