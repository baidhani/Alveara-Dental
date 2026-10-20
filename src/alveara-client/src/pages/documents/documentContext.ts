import { ALL_KEYS, DEFAULT_NUMBERING, UPPER_PERMANENT, LOWER_PERMANENT, displayTooth, toothName } from "../odontogram/toothNumbering";

export type ContextErrors = Partial<Record<"encounter" | "toothKey" | "regionKey", string>>;

/** The teeth in the order a dentist looks at the patient: upper permanent, lower permanent, then the primary teeth. */
export const TEETH = [...UPPER_PERMANENT, ...LOWER_PERMANENT, ...ALL_KEYS.filter((k) => !UPPER_PERMANENT.includes(k) && !LOWER_PERMANENT.includes(k))];

export const toothOptionLabel = (key: string) => `${displayTooth(key, DEFAULT_NUMBERING)} - ${toothName(key)}`;
