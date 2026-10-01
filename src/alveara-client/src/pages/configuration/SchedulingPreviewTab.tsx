import { useCallback, useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { PermissionDenied } from "../../components/PermissionDenied";
import { ApiError } from "../../services/authApi";
import { getSchedulingSnapshot } from "../../services/configApi";
import type { SchedulingSnapshot } from "../../services/configApi";
import { DAY_NAMES } from "./availabilityRules";

type State = { kind: "loading" } | { kind: "denied" } | { kind: "error" } | { kind: "loaded"; snapshot: SchedulingSnapshot };

/**
 * Previews exactly what the scheduling module will be offered (active configuration only), read
 * from the same read model scheduling itself consumes - so what an administrator sees here is what
 * a front-desk user will be able to book.
 */
export function SchedulingPreviewTab() {
  const [state, setState] = useState<State>({ kind: "loading" });

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      setState({ kind: "loaded", snapshot: await getSchedulingSnapshot() });
    } catch (err) {
      setState(err instanceof ApiError && err.status === 403 ? { kind: "denied" } : { kind: "error" });
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  if (state.kind === "loading") return <LoadingState label="Loading scheduling preview…" />;
  if (state.kind === "denied") return <PermissionDenied requiredPermission="ViewSchedule" />;
  if (state.kind === "error") return <ErrorState title="Could not load the scheduling preview" action={<Button onClick={load}>Retry</Button>} />;

  const { snapshot } = state;
  const nothing = snapshot.providers.length === 0 && snapshot.operatories.length === 0 && snapshot.appointmentTypes.length === 0;
  if (nothing) {
    return <EmptyState title="Nothing is schedulable yet" description="Add a location, operatory, appointment type, and provider with weekly hours to see what scheduling will offer." />;
  }

  return (
    <section className="alv-config-panel" aria-label="Scheduling preview">
      <p className="alv-config-panel__note">
        Active location: {snapshot.activeLocationName ?? "none"} - times in {snapshot.timeZoneId}.
      </p>

      <h2 className="alv-config-panel__form-title">Providers</h2>
      {snapshot.providers.length === 0 && <p className="alv-config-panel__note">No active providers.</p>}
      <ul>
        {snapshot.providers.map((p) => (
          <li key={p.providerId}>
            <strong>{p.displayName}</strong> ({p.specialty}):{" "}
            {p.weeklyAvailability.length === 0
              ? "no weekly hours set - not bookable yet"
              : p.weeklyAvailability.map((w) => `${DAY_NAMES[w.dayOfWeek]} ${w.startLocal}-${w.endLocal}`).join(", ")}
          </li>
        ))}
      </ul>

      <h2 className="alv-config-panel__form-title">Operatories</h2>
      {snapshot.operatories.length === 0 && <p className="alv-config-panel__note">No active operatories.</p>}
      <ul>
        {snapshot.operatories.map((o) => (
          <li key={o.id}>{o.name}</li>
        ))}
      </ul>

      <h2 className="alv-config-panel__form-title">Appointment types</h2>
      {snapshot.appointmentTypes.length === 0 && <p className="alv-config-panel__note">No active appointment types.</p>}
      <ul>
        {snapshot.appointmentTypes.map((t) => (
          <li key={t.id}>
            {t.name} - {t.defaultDurationMinutes} min
          </li>
        ))}
      </ul>
    </section>
  );
}
