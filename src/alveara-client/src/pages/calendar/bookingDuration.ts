/**
 * ALV-N021 A2 (OD-21, F-044b): the duration a booking form shows after the appointment type changes. The type's length fills the field and stays editable;
 * a length the person typed is never replaced (`typed`); a cleared field follows the type again. A reschedule does not use this (blank keeps the current length).
 */
export function durationAfterTypeChange(current: string, typed: boolean, typeLength: number | undefined): string {
  if (typed && current.trim() !== "") return current;
  return typeLength === undefined ? "" : String(typeLength);
}

/** The hint under a booking's duration field once it is filled from the type. */
export const bookingDurationHint = (typeLength: number | undefined) =>
  typeLength === undefined ? "Choose an appointment type to fill in its length." : `Filled in from the appointment type (${typeLength} minutes). You can change it.`;
