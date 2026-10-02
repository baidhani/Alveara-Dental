import { useEffect, useId, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { PageHeader } from "../../components/PageHeader";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { getSchedulingSnapshot } from "../../services/configApi";
import type { SchedulingSnapshot } from "../../services/configApi";
import { dateOf } from "../../services/schedulingApi";
import type { Appointment, PatientFlowState } from "../../services/schedulingApi";
import { assignVisit, getVisitBoard, moveVisit } from "../../services/visitsApi";
import type { VisitBoard, VisitCard } from "../../services/visitsApi";
import { addDays, dayLabel, practiceNow } from "../calendar/calendarLayout";
import { VisitCardView } from "./VisitCardView";
import { DONE_LABELS, STATE_LABELS, explainVisitRefusal } from "./visitLabels";
import { boardNowMs } from "./visitTime";
import "./FlowBoard.css";

const POLL_MS = 15_000;
const REQUEST_TIMEOUT_MS = 10_000;
const CLOCK_TICK_MS = 30_000;

type Load = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; snapshot: SchedulingSnapshot };

const clock = (ms: number) => new Date(ms).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });

/**
 * ALV-011-C01: the live visit board - a shared view of where every patient is today and what happens next.
 *
 * - <b>Persisted truth only.</b> Every card comes from the server's stored state. After any change the board takes the server's answer and then re-reads the
 *   whole board; every 15 seconds (and when the tab becomes visible again) it re-reads it too, so what one person does shows for everyone. A read that fails or
 *   takes more than 10 seconds keeps the last good board on screen and says it is out of date, then tries again at the next refresh - it never blanks the board.
 * - <b>No silent overwrites.</b> Each move and each reassignment carries the version the card was read at; if someone else changed the visit first the shared
 *   conflict banner says so and nothing is applied. Reloading shows the current state.
 * - <b>Honest times.</b> Elapsed figures use the server's clock (moved forward by the time since the board was fetched), never the browser's clock itself.
 * - Buttons are offered only for the moves the server says come next AND this person's role may make; the server still decides every time.
 */
export function FlowBoardPage({ pollMs = POLL_MS }: { pollMs?: number }) {
  const { hasPermission } = useAuth();
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [day, setDay] = useState("");
  const [board, setBoard] = useState<VisitBoard | null>(null);
  const [fetchedAt, setFetchedAt] = useState(0);
  const [outOfDate, setOutOfDate] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [clientNow, setClientNow] = useState(() => Date.now());
  const [busyId, setBusyId] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [announcement, setAnnouncement] = useState("");
  const busy = useRef(false);
  const focusAfter = useRef<string | null>(null);
  const dateId = useId();

  useEffect(() => {
    const controller = new AbortController();
    getSchedulingSnapshot()
      .then((snapshot) => {
        if (controller.signal.aborted) return;
        setDay((d) => d || dateOf(practiceNow(snapshot.timeZoneId)));
        setLoad({ kind: "loaded", snapshot });
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoad({ kind: "error" });
      });
    return () => controller.abort();
  }, []);

  // read the board now, then again on a timer and whenever the tab comes back into view
  useEffect(() => {
    if (!day) return;
    let alive = true;
    let latest = 0;
    let inflight: AbortController | null = null;
    const read = () => {
      if (busy.current) return; // never re-read underneath a change that is being made
      inflight?.abort();
      const mine = ++latest;
      const controller = new AbortController();
      inflight = controller;
      const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
      getVisitBoard(day, controller.signal)
        .then((b) => {
          if (!alive || mine !== latest) return;
          setBoard(b);
          setFetchedAt(Date.now());
          setOutOfDate(false);
        })
        .catch(() => {
          if (alive && mine === latest) setOutOfDate(true); // keep the last good board; say it is stale
        })
        .finally(() => clearTimeout(timer));
    };
    read();
    const poll = setInterval(() => { if (!document.hidden) read(); }, pollMs);
    const visible = () => { if (!document.hidden) read(); };
    document.addEventListener("visibilitychange", visible);
    return () => {
      alive = false;
      inflight?.abort();
      clearInterval(poll);
      document.removeEventListener("visibilitychange", visible);
    };
  }, [day, refreshKey, pollMs]);

  useEffect(() => {
    const t = setInterval(() => setClientNow(Date.now()), CLOCK_TICK_MS);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    if (focusAfter.current) {
      document.getElementById(`visit-title-${focusAfter.current}`)?.focus(); // the pressed button is gone or changed: keep keyboard focus on the visit
      focusAfter.current = null;
    }
  });

  if (load.kind === "loading") return <LoadingState label="Loading the visit board…" />;
  if (load.kind === "error") return <ErrorState title="Could not load the visit board" description="Check your connection and reload the page." />;
  const { snapshot } = load;
  const today = dateOf(practiceNow(snapshot.timeZoneId));
  const refresh = () => setRefreshKey((k) => k + 1);
  const canMove = hasPermission("UpdateVisitFlow") || hasPermission("UpdateChairsideFlow");
  const shown = board && board.date === day ? board : null; // never show another day's board under this day's heading

  async function act(card: VisitCard, work: () => Promise<Appointment>, done: string, operatoryId?: string) {
    if (busy.current) return;
    const id = card.appointment.id;
    busy.current = true;
    setBusyId(id);
    setErrors((e) => ({ ...e, [id]: "" }));
    setConflict(null);
    try {
      const updated = await work();
      setBoard((b) => (b ? { ...b, visits: b.visits.map((v) => (v.appointment.id === id ? { ...v, appointment: updated, readiness: null } : v)) } : b));
      setAnnouncement(`${card.appointment.patientName} ${done}.`);
      focusAfter.current = id;
      busy.current = false;
      refresh(); // then take the whole board from the server
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setErrors((e) => ({ ...e, [id]: explainVisitRefusal(err, { operatory: snapshot.operatories.find((o) => o.id === operatoryId)?.name }) }));
    } finally {
      busy.current = false;
      setBusyId(null);
    }
  }

  const onMove = (card: VisitCard, target: PatientFlowState) =>
    void act(card, () => moveVisit(card.appointment.id, target, card.appointment.rowVersion ?? ""), `is now ${DONE_LABELS[target] ?? target}`, card.appointment.visitOperatoryId ?? card.appointment.operatoryId);
  const onAssign = (card: VisitCard, providerId: string, operatoryId: string) =>
    void act(card, () => assignVisit(card.appointment.id, providerId, operatoryId, card.appointment.rowVersion ?? ""), "was moved to the new room or provider", operatoryId);

  const nowMs = shown ? boardNowMs(shown.serverNowUtc, fetchedAt, clientNow) : clientNow;
  const inChain = (v: VisitCard, s: string) => v.appointment.status === "Scheduled" && (v.appointment.flowState ?? "Scheduled") === s;
  const closed = shown?.visits.filter((v) => v.appointment.status !== "Scheduled") ?? [];
  const cardFor = (v: VisitCard) => (
    <li key={v.appointment.id}>
      <VisitCardView card={v} nowMs={nowMs} snapshot={snapshot} can={hasPermission} busy={busyId !== null} error={errors[v.appointment.id] || null} onMove={onMove} onAssign={onAssign} />
    </li>
  );

  return (
    <div className="flow-board">
      <PageHeader title="Visit board" description="Where every patient is today and what happens next. It refreshes on its own, so what one person does shows for everyone." />
      <div className="flow-toolbar" role="toolbar" aria-label="Board controls">
        <div className="flow-toolbar__group" role="group" aria-label="Move">
          <Button onClick={() => setDay(addDays(day, -1))} aria-label="Previous day">←</Button>
          <Button onClick={() => setDay(today)} disabled={day === today}>Today</Button>
          <Button onClick={() => setDay(addDays(day, 1))} aria-label="Next day">→</Button>
        </div>
        <div className="alv-form-field flow-toolbar__field">
          <label htmlFor={dateId} className="alv-form-field__label">Date</label>
          <input id={dateId} type="date" className="alv-form-field__input" value={day} onChange={(e) => e.target.value && setDay(e.target.value)} />
        </div>
        <Button onClick={refresh}>Refresh now</Button>
      </div>

      <h2 className="alv-workspace__section-title" aria-live="polite">{dayLabel(day)}</h2>
      <p className="alv-workspace__note" role="status">
        {shown ? `Updated ${clock(fetchedAt)}.` : "Loading the board…"}
        {outOfDate && <strong className="flow-stale"> Could not refresh just now - this is the board as of {clock(fetchedAt)}. Trying again shortly.</strong>}
      </p>
      {!canMove && <p className="alv-workspace__note">You can see the board but your role cannot change it.</p>}
      <p className="alv-workspace__saved-slot" role="status">{announcement}</p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => { setConflict(null); refresh(); }} />}

      {!shown ? (
        outOfDate ? <ErrorState title="Could not load the board" action={<Button onClick={refresh}>Retry</Button>} /> : <LoadingState label="Loading the visits…" />
      ) : (
        <>
          <div className="flow-columns">
            {shown.states.map((s) => {
              const here = shown.visits.filter((v) => inChain(v, s));
              return (
                <section key={s} className={`flow-column${here.length === 0 ? " flow-column--empty" : ""}`} aria-labelledby={`flow-col-${s}`}>
                  <h3 id={`flow-col-${s}`} className="flow-column__title">{STATE_LABELS[s]} <span className="flow-column__count">({here.length})</span></h3>
                  {here.length === 0 ? <p className="flow-column__empty">No patients</p> : <ul className="flow-list">{here.map(cardFor)}</ul>}
                </section>
              );
            })}
          </div>
          <section className="flow-closed" aria-labelledby="flow-closed-title">
            <h3 id="flow-closed-title" className="flow-column__title">Cancelled and no-show <span className="flow-column__count">({closed.length})</span></h3>
            <p className="alv-workspace__note">These no longer hold the time and are not part of the visit chain.</p>
            {closed.length === 0 ? <p className="flow-column__empty">None today</p> : <ul className="flow-list flow-list--closed">{closed.map(cardFor)}</ul>}
          </section>
        </>
      )}
    </div>
  );
}
