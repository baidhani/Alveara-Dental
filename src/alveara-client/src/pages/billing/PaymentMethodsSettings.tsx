import { useId, useState } from "react";
import { PAYMENT_METHODS, addPaymentMethod, setPaymentMethodActive } from "../../services/billingApi";
import type { PaymentMethod } from "../../services/billingApi";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  /** Every method, active or not. */
  methods: PaymentMethod[];
  onChanged: (methods: PaymentMethod[], text: string) => void;
}

/**
 * ALV-007-C01: the practice's payment methods, owned by billing. The four built-in ones are always available; billing adds named methods (each is one of the four kinds the ledger reports on) and stops using one it
 * no longer needs, which only stops it being chosen for new payments: old payments keep showing it. A method is never deleted or renamed.
 */
export function PaymentMethodsSettings({ methods, onChanged }: Props) {
  const [name, setName] = useState("");
  const [kind, setKind] = useState("Other");
  const [message, setMessage] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const id = useId();

  const replace = (updated: PaymentMethod) => methods.some((m) => m.id === updated.id) ? methods.map((m) => (m.id === updated.id ? updated : m)) : [...methods, updated];

  async function add() {
    setBusy(true);
    setMessage(null);
    setFields({});
    try {
      const added = await addPaymentMethod(name.trim(), kind);
      setName("");
      onChanged(replace(added), `Payment method ${added.name} is available.`);
    } catch (err) {
      const failure = describeBillingFailure(err);
      setMessage(failure.message.replace("Not recorded:", "Not added:"));
      setFields(failure.fields);
    } finally {
      setBusy(false);
    }
  }

  async function toggle(method: PaymentMethod) {
    setBusy(true);
    setMessage(null);
    try {
      const updated = await setPaymentMethodActive(method, !method.isActive);
      onChanged(replace(updated), `${updated.name} ${updated.isActive ? "can be chosen again" : "is no longer offered for new payments"}.`);
    } catch (err) {
      setMessage(describeBillingFailure(err).message.replace("Not recorded:", "Not changed:"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <details className="alv-bill__methods">
      <summary>Payment methods</summary>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <table className="alv-bill__table" aria-label="Payment methods">
        <thead><tr><th scope="col">Method</th><th scope="col">Kind</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
        <tbody>
          {methods.map((m) => (
            <tr key={m.id}>
              <td>{m.name}{m.isBuiltIn ? " (built in)" : ""}</td><td>{m.kind}</td><td>{m.isActive ? "In use" : "No longer used"}</td>
              <td>{!m.isBuiltIn && <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void toggle(m)}>{m.isActive ? `Stop using ${m.name}` : `Use ${m.name} again`}</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <form className="alv-dx__form" aria-label="Add a payment method" noValidate onSubmit={(e) => { e.preventDefault(); void add(); }}>
        <label htmlFor={`${id}-name`}>Method name
          <input id={`${id}-name`} type="text" maxLength={40} value={name} aria-invalid={fields.name ? true : undefined} onChange={(e) => setName(e.target.value)} />
        </label>
        {fields.name && <p className="alv-bill__error">{fields.name}</p>}
        <label htmlFor={`${id}-kind`}>It is a kind of
          <select id={`${id}-kind`} value={kind} aria-invalid={fields.kind ? true : undefined} onChange={(e) => setKind(e.target.value)}>{PAYMENT_METHODS.map((k) => <option key={k} value={k}>{k}</option>)}</select>
        </label>
        {fields.kind && <p className="alv-bill__error">{fields.kind}</p>}
        <div className="alv-clinical__row-actions"><button type="submit" className="btn btn-primary" disabled={busy}>Add method</button></div>
      </form>
    </details>
  );
}
