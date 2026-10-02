import type { MouseEvent } from "react";
import type { SchedulingSnapshot } from "../../services/configApi";
import type { Appointment } from "../../services/schedulingApi";
import { timeOf } from "../../services/schedulingApi";
import { PIXELS_PER_MINUTE, appointmentsOnDay, axisRange, dayLabel, dayOfWeek, hhmm, minutesOf, placeColumn, slotMinuteAt, statusWord } from "./calendarLayout";
import type { Placed } from "./calendarLayout";

export type CalendarView = "day" | "week";

interface GridProps {
  view: CalendarView;
  days: string[];
  appointments: Appointment[];
  snapshot: SchedulingSnapshot;
  /** The providers shown as columns in the day view (after filtering). */
  providers: SchedulingSnapshot["providers"];
  selectedId: string | null;
  canBook: boolean;
  onOpen: (appointment: Appointment) => void;
  onSlot: (day: string, providerId: string | null, startMinute: number) => void;
}

const blockLabel = (a: Appointment) =>
  `${timeOf(a.startLocal)} to ${timeOf(a.endLocal)}, ${a.patientName}, ${a.appointmentTypeName}, ${a.providerName}, ${a.operatoryName}, ${statusWord(a.status)}`;

/**
 * ALV-004-C01: the calendar grid. The day view has one column per provider (the practice's real assignments); the week view has one column per day,
 * with overlapping appointments (different providers at the same time) placed side by side. Working hours come from the practice configuration:
 * time outside a provider's weekly hours is drawn as "off hours" in the day view. Cancelled and no-show appointments stay on the grid but are drawn
 * distinctly (dashed or dotted border, struck-through patient name for a cancellation, and the status written in the block).
 */
export function CalendarGrid({ view, days, appointments, snapshot, providers, selectedId, canBook, onOpen, onSlot }: GridProps) {
  const windowsFor = (providerId: string, day: string) =>
    (snapshot.providers.find((p) => p.providerId === providerId)?.weeklyAvailability ?? []).filter((w) => w.dayOfWeek === dayOfWeek(day));
  const allWindows = days.flatMap((d) => (view === "day" ? providers : snapshot.providers).flatMap((p) => windowsFor(p.providerId, d)));
  const { startMinute, endMinute } = axisRange(appointments, allWindows);
  const heightPx = (endMinute - startMinute) * PIXELS_PER_MINUTE;
  const hours = Array.from({ length: (endMinute - startMinute) / 60 }, (_, i) => startMinute + i * 60);

  const columns =
    view === "day"
      ? providers.map((p) => ({ key: p.providerId, title: p.displayName, subtitle: dayLabel(days[0]), day: days[0], providerId: p.providerId as string | null }))
      : days.map((d) => ({ key: d, title: dayLabel(d), subtitle: "", day: d, providerId: null as string | null }));

  if (columns.length === 0) return <p className="alv-workspace__note">No providers to show.</p>;

  return (
    <div className="cal" role="group" aria-label={view === "day" ? `Day calendar for ${dayLabel(days[0])}` : `Week calendar ${days[0]} to ${days[6]}`}>
      <div className="cal__axis" aria-hidden="true">
        <div className="cal__head" />
        <div className="cal__axis-body" style={{ height: heightPx }}>
          {hours.map((m) => <div key={m} className="cal__hour-label" style={{ top: (m - startMinute) * PIXELS_PER_MINUTE }}>{hhmm(m)}</div>)}
        </div>
      </div>
      <div className="cal__columns" style={{ gridTemplateColumns: `repeat(${columns.length}, minmax(${view === "day" ? 150 : 110}px, 1fr))` }}>
        {columns.map((col) => {
          const mine = appointmentsOnDay(appointments, col.day).filter((a) => col.providerId === null || a.providerId === col.providerId);
          const placed = placeColumn(mine, col.day, startMinute);
          const hoursWindows = col.providerId ? windowsFor(col.providerId, col.day) : [];
          const onColumnClick = (event: MouseEvent<HTMLDivElement>) => {
            if (!canBook || event.target !== event.currentTarget) return; // only a click on the empty grid, not on an appointment
            onSlot(col.day, col.providerId, slotMinuteAt(event.nativeEvent.offsetY ?? 0, startMinute));
          };
          return (
            <div key={col.key} className="cal__column">
              <div className="cal__head">
                <strong>{col.title}</strong>
                {col.subtitle && <span className="cal__sub">{col.subtitle}</span>}
              </div>
              <div
                className={`cal__body${col.providerId ? " cal__body--off" : ""}`}
                style={{ height: heightPx }}
                data-column={col.key}
                data-day={col.day}
                onClick={onColumnClick}
                aria-label={`${col.title}${col.subtitle ? ` ${col.subtitle}` : ""}`}
                role="group"
              >
                {hours.map((m) => <div key={m} className="cal__hour-line" style={{ top: (m - startMinute) * PIXELS_PER_MINUTE }} />)}
                {hoursWindows.map((w) => (
                  <div
                    key={`${w.startLocal}-${w.endLocal}`}
                    className="cal__working"
                    data-working={`${w.startLocal}-${w.endLocal}`}
                    style={{ top: (minutesOf(w.startLocal) - startMinute) * PIXELS_PER_MINUTE, height: (minutesOf(w.endLocal) - minutesOf(w.startLocal)) * PIXELS_PER_MINUTE }}
                  />
                ))}
                {placed.map((p) => <Block key={p.appointment.id} placed={p} selected={p.appointment.id === selectedId} onOpen={onOpen} />)}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function Block({ placed, selected, onOpen }: { placed: Placed; selected: boolean; onOpen: (a: Appointment) => void }) {
  const a = placed.appointment;
  const widthPct = 100 / placed.lanes;
  const compact = placed.heightPx < 56; // a short appointment: one line, so the patient and status are never cropped
  const cls = `cal__block cal__block--${a.status.toLowerCase()}${compact ? " cal__block--compact" : ""}${selected ? " cal__block--selected" : ""}`;
  // the status is written in the first line (never a separate row that a short block could crop): "09:00–10:00 · Cancelled"
  const time = `${timeOf(a.startLocal)}–${timeOf(a.endLocal)}${a.status !== "Scheduled" ? ` · ${statusWord(a.status)}` : ""}`;
  return (
    <button
      type="button"
      className={cls}
      style={{ top: placed.topPx, height: placed.heightPx, left: `${placed.lane * widthPct}%`, width: `calc(${widthPct}% - 4px)` }}
      aria-label={blockLabel(a)}
      title={blockLabel(a)}
      aria-pressed={selected}
      data-appointment={a.id}
      data-status={a.status}
      onClick={() => onOpen(a)}
    >
      <span className="cal__block-time">{time}</span>
      <span className="cal__block-patient">{a.patientName}</span>
      <span className="cal__block-meta">{a.appointmentTypeName} · {a.providerName} · {a.operatoryName}</span>
    </button>
  );
}
