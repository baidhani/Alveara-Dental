import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { billingFieldErrorsOf } from "../../services/billingApi";

export interface BillingFailure {
  /** The sentence to show: it always says nothing was recorded, so a person never guesses. */
  message: string;
  /** The server's message for each field it refused, to show beside that field. */
  fields: Record<string, string>;
  /** True when the account changed under the person (someone else recorded something). */
  conflict: boolean;
}

/**
 * STORY-007: what a refused or failed billing save says. A refusal names its fields, a stale account says someone else changed it, a dropped connection says to try again, and a save the server could
 * not complete says nothing was recorded; every one of them says the account was not changed.
 */
export function describeBillingFailure(err: unknown): BillingFailure {
  const fields = billingFieldErrorsOf(err);
  if (isConcurrencyConflict(err)) return { message: "Not recorded: someone else changed this account. Reload to see the latest balance, then try again.", fields: {}, conflict: true };
  if (Object.keys(fields).length > 0) return { message: "Not recorded: some entries need correcting.", fields, conflict: false };
  if (isNetworkFailure(err)) return { message: "Not recorded: the connection dropped. What you typed is still here; try again.", fields: {}, conflict: false };
  return { message: `Not recorded: ${err instanceof ApiError ? err.message : "something went wrong."}`, fields: {}, conflict: false };
}
