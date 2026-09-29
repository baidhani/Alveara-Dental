import { BrowserRouter, Route, Routes } from "react-router-dom";
import { AppShell } from "./app/AppShell";
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
import { NotFoundPage } from "./pages/NotFoundPage";
import { NotificationProvider } from "./components/Notification";
import { AuthProvider } from "./contexts/AuthContext";

export function App() {
  return (
    <NotificationProvider>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            {/* Unauthenticated auth flows render outside the app shell - there's no signed-in
                identity yet for the shell's nav/patient-context regions to reflect. */}
            <Route path="/login" element={<LoginPage />} />
            <Route path="/mfa-challenge" element={<MfaChallengePage />} />
            <Route path="/reset-password" element={<ResetPasswordPage />} />

            <Route element={<AppShell />}>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/showcase" element={<ShowcasePage />} />
              <Route path="/system-status" element={<SystemStatusPage />} />
              <Route path="/admin/users" element={<AdminUsersPage />} />
              <Route path="/admin/users/:userId" element={<AdminUserDetailPage />} />
              <Route path="/admin/permissions" element={<PermissionMatrixPage />} />
              <Route path="/settings/mfa" element={<MfaSettingsPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </NotificationProvider>
  );
}
