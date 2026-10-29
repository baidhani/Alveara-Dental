import { useState } from "react";
import { getProviderActivity } from "../../services/reportsApi";
import type { AppointmentDrill, ProcedureDrill, ProviderActivityRow, ReportQuery } from "../../services/reportsApi";
import { ProcedureRecords } from "./MoreRecords";
import { NumberButton } from "./NumberButton";
import { AppointmentRecords } from "./RecordsPanel";
import { ReportFrame } from "./ReportFrame";
import { queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { kind: "appointments"; title: string; drill: AppointmentDrill } | { kind: "procedures"; title: string; drill: ProcedureDrill };
type Counts = Omit<ProviderActivityRow, "providerProfileId" | "providerName">;

/**
 * The provider-activity report: for each provider, three DIFFERENT measures. Booked is the appointments booked with the provider (scheduled, cancelled and no-show add up to it). Visits finished is credited to the
 * provider who actually saw the patient, which can differ from the one booked; the visit booked with one and seen by another is in the first one's Booked and the second one's Visits finished. Procedures done is the
 * procedures now credited to the provider. Each column adds up to its total (the report says so), and every count lists its records.
 */
export function ProviderActivityView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getProviderActivity(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);
  const appointments = (title: string, measure: string, providerId?: string) => setSpec({ kind: "appointments", title, drill: { route: "provider-activity/appointments", measure, providerId } });
  const procedures = (title: string, providerId?: string) => setSpec({ kind: "procedures", title, drill: { route: "provider-activity/procedures", providerId } });

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No appointment starts and no procedure was performed in this period for the chosen filters.">
      {(report) => {
        const d = report.data;
        const row = (name: string, c: Counts, providerId?: string) => (
          <tr key={providerId ?? "all"}>
            <th scope="row">{name}</th>
            <td><NumberButton value={c.booked} label={`${name}, booked`} onOpen={() => appointments(`${name}: booked`, "Booked", providerId)} /></td>
            <td><NumberButton value={c.scheduled} label={`${name}, scheduled`} onOpen={() => appointments(`${name}: scheduled`, "Scheduled", providerId)} /></td>
            <td><NumberButton value={c.cancelled} label={`${name}, cancelled`} onOpen={() => appointments(`${name}: cancelled`, "Cancelled", providerId)} /></td>
            <td><NumberButton value={c.noShow} label={`${name}, no-shows`} onOpen={() => appointments(`${name}: no-shows`, "NoShow", providerId)} /></td>
            <td><NumberButton value={c.visitsCompleted} label={`${name}, visits finished`} onOpen={() => appointments(`${name}: visits finished`, "VisitsCompleted", providerId)} /></td>
            <td><NumberButton value={c.proceduresCompleted} label={`${name}, procedures done`} onOpen={() => procedures(`${name}: procedures done`, providerId)} /></td>
          </tr>
        );
        return (
          <>
            <p className={d.reconciled ? "alv-rep__hint" : "alv-rep__problem"} role={d.reconciled ? undefined : "alert"}>
              {d.reconciled ? "Every column adds up to its total." : "A column does not add up to its total. Do not rely on these numbers; tell your administrator."}
            </p>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Activity of each provider</caption>
              <thead><tr><th scope="col">Provider</th><th scope="col">Booked</th><th scope="col">Scheduled</th><th scope="col">Cancelled</th><th scope="col">No-shows</th><th scope="col">Visits finished</th><th scope="col">Procedures done</th></tr></thead>
              <tbody>{d.providers.map((p) => row(p.providerName, p, p.providerProfileId))}{row("All providers", d.totals)}</tbody>
            </table>
            <p className="alv-rep__hint">A visit finished is credited to the provider who saw the patient, which may not be the one it was booked with.</p>
            {spec?.kind === "appointments" && <AppointmentRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
            {spec?.kind === "procedures" && <ProcedureRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
