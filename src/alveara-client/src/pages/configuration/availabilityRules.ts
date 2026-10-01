/** Weekly-availability rules shared by the availability editor, the scheduling preview, and tests. */
export const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

export interface WindowRow {
  key: number;
  dayOfWeek: string;
  startLocal: string;
  endLocal: string;
}

/** Client mirror of the server's rules; returns a message per row index plus any whole-schedule error. */
export function validateWindows(rows: WindowRow[]): Record<number, string> {
  const errors: Record<number, string> = {};
  rows.forEach((row, i) => {
    if (!row.startLocal || !row.endLocal) errors[i] = "Enter both a start and an end time.";
    else if (row.endLocal <= row.startLocal) errors[i] = "A window must end after it starts.";
  });
  DAY_NAMES.forEach((_, day) => {
    const sameDay = rows
      .map((row, i) => ({ row, i }))
      .filter(({ row, i }) => Number(row.dayOfWeek) === day && errors[i] === undefined)
      .sort((a, b) => a.row.startLocal.localeCompare(b.row.startLocal));
    for (let k = 1; k < sameDay.length; k++) {
      if (sameDay[k].row.startLocal < sameDay[k - 1].row.endLocal) {
        errors[sameDay[k].i] = `Overlaps another ${DAY_NAMES[day]} window.`;
      }
    }
  });
  return errors;
}
