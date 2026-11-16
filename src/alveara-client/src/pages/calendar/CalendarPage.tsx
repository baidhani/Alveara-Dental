import { useEffect, useId, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "../../components/Button";
import { PageHeader } from "../../components/PageHeader";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { getSchedulingSnapshot } from "../../services/configApi";
import type { SchedulingSnapshot } from "../../services/configApi";
import { dateOf, listCalendar, listCalendarBlocks, timeOf } from "../../services/schedulingApi";
import type { CalendarBlock } from "../../services/schedulingApi";
import type { Appointment } from "../../services/schedulingApi";
import { AppointmentPanel } from "./AppointmentPanel";
import { CalendarGrid } from "./CalendarGrid";
import type { CalendarView } from "./CalendarGrid";
import { NewAppointmentPanel } from "./NewAppointmentPanel";
import type { Rebook } from "./NewAppointmentPanel";
import { addDays, dayLabel, hhmm, viewDays } from "./calendarLayout";
import { sessionNow, sessionToday } from "../../time/practiceTime";
import "../scheduling/SchedulePage.css";
import "./Calendar.css";

type Drawer = { kind: "none" } | { kind: "new"; providerId?: string; date: string; time: string; rebook?: Rebook } | { kind: "appointment"; appointment: Appointment };
type Load = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; snapshot: SchedulingSnapshot };

/**
 * ALV-004-C01: the day and week calendar. It shows the practice's real provider and operatory assignments (the appointments come from the server;
 * nothing is computed here that the server did not say), lets staff filter by provider and operatory, and opens a drawer to book, reschedule,
 * cancel, mark a no-show or add a note. Booking and rescheduling are decided by the server (provider, operatory and patient overlap, working hours,
 * blocked time, stale edits); the drawer explains each refusal in words. Reschedule is done from the drawer rather than by dragging: a drag would
 * need the same server validation before it could be trusted, and the drawer already gives that with an explanation.
 */
export function CalendarPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission("ManageAppointments");
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [view, setView] = useState<CalendarView>("day");
  const [day, setDay] = useState("");
  const [providerFilter, setProviderFilter] = useState("");
  const [operatoryFilter, setOperatoryFilter] = useState("");
  const [appointments, setAppointments] = useState<Appointment[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [blocks, setBlocks] = useState<CalendarBlock[]>([]);
  const [blocksFailed, setBlocksFailed] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [drawer, setDrawer] = useState<Drawer>({ kind: "none" });
  const [announcement, setAnnouncement] = useState("");
  const drawerRef = useRef<HTMLDivElement>(null);
  const ids = { provider: useId(), operatory: useId() };
  // ALV-N021 A2 (F-069): /calendar?day=yyyy-MM-dd&appointment=<id> opens that day with that appointment's drawer (from the Schedule list); read once
  const [params] = useSearchParams();
  const linked = useRef({ day: /^\d{4}-\d{2}-\d{2}$/.test(params.get("day") ?? "") ? params.get("day")! : "", appointment: params.get("appointment") ?? "" });

  useEffect(() => {
    const controller = new AbortController();
    getSchedulingSnapshot()
      .then((snapshot) => {
        if (controller.signal.aborted) return;
        setDay((d) => d || linked.current.day || (sessionToday() ?? ""));   // ALV-N021 A3-R02: the session practice day, never the snapshot's zone
        setLoad({ kind: "loaded", snapshot });
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoad({ kind: "error" });
      });
    return () => controller.abort();
  }, []);

  const days = day ? viewDays(day, view) : [];
  const from = days[0];
  const to = days[days.length - 1];
  useEffect(() => {
    if (!from || !to) return;
    const controller = new AbortController();
    listCalendar(from, to, providerFilter || undefined, operatoryFilter || undefined, controller.signal)
      .then((rows) => {
        setFailed(false);
        setAppointments(rows);
        const wanted = linked.current.appointment && rows.find((a) => a.id === linked.current.appointment);
        if (wanted) { linked.current.appointment = ""; setDrawer({ kind: "appointment", appointment: wanted }); }
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [from, to, providerFilter, operatoryFilter, refreshKey]);
  useEffect(() => {
    // ALV-N021 A2 (F-052): blocked time is drawn when it can be read; when it cannot, the calendar says so and still works (the server refuses a booking over it anyway)
    if (!from || !to) return;
    const controller = new AbortController();
    listCalendarBlocks(from, to, providerFilter || undefined, controller.signal)
      .then((rows) => { setBlocks(rows); setBlocksFailed(false); })
      .catch(() => { if (!controller.signal.aborted) { setBlocks([]); setBlocksFailed(true); } });
    return () => controller.abort();
  }, [from, to, providerFilter, refreshKey]);

  const drawerKey = drawer.kind === "appointment" ? drawer.appointment.id : drawer.kind;
  useEffect(() => {
    if (drawerKey !== "none") document.getElementById("cal-drawer-title")?.focus(); // move focus into the drawer when it opens or switches appointment
  }, [drawerKey]);

  const drawerOpen = drawer.kind !== "none";
  useEffect(() => {
    if (!drawerOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setDrawer({ kind: "none" });
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [drawerOpen]);

  if (load.kind === "loading") return <LoadingState label="Loading the calendar…" />;
  if (load.kind === "error") return <ErrorState title="Could not load the calendar" description="Check your connection and reload the page." />;
  const { snapshot } = load;
  // ALV-N021 A3-R02 (CR-1): one zone source - the session's practice zone; without it the page cannot tell today or now, so it says so instead of guessing
  const nowLocal = sessionNow();
  if (nowLocal === null) return <ErrorState title="Times cannot be shown" description="The practice time zone is not available, so this page cannot tell which day it is. Reload the page or sign in again." />;
  const shownProviders = snapshot.providers.filter((p) => !providerFilter || p.providerId === providerFilter);
  const today = dateOf(nowLocal);
  const step = view === "day" ? 1 : 7;

  const closeDrawer = () => setDrawer({ kind: "none" });
  const refresh = () => setRefreshKey((k) => k + 1);
  const replace = (a: Appointment) => {
    setAppointments((list) => (list ?? []).map((x) => (x.id === a.id ? a : x)));
    setDrawer((d) => (d.kind === "appointment" && d.appointment.id === a.id ? { kind: "appointment", appointment: a } : d));
    refresh(); // the moved/cancelled appointment may now belong to another day or column
  };

  return (
    <>
      <PageHeader title="Calendar" description="The day or the week at a glance, by provider and operatory. Select an appointment to see it, move it, cancel it or record a no-show." />
      <div className="cal-toolbar" role="toolbar" aria-label="Calendar controls">
        <div className="cal-toolbar__group" role="group" aria-label="View">
          <Button aria-pressed={view === "day"} variant={view === "day" ? "primary" : "secondary"} onClick={() => setView("day")}>Day</Button>
          <Button aria-pressed={view === "week"} variant={view === "week" ? "primary" : "secondary"} onClick={() => setView("week")}>Week</Button>
        </div>
        <div className="cal-toolbar__group" role="group" aria-label="Move">
          <Button onClick={() => setDay(addDays(day, -step))} aria-label={view === "day" ? "Previous day" : "Previous week"}>←</Button>
          <Button onClick={() => setDay(today)} disabled={days.includes(today) && view === "day"}>Today</Button>
          <Button onClick={() => setDay(addDays(day, step))} aria-label={view === "day" ? "Next day" : "Next week"}>→</Button>
        </div>
        <div className="alv-form-field cal-toolbar__field">
          <label htmlFor="cal-day" className="alv-form-field__label">Date</label>
          <input id="cal-day" type="date" className="alv-form-field__input" value={day} onChange={(e) => e.target.value && setDay(e.target.value)} />
        </div>
        <div className="alv-form-field cal-toolbar__field">
          <label htmlFor={ids.provider} className="alv-form-field__label">Provider</label>
          <select id={ids.provider} className="alv-form-field__input" value={providerFilter} onChange={(e) => setProviderFilter(e.target.value)}>
            <option value="">All providers</option>
            {snapshot.providers.map((p) => <option key={p.providerId} value={p.providerId}>{p.displayName}</option>)}
          </select>
        </div>
        <div className="alv-form-field cal-toolbar__field">
          <label htmlFor={ids.operatory} className="alv-form-field__label">Operatory</label>
          <select id={ids.operatory} className="alv-form-field__input" value={operatoryFilter} onChange={(e) => setOperatoryFilter(e.target.value)}>
            <option value="">All operatories</option>
            {snapshot.operatories.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
          </select>
        </div>
        {canManage && (
          <Button variant="primary" onClick={() => setDrawer({ kind: "new", providerId: providerFilter || undefined, date: day, time: "09:00" })}>New appointment</Button>
        )}
      </div>

      <h2 className="alv-workspace__section-title" aria-live="polite">
        {view === "day" ? dayLabel(day) : `Week of ${days[0]} to ${days[6]}`}
      </h2>
      <p className="cal-legend alv-workspace__note">
        Cancelled appointments have a dashed outline and a struck-through name; no-shows have a dotted outline. Both are labelled and no longer hold the time. Blocked time is a grey band with its label.
        {view === "day" ? " Shaded time is outside the provider's working hours." : " Shaded time is outside every shown provider's working hours."}
      </p>
      <p className="alv-workspace__saved-slot" role="status">{announcement}</p>
      {blocksFailed && <p className="alv-workspace__note" role="alert">Could not load the providers' blocked time; it is not shown. Bookings over it are still refused.</p>}

      {failed ? (
        <ErrorState title="Could not load the appointments" action={<Button onClick={refresh}>Retry</Button>} />
      ) : appointments === null ? (
        <LoadingState label="Loading the appointments…" />
      ) : (
        <CalendarGrid
          view={view}
          days={days}
          appointments={appointments}
          blocks={blocks}
          snapshot={snapshot}
          providers={shownProviders}
          selectedId={drawer.kind === "appointment" ? drawer.appointment.id : null}
          canBook={canManage}
          onOpen={(a) => setDrawer({ kind: "appointment", appointment: a })}
          onSlot={(slotDay, providerId, minute) => setDrawer({ kind: "new", providerId: providerId ?? (providerFilter || undefined), date: slotDay, time: hhmm(minute) })}
          onGoToDay={setDay}
        />
      )}

      {drawer.kind !== "none" && (
        <div
          ref={drawerRef}
          className="cal-drawer"
          role="dialog"
          aria-labelledby="cal-drawer-title"
        >
          <div className="cal-drawer__bar">
            <span className="cal-drawer__kicker">{drawer.kind === "new" ? "New appointment" : "Appointment"}</span>
            <Button onClick={closeDrawer} aria-label="Close drawer">Close</Button>
          </div>
          {drawer.kind === "new" && (
            <>
              <h2 id="cal-drawer-title" tabIndex={-1} className="alv-workspace__section-title">{drawer.rebook ? `Rebook ${drawer.rebook.patientName}` : "Book an appointment"}</h2>
              <NewAppointmentPanel
                key={`${drawer.date}-${drawer.time}-${drawer.providerId ?? ""}-${drawer.rebook?.patientId ?? ""}`}
                snapshot={snapshot}
                initial={{ providerId: drawer.providerId, date: drawer.date, time: drawer.time, rebook: drawer.rebook }}
                onBooked={(a) => {
                  setAnnouncement(`Appointment booked: ${a.patientName} with ${a.providerName}, ${dateOf(a.startLocal)} ${timeOf(a.startLocal)}–${timeOf(a.endLocal)}.`);
                  setDay(dateOf(a.startLocal));
                  setDrawer({ kind: "appointment", appointment: a });
                  refresh();
                }}
              />
            </>
          )}
          {drawer.kind === "appointment" && (
            <AppointmentPanel
              key={drawer.appointment.id}
              appointment={drawer.appointment}
              snapshot={snapshot}
              canManage={canManage}
              nowLocal={nowLocal}
              onChanged={replace}
              onRebook={(a) => {
                // the same provider and time of day, on the appointment's own day or today if that has passed; the booking route decides whether it is free
                const date = dateOf(a.startLocal) >= today ? dateOf(a.startLocal) : today;
                setDrawer({ kind: "new", providerId: a.providerId, date, time: timeOf(a.startLocal), rebook: { patientId: a.patientId, patientName: a.patientName, appointmentTypeId: a.appointmentTypeId, operatoryId: a.operatoryId } });
              }}
            />
          )}
        </div>
      )}
    </>
  );
}
