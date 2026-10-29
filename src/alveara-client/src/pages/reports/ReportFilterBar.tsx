import { useState } from "react";
import { Button } from "../../components/Button";
import type { LocationRecord, ProviderRecord } from "../../services/configApi";
import type { ReportQuery } from "../../services/reportsApi";
import { PRESETS, isDate } from "./reportFormat";
import type { ReportDef } from "./reportCatalog";

interface Props {
  report: ReportDef;
  query: ReportQuery;
  /** undefined while the list is loading, null when it could not be loaded. */
  providers: ProviderRecord[] | null | undefined;
  locations: LocationRecord[] | null;
  onApply: (q: ReportQuery) => void;
}

/**
 * The filter bar. Nothing is asked of the server until "Show report" is pressed, so typing a date never fires a request. The dates are practice-local days. Provider and location choices appear only for reports the
 * server can honestly filter that way; for the others the bar says the report is not filtered, and does not send the filter (the server would refuse it). A range whose first day is after its last is caught here with a message.
 */
export function ReportFilterBar({ report, query, providers, locations, onApply }: Props) {
  const [from, setFrom] = useState(query.from);
  const [to, setTo] = useState(query.to);
  const [providerIds, setProviderIds] = useState<string[]>(query.providerIds);
  const [locationId, setLocationId] = useState(query.locationId ?? "");
  const problem = !isDate(from) || !isDate(to) ? "Choose a first and a last date." : from > to ? "The first date must not be after the last date." : null;

  const toggle = (id: string) => setProviderIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  const apply = () => { if (!problem) onApply({ from, to, providerIds: report.providerFilter ? providerIds : [], locationId: report.locationFilter && locationId ? locationId : null }); };

  return (
    <form className="alv-rep__filters" aria-label="Report filters" onSubmit={(e) => { e.preventDefault(); apply(); }}>
      <div className="alv-rep__dates">
        <label>First day<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>Last day<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <div className="alv-rep__presets" role="group" aria-label="Quick ranges">
          {PRESETS.map((p) => <Button key={p.label} type="button" onClick={() => { const r = p.range(); setFrom(r.from); setTo(r.to); }}>{p.label}</Button>)}
        </div>
      </div>

      {report.providerFilter && (providers === undefined ? <p className="alv-rep__hint">Loading the list of providers…</p> : providers ? (
        <fieldset className="alv-rep__providers">
          <legend>Booked providers (none chosen means all)</legend>
          {providers.length === 0 && <p>No providers are set up.</p>}
          {providers.map((p) => (
            <label key={p.id}><input type="checkbox" checked={providerIds.includes(p.id)} onChange={() => toggle(p.id)} /> {p.displayName ?? "Unnamed provider"}</label>
          ))}
        </fieldset>
      ) : <p className="alv-rep__hint">The list of providers could not be loaded, so this report cannot be narrowed by provider.</p>)}

      {report.locationFilter && locations && locations.length > 1 && (
        <label>Location
          <select value={locationId} onChange={(e) => setLocationId(e.target.value)}>
            <option value="">All locations</option>
            {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        </label>
      )}
      {!report.providerFilter && !report.locationFilter && <p className="alv-rep__hint">This report covers the whole practice. It cannot be narrowed by provider or location.</p>}

      {problem && <p role="alert" className="alv-rep__problem">{problem}</p>}
      <div className="alv-rep__apply"><Button type="submit" variant="primary" disabled={!!problem}>Show report</Button></div>
    </form>
  );
}
