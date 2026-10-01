import { useCallback, useState } from "react";
import { PageHeader } from "../components/PageHeader";
import { confirmDiscard } from "../hooks/useUnsavedChangesWarning";
import { PracticeTab } from "./configuration/PracticeTab";
import { StaffTab, ProvidersTab } from "./configuration/PeopleTabs";
import { OperatoriesTab, AppointmentTypesTab } from "./configuration/SimpleTabs";
import { AvailabilityTab } from "./configuration/AvailabilityTab";
import { SchedulingPreviewTab } from "./configuration/SchedulingPreviewTab";
import "./ConfigurationHubPage.css";

const TABS = [
  { id: "practice", label: "Practice" },
  { id: "staff", label: "Staff" },
  { id: "providers", label: "Providers" },
  { id: "operatories", label: "Operatories" },
  { id: "appointment-types", label: "Appointment types" },
  { id: "availability", label: "Availability" },
  { id: "preview", label: "Scheduling preview" },
] as const;

type TabId = (typeof TABS)[number]["id"];

/**
 * ALV-N003's Admin Configuration hub. One tab per kind of configuration, each built on the shared
 * configuration pattern (ConfigEntityPanel). The route is gated on ManagePracticeConfiguration and
 * the server enforces the same permission on every call; switching tabs with unsaved edits asks
 * before discarding them.
 */
export function ConfigurationHubPage() {
  const [active, setActive] = useState<TabId>("practice");
  const [dirty, setDirty] = useState(false);
  const onDirtyChange = useCallback((value: boolean) => setDirty(value), []);

  function selectTab(id: TabId) {
    if (id === active) return;
    if (!confirmDiscard(dirty)) return;
    setDirty(false);
    setActive(id);
  }

  return (
    <>
      <PageHeader
        title="Practice configuration"
        description="Set up the practice, its staff and providers, operatories, appointment types, and working hours. Changes are audited; nothing is ever deleted, only inactivated."
      />
      <div role="tablist" aria-label="Configuration sections" className="alv-config-tabs">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`config-tab-${tab.id}`}
            aria-selected={active === tab.id}
            aria-controls="config-tabpanel"
            className={`alv-config-tabs__tab${active === tab.id ? " alv-config-tabs__tab--active" : ""}`}
            onClick={() => selectTab(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>
      <div role="tabpanel" id="config-tabpanel" aria-labelledby={`config-tab-${active}`}>
        {active === "practice" && <PracticeTab onDirtyChange={onDirtyChange} />}
        {active === "staff" && <StaffTab onDirtyChange={onDirtyChange} />}
        {active === "providers" && <ProvidersTab onDirtyChange={onDirtyChange} />}
        {active === "operatories" && <OperatoriesTab onDirtyChange={onDirtyChange} />}
        {active === "appointment-types" && <AppointmentTypesTab onDirtyChange={onDirtyChange} />}
        {active === "availability" && <AvailabilityTab onDirtyChange={onDirtyChange} />}
        {active === "preview" && <SchedulingPreviewTab />}
      </div>
    </>
  );
}
