import { useEffect, useState } from "react";
import { ConfigEntityPanel } from "../../components/ConfigEntityPanel";
import type { FormValues } from "../../components/ConfigEntityPanel";
import {
  createAppointmentType, createLocation, createOperatory, listAppointmentTypes, listLocations, listOperatories,
  setAppointmentTypeActive, setLocationActive, setOperatoryActive, updateAppointmentType, updateLocation, updateOperatory,
} from "../../services/configApi";
import type { AppointmentTypeRecord, LocationRecord, OperatoryRecord } from "../../services/configApi";

interface TabProps {
  onDirtyChange: (dirty: boolean) => void;
}

const PERMISSION = "ManagePracticeConfiguration";

/** Operatories hang off the single active location, so adding one is blocked (with the reason) until it exists. */
export function OperatoriesTab({ onDirtyChange }: TabProps) {
  const [hasActiveLocation, setHasActiveLocation] = useState<boolean | null>(null);

  useEffect(() => {
    let cancelled = false;
    listLocations(false)
      .then((rows) => !cancelled && setHasActiveLocation(rows.length > 0))
      .catch(() => !cancelled && setHasActiveLocation(null)); // unknown: let the server decide on submit
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <ConfigEntityPanel<OperatoryRecord>
      noun="operatory"
      nounPlural="operatories"
      permission={PERMISSION}
      onDirtyChange={onDirtyChange}
      createDisabledReason={hasActiveLocation === false ? "Create an active location on the Practice tab before adding operatories." : undefined}
      fields={[{ name: "name", label: "Operatory name", type: "text", required: true, maxLength: 120 }]}
      columns={[{ header: "Name", render: (r) => r.name }]}
      load={listOperatories}
      create={(v: FormValues) => createOperatory(v.name.trim())}
      update={(row, v) => updateOperatory(row.id, v.name.trim(), row.rowVersion)}
      setActive={(row, active) => setOperatoryActive(row.id, active, row.rowVersion)}
      toValues={(r) => ({ name: r.name })}
      searchText={(r) => r.name}
    />
  );
}

export function AppointmentTypesTab({ onDirtyChange }: TabProps) {
  return (
    <ConfigEntityPanel<AppointmentTypeRecord>
      noun="appointment type"
      nounPlural="appointment types"
      permission={PERMISSION}
      onDirtyChange={onDirtyChange}
      fields={[
        { name: "name", label: "Appointment type name", type: "text", required: true, maxLength: 120 },
        { name: "duration", label: "Default duration (minutes)", type: "number", required: true, min: 5, max: 480, step: 5, hint: "5 to 480 minutes, in steps of 5." },
      ]}
      columns={[
        { header: "Name", render: (r) => r.name },
        { header: "Default duration", render: (r) => `${r.defaultDurationMinutes} min` },
      ]}
      load={listAppointmentTypes}
      create={(v) => createAppointmentType(v.name.trim(), Number(v.duration))}
      update={(row, v) => updateAppointmentType(row.id, v.name.trim(), Number(v.duration), row.rowVersion)}
      setActive={(row, active) => setAppointmentTypeActive(row.id, active, row.rowVersion)}
      toValues={(r) => ({ name: r.name, duration: String(r.defaultDurationMinutes) })}
      searchText={(r) => r.name}
    />
  );
}

export function LocationPanel({ onDirtyChange }: TabProps) {
  return (
    <ConfigEntityPanel<LocationRecord>
      noun="location"
      nounPlural="locations"
      permission={PERMISSION}
      onDirtyChange={onDirtyChange}
      fields={[{ name: "name", label: "Location name", type: "text", required: true, maxLength: 120, hint: "First release supports one active location." }]}
      columns={[{ header: "Name", render: (r) => r.name }]}
      load={listLocations}
      create={(v) => createLocation(v.name.trim())}
      update={(row, v) => updateLocation(row.id, v.name.trim(), row.rowVersion)}
      setActive={(row, active) => setLocationActive(row.id, active, row.rowVersion)}
      toValues={(r) => ({ name: r.name })}
      searchText={(r) => r.name}
    />
  );
}
