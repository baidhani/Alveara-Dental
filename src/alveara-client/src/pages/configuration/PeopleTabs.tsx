import { useCallback, useEffect, useMemo, useState } from "react";
import { ConfigEntityPanel } from "../../components/ConfigEntityPanel";
import type { ConfigField } from "../../components/ConfigEntityPanel";
import {
  createProvider, createStaff, listLinkableAccounts, listProviders, listStaff, setProviderActive, setStaffActive,
  updateProvider, updateStaff,
} from "../../services/configApi";
import type { LinkableAccount, ProviderRecord, StaffRecord } from "../../services/configApi";

interface TabProps {
  onDirtyChange: (dirty: boolean) => void;
}

const PERMISSION = "ManagePracticeConfiguration";

/**
 * Staff profiles. The login-account link is optional and explicit: the picker offers only enabled,
 * not-yet-linked accounts (plus the profile's own current account), and the server re-validates.
 */
export function StaffTab({ onDirtyChange }: TabProps) {
  const [accounts, setAccounts] = useState<LinkableAccount[]>([]);
  const [accountsKey, setAccountsKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    listLinkableAccounts()
      .then((a) => !cancelled && setAccounts(a))
      .catch(() => !cancelled && setAccounts([])); // the panel surfaces its own load errors
    return () => {
      cancelled = true;
    };
  }, [accountsKey]);

  const accountLabel = useCallback(
    (id: string | null) => {
      if (!id) return "No login account";
      const a = accounts.find((x) => x.id === id);
      return a ? `${a.username}${a.isDisabled ? " (disabled)" : ""}` : "Linked account";
    },
    [accounts]
  );

  // An account is offered only when it is free or already belongs to the profile being edited, so
  // opening an existing linked profile preselects its own account instead of silently unlinking it.
  const optionsFor = useCallback(
    (editingId: string | null) =>
      accounts
        .filter((a) => a.linkedStaffId === null || a.linkedStaffId === editingId)
        .map((a) => ({ value: a.id, label: `${a.username} (${a.role})${a.isDisabled ? " - disabled" : ""}` })),
    [accounts]
  );

  return (
    <ConfigEntityPanel<StaffRecord>
      noun="staff profile"
      nounPlural="staff profiles"
      permission={PERMISSION}
      onDirtyChange={onDirtyChange}
      fields={[
        { name: "displayName", label: "Display name", type: "text", required: true, maxLength: 120 },
        { name: "jobTitle", label: "Job title", type: "text", maxLength: 80 },
        { name: "userAccountId", label: "Login account (optional)", type: "select", optionsFor },
      ]}
      columns={[
        { header: "Name", render: (r) => r.displayName },
        { header: "Job title", render: (r) => r.jobTitle ?? "-" },
        { header: "Login account", render: (r) => accountLabel(r.userAccountId) },
      ]}
      load={listStaff}
      create={async (v) => {
        const created = await createStaff(v.displayName.trim(), v.jobTitle.trim(), v.userAccountId || null);
        setAccountsKey((k) => k + 1);
        return created;
      }}
      update={async (row, v) => {
        const updated = await updateStaff(row.id, v.displayName.trim(), v.jobTitle.trim(), v.userAccountId || null, row.rowVersion);
        setAccountsKey((k) => k + 1);
        return updated;
      }}
      setActive={(row, active) => setStaffActive(row.id, active, row.rowVersion)}
      toValues={(r) => ({ displayName: r.displayName, jobTitle: r.jobTitle ?? "", userAccountId: r.userAccountId ?? "" })}
      searchText={(r) => r.displayName}
    />
  );
}

/** Provider profiles: a clinical profile that belongs to exactly one active staff member. */
export function ProvidersTab({ onDirtyChange }: TabProps) {
  const [staff, setStaff] = useState<StaffRecord[]>([]);
  const [providers, setProviders] = useState<ProviderRecord[]>([]);
  const [optionsKey, setOptionsKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    Promise.all([listStaff(false), listProviders(true)])
      .then(([s, p]) => {
        if (cancelled) return;
        setStaff(s);
        setProviders(p);
      })
      .catch(() => undefined); // the panel surfaces its own load errors
    return () => {
      cancelled = true;
    };
  }, [optionsKey]);

  // Only active staff without a provider profile yet can become one.
  const options = useMemo(() => {
    const taken = new Set(providers.map((p) => p.staffProfileId));
    return staff.filter((s) => !taken.has(s.id)).map((s) => ({ value: s.id, label: s.displayName }));
  }, [staff, providers]);

  const providerFields: ConfigField[] = [
    { name: "staffProfileId", label: "Staff member", type: "select", required: true, createOnly: true, options },
    { name: "specialty", label: "Specialty", type: "text", required: true, maxLength: 80 },
  ];

  return (
    <ConfigEntityPanel<ProviderRecord>
      noun="provider"
      nounPlural="providers"
      permission={PERMISSION}
      onDirtyChange={onDirtyChange}
      createDisabledReason={options.length === 0 ? "Every active staff member already has a provider profile. Add a staff profile first." : undefined}
      fields={providerFields}
      columns={[
        { header: "Provider", render: (r) => r.displayName ?? "-" },
        { header: "Specialty", render: (r) => r.specialty },
      ]}
      load={listProviders}
      create={async (v) => {
        const created = await createProvider(v.staffProfileId, v.specialty.trim());
        setOptionsKey((k) => k + 1);
        return created;
      }}
      update={(row, v) => updateProvider(row.id, v.specialty.trim(), row.rowVersion)}
      setActive={(row, active) => setProviderActive(row.id, active, row.rowVersion)}
      toValues={(r) => ({ staffProfileId: r.staffProfileId, specialty: r.specialty })}
      searchText={(r) => r.displayName ?? r.specialty}
    />
  );
}
