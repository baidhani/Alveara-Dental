/** A number in a report that can be opened: pressing it lists the records behind it. A zero is plain text (there is nothing to list). */
export function NumberButton({ value, label, onOpen, selected }: { value: number; label: string; onOpen: () => void; selected?: boolean }) {
  if (value === 0) return <span>0</span>;
  return (
    <button type="button" className="alv-rep__number" aria-label={`${value}, show the records: ${label}`} aria-pressed={selected ?? false} onClick={onOpen}>{value.toLocaleString()}</button>
  );
}
