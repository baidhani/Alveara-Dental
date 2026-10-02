import { useEffect, useId, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { PageHeader } from "../../components/PageHeader";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { getSchedulingSnapshot } from "../../services/configApi";
import type { SchedulingSnapshot } from "../../services/configApi";
import { dateOf, listCalendar, timeOf } from "../../services/schedulingApi";
import type { Appointment } from "../../services/schedulingApi";
import { AppointmentPanel } from "./AppointmentPanel";
import { CalendarGrid } from "./CalendarGrid";
import type { CalendarView } from "./CalendarGrid";
import { NewAppointmentPanel } from "./NewAppointmentPanel";
import { addDays, dayLabel, hhmm, practiceNow, viewDays } from "./calendarLayout";
import "../scheduling/SchedulePage.css";
import "./Calendar.css";

type Drawer = { kind: "none" } | { kind: "new"; providerId?: string; date: string; time: string } | { kind: "appointment"; appointment: Appointment };
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
  const [refreshKey, setRefreshKey] = useState(0);
  const [drawer, setDrawer] = useState<Drawer>({ kind: "none" });
  const [announcement, setAnnouncement] = useState("");
  const drawerRef = useRef<HTMLDivElement>(null);
  const ids = { provider: useId(), operatory: useId() };

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
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [from, to, providerFilter, operatoryFilter, refreshKey]);

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
  const shownProviders = snapshot.providers.filter((p) => !providerFilter || p.providerId === providerFilter);
  const today = dateOf(practiceNow(snapshot.timeZoneId));
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
        Cancelled appointments have a dashed outline and a struck-through name; no-shows have a dotted outline. Both are labelled and no longer hold the time.
        {view === "day" && " Shaded time is outside the provider's working hours."}
      </p>
      <p className="alv-workspace__saved-slot" role="status">{announcement}</p>

      {failed ? (
        <ErrorState title="Could not load the appointments" action={<Button onClick={refresh}>Retry</Button>} />
      ) : appointments === null ? (
        <LoadingState label="Loading the appointments…" />
      ) : (
        <CalendarGrid
          view={view}
          days={days}
          appointments={appointments}
          snapshot={snapshot}
          providers={shownProviders}
          selectedId={drawer.kind === "appointment" ? drawer.appointment.id : null}
          canBook={canManage}
          onOpen={(a) => setDrawer({ kind: "appointment", appointment: a })}
          onSlot={(slotDay, providerId, minute) => setDrawer({ kind: "new", providerId: providerId ?? (providerFilter || undefined), date: slotDay, time: hhmm(minute) })}
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
              <h2 id="cal-drawer-title" tabIndex={-1} className="alv-workspace__section-title">Book an appointment</h2>
              <NewAppointmentPanel
                key={`${drawer.date}-${drawer.time}-${drawer.providerId ?? ""}`}
                snapshot={snapshot}
                initial={{ providerId: drawer.providerId, date: drawer.date, time: drawer.time }}
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
              nowLocal={practiceNow(snapshot.timeZoneId)}
              onChanged={replace}
            />
          )}
        </div>
      )}
    </>
  );
}
