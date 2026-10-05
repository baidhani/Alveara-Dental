/**
 * ALV-012-C01: the pure state of step-by-step entry. Entries made on screen wait in a BUFFER until they are saved into the draft (when the cursor leaves a tooth, or on demand); the screen shows the
 * draft as the server holds it overlaid with the buffer, so what was typed is never hidden and never lost to a refused or failed save. Nothing here touches the network or the page.
 */
import type { PerioEntryBatch, PerioReading, PerioReadingInput, PerioTooth, PerioToothInput } from "../../services/perioApi";
import { SITES, cellId } from "./perioChartRules";
import { sweep } from "./perioSiteModel";
import type { SiteRef } from "./perioSiteModel";

export interface EntryBuffer {
  readings: Record<string, PerioReadingInput>;
  teeth: Record<string, PerioToothInput>;
  clearSites: string[];
  clearTeeth: string[];
}

export const emptyBuffer = (): EntryBuffer => ({ readings: {}, teeth: {}, clearSites: [], clearTeeth: [] });
export const isBufferEmpty = (b: EntryBuffer) => Object.keys(b.readings).length + Object.keys(b.teeth).length + b.clearSites.length + b.clearTeeth.length === 0;
export const bufferedSites = (b: EntryBuffer) => Object.keys(b.readings).length + b.clearSites.length;

const without = (list: string[], key: string) => list.filter((k) => k !== key);

/** Sets a site's reading; a reading wins over an earlier request to clear that site. */
export function withReading(b: EntryBuffer, r: PerioReadingInput): EntryBuffer {
  const key = cellId(r.toothKey, r.site);
  return { ...b, readings: { ...b.readings, [key]: r }, clearSites: without(b.clearSites, key) };
}

/** Clears a site, whether it was entered earlier or only in the buffer. */
export function withSiteCleared(b: EntryBuffer, toothKey: string, site: string): EntryBuffer {
  const key = cellId(toothKey, site);
  const { [key]: _gone, ...readings } = b.readings;
  return { ...b, readings, clearSites: b.clearSites.includes(key) ? b.clearSites : [...b.clearSites, key] };
}

export function withTooth(b: EntryBuffer, t: PerioToothInput): EntryBuffer {
  return { ...b, teeth: { ...b.teeth, [t.toothKey]: t }, clearTeeth: without(b.clearTeeth, t.toothKey) };
}

export function withToothCleared(b: EntryBuffer, toothKey: string): EntryBuffer {
  const { [toothKey]: _gone, ...teeth } = b.teeth;
  return { ...b, teeth, clearTeeth: b.clearTeeth.includes(toothKey) ? b.clearTeeth : [...b.clearTeeth, toothKey] };
}

/** The buffer as one save: clears are sent as the tooth and site they name. */
export function toBatch(b: EntryBuffer): PerioEntryBatch {
  return {
    readings: Object.values(b.readings), teeth: Object.values(b.teeth),
    clearSites: b.clearSites.map((k) => { const [toothKey, site] = k.split(":"); return { toothKey, site }; }), clearTeeth: b.clearTeeth,
  };
}

/** After a failed save: what was in flight goes back, and anything entered since for the same site or tooth wins. */
export function mergeBack(inFlight: EntryBuffer, current: EntryBuffer): EntryBuffer {
  let merged = inFlight;
  for (const k of current.clearSites) { const [t, s] = k.split(":"); merged = withSiteCleared(merged, t, s); }
  for (const k of current.clearTeeth) merged = withToothCleared(merged, k);
  for (const r of Object.values(current.readings)) merged = withReading(merged, r);
  for (const t of Object.values(current.teeth)) merged = withTooth(merged, t);
  return merged;
}

/** The draft as the screen shows it: the server's readings, then the buffer on top (entered or cleared). */
export function effectiveReadings(server: readonly PerioReading[], ...buffers: EntryBuffer[]): Map<string, PerioReadingInput> {
  const map = new Map<string, PerioReadingInput>(server.map((r) => [cellId(r.toothKey, r.site), r]));
  for (const b of buffers) {
    for (const k of b.clearSites) map.delete(k);
    for (const [k, r] of Object.entries(b.readings)) map.set(k, r);
  }
  return map;
}

export function effectiveTeeth(server: readonly PerioTooth[], ...buffers: EntryBuffer[]): Map<string, PerioToothInput> {
  const map = new Map<string, PerioToothInput>(server.map((t) => [t.toothKey, t]));
  for (const b of buffers) {
    for (const k of b.clearTeeth) map.delete(k);
    for (const [k, t] of Object.entries(b.teeth)) map.set(k, t);
  }
  return map;
}

/** Teeth the entry order passes over: marked not charted, or missing in the odontogram. */
export function skippedTeeth(teeth: Map<string, PerioToothInput>, absent: readonly string[]): Set<string> {
  return new Set([...[...teeth.values()].filter((t) => t.excluded).map((t) => t.toothKey), ...absent]);
}

/** The entry order for this draft, and where in it each site is. */
export interface Path { order: SiteRef[]; indexOf: Map<string, number> }
export function pathFor(skipped: ReadonlySet<string>): Path {
  const order = sweep(skipped);
  return { order, indexOf: new Map(order.map((s, i) => [cellId(s.toothKey, s.site), i])) };
}

/** The first site at or after `from` that has no reading yet; the last site when every one has (so the cursor always rests somewhere), or -1 for an empty path. */
export function firstOpenIndex(path: Path, readings: Map<string, unknown>, from = 0): number {
  for (let i = from; i < path.order.length; i++) if (!readings.has(cellId(path.order[i].toothKey, path.order[i].site))) return i;
  return path.order.length === 0 ? -1 : path.order.length - 1;
}

/** Index of the first site of a tooth in the entry order, or -1 when the tooth is skipped. */
export function firstIndexOfTooth(path: Path, toothKey: string): number {
  return path.order.findIndex((s) => s.toothKey === toothKey);
}

export const allEntered = (path: Path, readings: Map<string, unknown>) => path.order.length > 0 && path.order.every((s) => readings.has(cellId(s.toothKey, s.site)));

/** The first site at or after `from` with no reading, going on from the start if there is none ahead; null when every site has one. */
export function nextOpenIndex(path: Path, readings: Map<string, unknown>, from: number): number | null {
  for (const start of [from, 0]) {
    for (let i = start; i < path.order.length; i++) if (!readings.has(cellId(path.order[i].toothKey, path.order[i].site))) return i;
  }
  return null;
}

/** In words, never colour alone: where each tooth stands in this chart. */
export function toothState(toothKey: string, readings: ReadonlyMap<string, unknown>, teeth: ReadonlyMap<string, PerioToothInput>, absent: readonly string[]): string {
  if (teeth.get(toothKey)?.excluded) return "not charted";
  if (absent.includes(toothKey)) return "missing in the odontogram";
  const n = SITES.filter((s) => readings.has(cellId(toothKey, s))).length;
  return n === 0 ? "not started" : n === SITES.length ? "charted" : `${n} of ${SITES.length} sites`;
}
