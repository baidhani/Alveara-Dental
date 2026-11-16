import type { MouseEvent } from "react";
import { Button } from "../../components/Button";
import type { SchedulingSnapshot } from "../../services/configApi";
import type { Appointment, CalendarBlock } from "../../services/schedulingApi";
import { timeOf } from "../../services/schedulingApi";
import { DAY_NAMES, PIXELS_PER_MINUTE, appointmentsOnDay, axisRange, dayLabel, dayOfWeek, hhmm, flowWord, minutesOf, nextWorkingDay, placeColumn, slotMinuteAt, statusWord } from "./calendarLayout";
import type { Placed } from "./calendarLayout";

export type CalendarView = "day" | "week";

interface GridProps {
  view: CalendarView;
  days: string[];
  appointments: Appointment[];
  /** ALV-N021 A2 (F-052): the providers' blocked time in the shown days, each with its calendar label. */
  blocks?: CalendarBlock[];
  snapshot: SchedulingSnapshot;
  /** The providers shown as columns in the day view (after filtering). */
  providers: SchedulingSnapshot["providers"];
  selectedId: string | null;
  canBook: boolean;
  onOpen: (appointment: Appointment) => void;
  onSlot: (day: string, providerId: string | null, startMinute: number) => void;
  /** ALV-N021 A2 (F-048): go to another day (the "Next working day" shortcut). */
  onGoToDay?: (day: string) => void;
}

const blockLabel = (a: Appointment) =>
  `${timeOf(a.startLocal)} to ${timeOf(a.endLocal)}, ${a.patientName}, ${a.appointmentTypeName}, ${a.providerName}, ${a.operatoryName}, ${flowWord(a) ?? statusWord(a.status)}${blockedWord(a)}`;

/** ALV-N021 A2 (OD-18): blocked time covers it and nobody has resolved it; written, never colour alone. */
const blockedWord = (a: Appointment) => (a.status === "Scheduled" && a.blockFlagged ? ", blocked time" : "");

/**
 * ALV-004-C01: the calendar grid. The day view has one column per provider (the practice's real assignments); the week view has one column per day,
 * with overlapping appointments (different providers at the same time) placed side by side. Working hours come from the practice configuration:
 * time outside a provider's weekly hours is drawn as "off hours" in the day view. Cancelled and no-show appointments stay on the grid but are drawn
 * distinctly (dashed or dotted border, struck-through patient name for a cancellation, and the status written in the block).
 */
export function CalendarGrid({ view, days, appointments, blocks = [], snapshot, providers, selectedId, canBook, onOpen, onSlot, onGoToDay }: GridProps) {
  const windowsFor = (providerId: string, day: string) =>
    (snapshot.providers.find((p) => p.providerId === providerId)?.weeklyAvailability ?? []).filter((w) => w.dayOfWeek === dayOfWeek(day));
  // ALV-N021 A2 (F-052): the week view shades too - a day's column is plain where any shown provider works, hatched elsewhere
  const shownWindows = (day: string) => providers.flatMap((p) => windowsFor(p.providerId, day));
  const allWindows = days.flatMap((d) => shownWindows(d));
  const visible = days.flatMap((d) => appointmentsOnDay(appointments, d));
  const nextDay = view === "day" && allWindows.length === 0 ? nextWorkingDay(days[0], providers) : null;
  const { startMinute, endMinute } = axisRange(appointments, allWindows);
  const heightPx = (endMinute - startMinute) * PIXELS_PER_MINUTE;
  const hours = Array.from({ length: (endMinute - startMinute) / 60 }, (_, i) => startMinute + i * 60);

  const columns =
    view === "day"
      ? providers.map((p) => ({ key: p.providerId, title: p.displayName, subtitle: dayLabel(days[0]), day: days[0], providerId: p.providerId as string | null }))
      // F-077: every week header is the day name over the date, on two lines, whatever the column width
      : days.map((d) => ({ key: d, title: DAY_NAMES[dayOfWeek(d)], subtitle: d, day: d, providerId: null as string | null }));

  if (columns.length === 0) return <p className="alv-workspace__note">No providers to show.</p>;

  return (
    <>
    {view === "day" && allWindows.length === 0 && (
      // F-048: a day nobody works says so, with a way to the next working day
      <div className="cal-notice" role="note">
        <p>No working hours for {providers.length === 1 ? providers[0].displayName : "any provider"} on {dayLabel(days[0])}.</p>
        {nextDay && onGoToDay && <Button onClick={() => onGoToDay(nextDay)}>Next working day ({dayLabel(nextDay)})</Button>}
      </div>
    )}
    {visible.length === 0 && <p className="alv-workspace__note" data-testid="cal-empty">No appointments for this selection.</p>}
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
          const hoursWindows = col.providerId ? windowsFor(col.providerId, col.day) : shownWindows(col.day);
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
                className="cal__body cal__body--off"
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
                {view === "week" && hoursWindows.length === 0 && <p className="cal__closed">No working hours</p>}
                {blocksIn(blocks, col.day, col.providerId === null ? providers.map((p) => p.providerId) : [col.providerId]).map((b) => {
                  const top = Math.max(minutesIn(b.startLocal, col.day), startMinute);
                  const bottom = Math.min(minutesIn(b.endLocal, col.day), endMinute);
                  if (bottom <= top) return null;
                  const text = view === "week" ? `${b.displayLabel} · ${b.providerName}` : b.displayLabel;
                  return (
                    <div key={b.id} className="cal__blocked" data-block={b.id} title={`${text}, ${timeOf(b.startLocal)} to ${timeOf(b.endLocal)}`}
                      style={{ top: (top - startMinute) * PIXELS_PER_MINUTE, height: (bottom - top) * PIXELS_PER_MINUTE }}>
                      <span>{text}</span>
                    </div>
                  );
                })}
                {placed.map((p) => <Block key={p.appointment.id} placed={p} narrow={view === "week" && p.lanes > 1} selected={p.appointment.id === selectedId} onOpen={onOpen} />)}
              </div>
            </div>
          );
        })}
      </div>
    </div>
    </>
  );
}

/** The blocks of these providers that touch `day`. */
const blocksIn = (blocks: CalendarBlock[], day: string, providerIds: string[]) =>
  blocks.filter((b) => providerIds.includes(b.providerId) && b.startLocal.slice(0, 10) <= day && day <= b.endLocal.slice(0, 10) && b.endLocal !== `${day}T00:00`);

/** A local time as minutes from the start of `day`: before the day is 0, after it is 24:00. */
const minutesIn = (local: string, day: string) => (local.slice(0, 10) < day ? 0 : local.slice(0, 10) > day ? 24 * 60 : minutesOf(local));

function Block({ placed, selected, narrow, onOpen }: { placed: Placed; selected: boolean; narrow: boolean; onOpen: (a: Appointment) => void }) {
  const a = placed.appointment;
  const widthPct = 100 / placed.lanes;
  const compact = !narrow && placed.heightPx < 56; // a short appointment: one line, so the patient and status are never cropped
  // ALV-N021 A2 (F-054): side by side in the week view the block is narrow - the start time alone, then the patient's name wrapping; full details in its name and tooltip
  const cls = `cal__block cal__block--${a.status.toLowerCase()}${compact ? " cal__block--compact" : ""}${narrow ? " cal__block--narrow" : ""}${selected ? " cal__block--selected" : ""}`;
  // the status is written in the first line (never a separate row that a short block could crop): "09:00–10:00 · Cancelled"
  const word = a.status !== "Scheduled" ? statusWord(a.status) : flowWord(a); // STORY-011: a visit under way says so too
  const span = narrow ? timeOf(a.startLocal) : `${timeOf(a.startLocal)}–${timeOf(a.endLocal)}`;
  const status = `${word ?? ""}${word && blockedWord(a) ? " · " : ""}${blockedWord(a) ? "Blocked time" : ""}`;
  // narrow: the status on its own line under the start time, so a word like "Cancelled" is never split
  const time = narrow ? span : `${span}${status ? ` · ${status}` : ""}`;
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
      data-flow={a.flowState ?? "Scheduled"}
      onClick={() => onOpen(a)}
    >
      <span className="cal__block-time">{time}</span>
      {narrow && status && <span className="cal__block-status">{status}</span>}
      <span className="cal__block-patient">{a.patientName}</span>
      <span className="cal__block-meta">{a.appointmentTypeName} · {a.providerName} · {a.operatoryName}</span>
    </button>
  );
}
