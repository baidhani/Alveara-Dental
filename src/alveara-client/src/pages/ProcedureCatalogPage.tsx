import { useCallback, useEffect, useState } from "react";
import { Button } from "../components/Button";
import { ConcurrencyConflictBanner } from "../components/ConcurrencyConflictBanner";
import { PageHeader } from "../components/PageHeader";
import { PermissionDenied } from "../components/PermissionDenied";
import { EmptyState, ErrorState, LoadingState } from "../components/StatePatterns";
import { useAuth } from "../contexts/AuthContext";
import { ApiError, isConcurrencyConflict } from "../services/authApi";
import type { ConcurrencyConflictProblem } from "../services/authApi";
import { clinicalFieldErrorsOf, isNetworkFailure } from "../services/clinicalApi";
import { CATEGORIES, CODE_SYSTEMS, createProcedure, listProcedures } from "../services/proceduresApi";
import type { ProcedureDetail, ProcedureFilters, ProcedureSummary } from "../services/proceduresApi";
import type { SaveResult } from "./safety/SafetyForms";
import { ProcedureForm } from "./procedures/ProcedureForm";
import { ProcedureRow } from "./procedures/ProcedureRow";
import { CATEGORY_TEXT, CODE_SYSTEM_TEXT, emptyInput } from "./procedures/procedureText";
import "./clinical/Clinical.css";
import "./odontogram/Odontogram.css";
import "./ProcedureCatalogPage.css";

type Saving = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };
type Load = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; rows: ProcedureSummary[] };

/**
 * ALV-N005: the procedure and fee catalog. Anyone who can see billing can read it; billing, the practice manager and administrators can add a procedure, change it or its fee (a change is
 * a new version, so the old fee stays readable), inactivate it (with a reason, and a warning when other records still refer to it) and reactivate it. Every change is sent as it is made
 * and the list is replaced by what the server holds; the status line says in words whether the last change was saved or not. A stale change shows the shared conflict banner and a reload,
 * so an open form keeps what was typed. The server decides who may do what; the permission here only decides which controls are drawn.
 */
export function ProcedureCatalogPage() {
  const { hasPermission } = useAuth();
  const canView = hasPermission("ViewBilling");
  const canManage = hasPermission("ManageBilling");
  const [filters, setFilters] = useState<ProcedureFilters>({ status: "all" });
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [adding, setAdding] = useState(false);
  const [saving, setSaving] = useState<Saving>({ kind: "idle" });
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const busy = saving.kind === "saving";

  const reload = useCallback(async (signal?: AbortSignal) => {
    try {
      const rows = await listProcedures(filters, signal);
      setLoad({ kind: "loaded", rows });
      setConflict(null);
    } catch {
      if (!signal?.aborted) setLoad((cur) => (cur.kind === "loaded" ? cur : { kind: "error" }));
    }
  }, [filters]);

  useEffect(() => {
    if (!canView) return;
    const controller = new AbortController();
    void reload(controller.signal);
    return () => controller.abort();
  }, [canView, reload]);

  async function run(change: () => Promise<ProcedureDetail>, savedText: string): Promise<SaveResult> {
    setSaving({ kind: "saving" });
    try {
      await change();
      await reload();
      setSaving({ kind: "saved", text: savedText });
      return null;
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict({ ...err.body, entityType: "procedure" });
        setSaving({ kind: "failed", text: "Not saved: someone else changed this." });
      } else if (isNetworkFailure(err)) {
        setSaving({ kind: "failed", text: "Not saved: the connection dropped. What you typed is still here; try again." });
      } else {
        setSaving({ kind: "failed", text: `Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}` });
      }
      return clinicalFieldErrorsOf(err);
    }
  }

  if (!canView) return <><PageHeader title="Procedures and fees" /><PermissionDenied requiredPermission="ViewBilling" /></>;
  const rows = load.kind === "loaded" ? load.rows : [];
  const setFilter = (patch: Partial<ProcedureFilters>) => setFilters((f) => ({ ...f, ...patch }));

  return (
    <>
      <PageHeader title="Procedures and fees" description="The practice's procedure codes and fees. A fee change is kept as a new version, so earlier fees stay readable." />
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}
      <div className="alv-procedures__filters" role="search" aria-label="Find a procedure">
        <label>Search<input type="search" value={filters.search ?? ""} onChange={(e) => setFilter({ search: e.target.value })} placeholder="Code or description" /></label>
        <label>Category
          <select value={filters.category ?? ""} onChange={(e) => setFilter({ category: e.target.value })}>
            <option value="">All categories</option>
            {CATEGORIES.map((c) => <option key={c} value={c}>{CATEGORY_TEXT[c]}</option>)}
          </select>
        </label>
        <label>Code system
          <select value={filters.codeSystem ?? ""} onChange={(e) => setFilter({ codeSystem: e.target.value })}>
            <option value="">All code systems</option>
            {CODE_SYSTEMS.map((c) => <option key={c} value={c}>{CODE_SYSTEM_TEXT[c]}</option>)}
          </select>
        </label>
        <label>Show
          <select value={filters.status ?? "all"} onChange={(e) => setFilter({ status: e.target.value as ProcedureFilters["status"] })}>
            <option value="all">Active and inactive</option>
            <option value="active">Active only</option>
            <option value="inactive">Inactive only</option>
          </select>
        </label>
      </div>
      <p className="alv-clinical__status" role="status" aria-live="polite">
        {saving.kind === "saving" ? "Saving…" : saving.kind === "idle" ? (canManage ? "Every change is saved as you make it." : "You can read the catalog but your role cannot change it.") : saving.text}
      </p>
      {load.kind === "loading" && <LoadingState label="Loading procedures…" />}
      {load.kind === "error" && <ErrorState title="Could not load the procedures" action={<Button onClick={() => void reload()}>Retry</Button>} />}
      {load.kind === "loaded" && rows.length === 0 && <EmptyState title="No procedures found" description="Nothing matches. Clear the filters, or add a procedure if the catalog is new." />}
      {rows.length > 0 && <ul className="alv-clinical__entries" aria-label="Procedures">{rows.map((p) => <ProcedureRow key={p.id} p={p} canManage={canManage} busy={busy} run={run} />)}</ul>}
      {canManage && (adding
        ? <ProcedureForm initial={emptyInput()} busy={busy} submitLabel="Add procedure" onCancel={() => setAdding(false)}
            onSubmit={async (input) => { const refused = await run(() => createProcedure(input), `${input.code.trim()} added to the catalog.`); if (!refused) setAdding(false); return refused; }} />
        : <div className="alv-clinical__section-actions"><Button type="button" onClick={() => setAdding(true)} disabled={busy}>Add a procedure</Button></div>)}
    </>
  );
}
