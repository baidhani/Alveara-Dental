import { useEffect, useState } from "react";
import { Navigate, NavLink, useParams, useSearchParams } from "react-router-dom";
import { EmptyState } from "../../components/StatePatterns";
import { PageHeader } from "../../components/PageHeader";
import { useAuth } from "../../contexts/AuthContext";
import { listLocations, listProviders } from "../../services/configApi";
import type { LocationRecord, ProviderRecord } from "../../services/configApi";
import { BalancesView } from "./BalancesView";
import { CancellationsView } from "./CancellationsView";
import { CollectionsView } from "./CollectionsView";
import { CompletedProceduresView } from "./CompletedProceduresView";
import { FollowUpsView } from "./FollowUpsView";
import { PatientFlowView } from "./PatientFlowView";
import { ProductionView } from "./ProductionView";
import { ProviderActivityView } from "./ProviderActivityView";
import { ReportFilterBar } from "./ReportFilterBar";
import { ScheduleView } from "./ScheduleView";
import { TreatmentPlansView } from "./TreatmentPlansView";
import { GROUPS, REPORTS } from "./reportCatalog";
import { queryFromSearch, queryKey, searchFromQuery } from "./reportFormat";
import type { ReportQuery } from "../../services/reportsApi";
import "./reports.css";

const VIEWS: Record<string, (props: { query: ReportQuery }) => React.ReactNode> = {
  schedule: (p) => <ScheduleView {...p} />,
  "patient-flow": (p) => <PatientFlowView {...p} />,
  "follow-ups": (p) => <FollowUpsView {...p} />,
  cancellations: (p) => <CancellationsView {...p} />,
  production: (p) => <ProductionView {...p} />,
  collections: (p) => <CollectionsView {...p} />,
  balances: (p) => <BalancesView {...p} />,
  "treatment-plans": (p) => <TreatmentPlansView {...p} />,
  "completed-procedures": (p) => <CompletedProceduresView {...p} />,
  "provider-activity": (p) => <ProviderActivityView {...p} />,
};

/**
 * The reporting hub: one place for every report a person may see, grouped by the kind of information (each group needs its own permission, so the front desk never sees financial reports and billing never sees
 * clinical ones). The date range and filters live in the address, so a report can be refreshed or shared exactly as it was asked for. The person chooses a report on the left, sets the filter and presses "Show report";
 * every number in a report can be opened to list the records behind it. The server checks the permission again on every call.
 */
export function ReportsHubPage() {
  const { hasPermission } = useAuth();
  const { reportId } = useParams();
  const [search, setSearch] = useSearchParams();
  const [providers, setProviders] = useState<ProviderRecord[] | null | undefined>(undefined);
  const [locations, setLocations] = useState<LocationRecord[] | null>(null);
  const available = REPORTS.filter((r) => hasPermission(r.permission));
  const current = available.find((r) => r.id === reportId) ?? null;
  const asked = queryFromSearch(search);
  // A filter the report cannot honestly apply is dropped before the report is asked for (the server would refuse it), though it stays in the address for the reports that can use it.
  const query: ReportQuery = { ...asked, providerIds: current?.providerFilter ? asked.providerIds : [], locationId: current?.locationFilter ? asked.locationId : null };

  useEffect(() => {
    const controller = new AbortController();
    listProviders(false).then((p) => { if (!controller.signal.aborted) setProviders(p); }).catch(() => { if (!controller.signal.aborted) setProviders(null); });
    listLocations(false).then((l) => { if (!controller.signal.aborted) setLocations(l); }).catch(() => { if (!controller.signal.aborted) setLocations(null); });
    return () => controller.abort();
  }, []);

  if (available.length === 0) return <EmptyState title="No reports are available to you" description="Reports are shown to the roles that may see them. Ask an administrator if you need one." />;
  if (!reportId) return <Navigate to={`/reports/${available[0].id}?${search.toString() || searchFromQuery(query).toString()}`} replace />;

  return (
    <div className="alv-rep">
      <PageHeader title="Reports" description={current?.description ?? "Choose a report from the list."} />
      <div className="alv-rep__layout">
        <nav className="alv-rep__nav" aria-label="Reports">
          {GROUPS.map(({ group }) => {
            const inGroup = available.filter((r) => r.group === group);
            return inGroup.length === 0 ? null : (
              <section key={group}>
                <h2>{group}</h2>
                <ul>{inGroup.map((r) => <li key={r.id}><NavLink to={`/reports/${r.id}?${search.toString()}`} aria-current={r.id === reportId ? "page" : undefined}>{r.label}</NavLink></li>)}</ul>
              </section>
            );
          })}
        </nav>
        <main className="alv-rep__main">
          {current === null ? (
            <EmptyState title="That report is not available to you" description="Choose one from the list." />
          ) : (
            <>
              <h2 className="alv-rep__title">{current.label}</h2>
              <ReportFilterBar key={queryKey(query) + current.id} report={current} query={query} providers={providers} locations={locations} onApply={(q) => setSearch(searchFromQuery(q))} />
              <div key={current.id + queryKey(query)}>{VIEWS[current.id]({ query })}</div>
            </>
          )}
        </main>
      </div>
    </div>
  );
}
