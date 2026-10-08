import { useState } from "react";
import { createBrowserRouter, createRoutesFromElements, Outlet, Route, RouterProvider } from "react-router-dom";
import { AppShell } from "./app/AppShell";
import { NavigationGuard } from "./app/NavigationGuard";
import { RequireAuth, RequirePermission } from "./app/RouteGuards";
import { DashboardPage } from "./pages/DashboardPage";
import { ShowcasePage } from "./pages/ShowcasePage";
import { SystemStatusPage } from "./pages/SystemStatusPage";
import { LoginPage } from "./pages/LoginPage";
import { MfaChallengePage } from "./pages/MfaChallengePage";
import { ResetPasswordPage } from "./pages/ResetPasswordPage";
import { AdminUsersPage } from "./pages/AdminUsersPage";
import { AdminUserDetailPage } from "./pages/AdminUserDetailPage";
import { MfaSettingsPage } from "./pages/MfaSettingsPage";
import { PermissionMatrixPage } from "./pages/PermissionMatrixPage";
import { AuditLogPage } from "./pages/AuditLogPage";
import { ConfigurationHubPage } from "./pages/ConfigurationHubPage";
import { BackupRecoveryPage } from "./pages/BackupRecoveryPage";
import { ProcedureCatalogPage } from "./pages/ProcedureCatalogPage";
import { PatientRegistrationPage } from "./pages/PatientRegistrationPage";
import { PatientRegistrationSettingsPage } from "./pages/PatientRegistrationSettingsPage";
import { PatientSearchPage } from "./pages/PatientSearchPage";
import { PatientClinicalTab, PatientClinicalTemplatesTab, PatientDetailsTab, PatientSafetyTab, PatientOdontogramTab, PatientPerioTab, PatientDiagnosesTab, PatientEncounterTab, PatientFormTab, PatientFormsTab, PatientHistoryTab, PatientHouseholdTab, PatientWorkspacePage } from "./pages/PatientWorkspacePage";
import { FormTemplatesPage } from "./pages/forms/FormTemplatesPage";
import { SchedulePage } from "./pages/scheduling/SchedulePage";
import { CalendarPage } from "./pages/calendar/CalendarPage";
import { FlowBoardPage } from "./pages/flow/FlowBoardPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { NotificationProvider } from "./components/Notification";
import { AuthProvider } from "./contexts/AuthContext";
import { UnsavedChangesProvider } from "./contexts/UnsavedChangesContext";
import { DisconnectedBanner } from "./components/DisconnectedBanner";
import { useConnectionStatus } from "./hooks/useConnectionStatus";

/**
 * ALV-N009: rendered above every route (auth pages included, not just the signed-in shell) -
 * "can't reach the server" is a connectivity fact independent of whether the caller happens to
 * be signed in yet, and the caller needs to see it on the login screen just as much as anywhere
 * else (a failed permissions check during a genuine outage should never look like a plain
 * "session expired" with no further explanation).
 *
 * ALV-N003 R02: also hosts the NavigationGuard. Blocking a route transition needs a data router
 * (`useBlocker`), which is why the app now uses createBrowserRouter/RouterProvider rather than
 * <BrowserRouter>; every route below is otherwise unchanged.
 */
function RootLayout() {
  const connectionStatus = useConnectionStatus();
  return (
    <>
      {connectionStatus === "disconnected" && <DisconnectedBanner />}
      <NavigationGuard />
      <Outlet />
    </>
  );
}

function createAppRouter() {
  return createBrowserRouter(
    createRoutesFromElements(
      <Route element={<RootLayout />}>
        {/* Unauthenticated auth flows render outside the app shell - there's no signed-in
            identity yet for the shell's nav/patient-context regions to reflect. */}
        <Route path="/login" element={<LoginPage />} />
        <Route path="/mfa-challenge" element={<MfaChallengePage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />

        {/* ALV-N009: every route behind the shell requires a real session - RequireAuth denies
            (redirecting to /login) before AppShell or any child route even mounts, so a
            direct/deep-link URL can never flash protected content or start a protected data
            fetch first. Permission-specific routes additionally require RequirePermission;
            the server remains the actual authority for both checks (see RouteGuards.tsx). */}
        <Route
          element={
            <RequireAuth>
              <AppShell />
            </RequireAuth>
          }
        >
          <Route path="/" element={<DashboardPage />} />
          <Route path="/showcase" element={<ShowcasePage />} />
          <Route path="/system-status" element={<SystemStatusPage />} />
          <Route
            path="/admin/users"
            element={
              <RequirePermission permission="ManageUsers">
                <AdminUsersPage />
              </RequirePermission>
            }
          />
          <Route
            path="/admin/users/:userId"
            element={
              <RequirePermission permission="ManageUsers">
                <AdminUserDetailPage />
              </RequirePermission>
            }
          />
          <Route
            path="/admin/permissions"
            element={
              <RequirePermission permission="ViewPermissionMatrix">
                <PermissionMatrixPage />
              </RequirePermission>
            }
          />
          <Route
            path="/admin/audit-log"
            element={
              <RequirePermission permission="ViewAuditLog">
                <AuditLogPage />
              </RequirePermission>
            }
          />
          <Route
            path="/admin/configuration"
            element={
              <RequirePermission permission="ManagePracticeConfiguration">
                <ConfigurationHubPage />
              </RequirePermission>
            }
          />
          <Route
            path="/admin/patient-registration"
            element={
              <RequirePermission permission="ManagePracticeConfiguration">
                <PatientRegistrationSettingsPage />
              </RequirePermission>
            }
          />
          <Route
            path="/admin/form-templates"
            element={
              <RequirePermission permission="ManageFormTemplates">
                <FormTemplatesPage />
              </RequirePermission>
            }
          />
          <Route
            path="/admin/backup"
            element={
              <RequirePermission permission="ViewBackupStatus">
                <BackupRecoveryPage />
              </RequirePermission>
            }
          />
          <Route
            path="/procedures"
            element={
              <RequirePermission permission="ViewBilling">
                <ProcedureCatalogPage />
              </RequirePermission>
            }
          />
          <Route
            path="/calendar"
            element={
              <RequirePermission permission="ViewSchedule">
                <CalendarPage />
              </RequirePermission>
            }
          />
          <Route
            path="/flow"
            element={
              <RequirePermission permission="ViewSchedule">
                <FlowBoardPage />
              </RequirePermission>
            }
          />
          <Route
            path="/schedule"
            element={
              <RequirePermission permission="ViewSchedule">
                <SchedulePage />
              </RequirePermission>
            }
          />
          <Route
            path="/patients/register"
            element={
              <RequirePermission permission="RegisterPatients">
                <PatientRegistrationPage />
              </RequirePermission>
            }
          />
          <Route
            path="/patients"
            element={
              <RequirePermission permission="ViewPatientRecords">
                <PatientSearchPage />
              </RequirePermission>
            }
          />
          <Route
            path="/patients/:patientId"
            element={
              <RequirePermission permission="ViewPatientRecords">
                <PatientWorkspacePage />
              </RequirePermission>
            }
          >
            <Route index element={<PatientDetailsTab />} />
            <Route path="household" element={<PatientHouseholdTab />} />
            <Route path="history" element={<PatientHistoryTab />} />
            <Route path="forms" element={<RequirePermission permission="ViewSignedForms"><PatientFormsTab /></RequirePermission>} />
            <Route path="forms/:formId" element={<RequirePermission permission="ViewSignedForms"><PatientFormTab /></RequirePermission>} />
            <Route path="clinical" element={<RequirePermission permission="ViewClinicalDocumentation"><PatientClinicalTab /></RequirePermission>} />
            <Route path="safety" element={<RequirePermission permission="ViewClinicalDocumentation"><PatientSafetyTab /></RequirePermission>} />
            <Route path="odontogram" element={<RequirePermission permission="ViewClinicalDocumentation"><PatientOdontogramTab /></RequirePermission>} />
            <Route path="periodontal" element={<RequirePermission permission="ViewClinicalDocumentation"><PatientPerioTab /></RequirePermission>} />
            <Route path="diagnoses" element={<RequirePermission permission="ViewClinicalDocumentation"><PatientDiagnosesTab /></RequirePermission>} />
            <Route path="clinical/templates" element={<RequirePermission permission="ViewClinicalDocumentation"><PatientClinicalTemplatesTab /></RequirePermission>} />
            <Route path="clinical/:encounterId" element={<RequirePermission permission="ViewClinicalDocumentation"><PatientEncounterTab /></RequirePermission>} />
          </Route>
          <Route path="/settings/mfa" element={<MfaSettingsPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    )
  );
}

export function App() {
  // Created once per mount (not at module load) so it reads the location the app actually starts at.
  const [router] = useState(createAppRouter);

  return (
    <NotificationProvider>
      <AuthProvider>
        <UnsavedChangesProvider>
          <RouterProvider router={router} />
        </UnsavedChangesProvider>
      </AuthProvider>
    </NotificationProvider>
  );
}
