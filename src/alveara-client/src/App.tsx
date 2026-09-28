import { BrowserRouter, Route, Routes } from "react-router-dom";
import { AppShell } from "./app/AppShell";
import { DashboardPage } from "./pages/DashboardPage";
import { ShowcasePage } from "./pages/ShowcasePage";
import { SystemStatusPage } from "./pages/SystemStatusPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { NotificationProvider } from "./components/Notification";

export function App() {
  return (
    <NotificationProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<AppShell />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/showcase" element={<ShowcasePage />} />
            <Route path="/system-status" element={<SystemStatusPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </NotificationProvider>
  );
}
