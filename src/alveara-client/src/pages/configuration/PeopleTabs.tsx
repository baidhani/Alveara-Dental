import { useCallback, useEffect, useMemo, useState } from "react";
import { ConfirmStaffInactivation, UnlinkLoginAction } from "./StaffLoginDialogs";
import { LegacyLoginAction, TemporaryPasswordPanel } from "./StaffMemberDialogs";
import type { IssuedLogin } from "./StaffMemberDialogs";
import { useAuth } from "../../contexts/AuthContext";
import { useNotifications } from "../../components/Notification";
import { ConfigEntityPanel } from "../../components/ConfigEntityPanel";
import type { ConfigField } from "../../components/ConfigEntityPanel";
import { ApiError } from "../../services/authApi";
import { addStaffMember, getPracticeOwner, WORKING_ROLES } from "../../services/accountsApi";
import {
  createProvider, listProviders, listStaff, setProviderActive, setStaffActive,
  updateProvider, updateStaff,
} from "../../services/configApi";
import type { ProviderRecord, StaffRecord } from "../../services/configApi";

interface TabProps {
  onDirtyChange: (dirty: boolean) => void;
}

const PERMISSION = "ManagePracticeConfiguration";

/** Stable codes from "Add staff member", in the screen's own words. */
/** ALV-N030-C01 (brief 18.8): shown beside the owner's disabled Inactivate button. */
export const OWNER_PROTECTED = "This person is the Practice Owner. Transfer the Practice Owner designation first.";

const ADD_ERRORS: Record<string, string> = {
  staff_name_taken: "A staff member with that display name already exists.",
  username_taken: "That username is already in use.",
  username_required: "Enter a username.",
  role_invalid: "Choose a role.",
  target_is_admin: "Only an administrator can create an administrator login.",
  target_exceeds_actor: "You can only create a login whose permissions you hold yourself.",
  owner_must_be_administrator: "The Practice Owner must sign in with the Administrator role. Choose Administrator as the role.",
  owner_already_designated: "A Practice Owner has already been designated. Nobody was added; untick \"Make this person the Practice Owner\" to add this person as staff.",
  owner_designation_requires_administrator: "Only an Administrator can add the Practice Owner.",
};

/**
 * ALV-N030 (brief section 9, R01 CR-1): staff are added WITH their login ("Add staff member": display name, job title, username, role, and - while no
 * Practice Owner exists - "Make this person the Practice Owner"). The temporary password is shown once. There is no profile-only create and no
 * "link an existing account" picker; a record created before logins were required is labelled "Legacy record without a login" and offers
 * "Create a login for this legacy staff record". Adding needs Manage users as well (it creates a login); the server enforces both.
 */
export function StaffTab({ onDirtyChange }: TabProps) {
  const { hasPermission, state, refresh: refreshIdentity } = useAuth();
  // ALV-N030-C01 (brief 18.8, E4): the owner's profile cannot be inactivated until the designation is transferred; the server refuses regardless
  const [ownerStaffId, setOwnerStaffId] = useState<string | null>(null);
  const { notify } = useNotifications();
  const canManageUsers = hasPermission("ManageUsers");
  const ownerExists = state.kind === "signed-in" && state.practiceOwnerExists === true;
  const [issued, setIssued] = useState<IssuedLogin | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  useEffect(() => {
    if (!canManageUsers || !ownerExists) return;
    let cancelled = false;
    getPracticeOwner()
      .then((r) => { if (!cancelled) setOwnerStaffId(r.owner?.staffProfileId ?? null); })
      .catch((err) => console.warn("Reading the Practice Owner failed; the server still refuses inactivating the owner", err));
    return () => { cancelled = true; };
  }, [canManageUsers, ownerExists, refreshKey]);

  const loginLabel = useCallback((row: StaffRecord) => (row.userAccountId ? "Has a login" : "Legacy record without a login"), []);

  // ALV-N020 (F-021): inactivating a profile with a login asks first (the login will be disabled); the panel waits for the answer.
  // Inactivation and unlinking also need ManageUsers (review decision 1); the server enforces it, the screen says so first.
  const [pending, setPending] = useState<{ row: StaffRecord; resolve: (ok: boolean) => void } | null>(null);
  const beforeSetActive = useCallback(
    (row: StaffRecord, active: boolean) => {
      if (active) return Promise.resolve(true);
      if (!canManageUsers) {
        notify("warning", "Inactivating a staff member also disables their login, which needs the Manage users permission.");
        return Promise.resolve(false);
      }
      return row.userAccountId ? new Promise<boolean>((resolve) => setPending({ row, resolve })) : Promise.resolve(true);
    },
    [canManageUsers, notify]
  );
  const answer = (ok: boolean) => {
    pending?.resolve(ok);
    setPending(null);
  };

  const fields = useMemo<ConfigField[]>(() => {
    const list: ConfigField[] = [
      { name: "displayName", label: "Display name", type: "text", required: true, maxLength: 120 },
      { name: "jobTitle", label: "Job title", type: "text", maxLength: 80 },
      { name: "username", label: "Username", type: "text", required: true, maxLength: 256, createOnly: true, hint: "The name this person signs in with." },
      { name: "role", label: "Role", type: "select", required: true, createOnly: true, options: WORKING_ROLES.map((r) => ({ value: r.value, label: r.label })),
        // ALV-N030-C01 (brief 18.8, owner decision 4): the owner box presets and locks the role; the server refuses any other role (E2)
        lockedBy: ownerExists ? undefined : { field: "practiceOwner", value: "Admin",
          hint: "The Practice Owner signs in with the Administrator role so they can configure and run the practice. Their job title and provider profile still describe their clinical work." } },
    ];
    if (!ownerExists) list.push({ name: "practiceOwner", label: "Make this person the Practice Owner", type: "checkbox", createOnly: true,
      hint: "No Practice Owner has been designated yet. Only one person can be the Practice Owner." });
    return list;
  }, [ownerExists]);

  return (
    <>
    {issued && <TemporaryPasswordPanel issued={issued} onDismiss={() => setIssued(null)} />}
    {pending && <ConfirmStaffInactivation row={pending.row} loginLabel={loginLabel(pending.row).toLowerCase()} onAnswer={answer} />}
    <ConfigEntityPanel<StaffRecord>
      noun="staff member"
      nounPlural="staff members"
      permission={PERMISSION}
      onDirtyChange={onDirtyChange}
      refreshKey={refreshKey}
      createDisabledReason={canManageUsers ? undefined : "Adding a staff member also creates their login, which needs the Manage users permission."}
      fields={fields}
      columns={[
        { header: "Name", render: (r) => r.displayName },
        { header: "Job title", render: (r) => r.jobTitle ?? "-" },
        { header: "Login account", render: (r) => loginLabel(r) },
      ]}
      load={listStaff}
      create={async (v) => {
        let created;
        try {
          created = await addStaffMember(v.displayName.trim(), v.jobTitle.trim(), v.username.trim(), v.role, v.practiceOwner === "true");
        } catch (err) {
          throw err instanceof ApiError && ADD_ERRORS[err.code] ? new ApiError(err.status, err.code, ADD_ERRORS[err.code], err.body) : err;
        }
        if (!created.temporaryPassword) {
          notify("warning", "This staff member was already added. If the temporary password was lost, issue a password reset in Security Administration.");
          return created;
        }
        setIssued({ displayName: v.displayName.trim(), username: created.username, temporaryPassword: created.temporaryPassword });
        if (created.practiceOwner) {
          // ALV-N030-C01 (brief 18.4): one server call made the person and the designation together, so there is no partial outcome to report
          notify("success", `${v.displayName.trim()} is now the Practice Owner.`);
          await refreshIdentity();
        }
        return created;
      }}
      update={(row, v) => updateStaff(row.id, v.displayName.trim(), v.jobTitle.trim(), row.rowVersion)}
      activeToggleDisabledReason={(row) => (canManageUsers && ownerExists && row.isActive && row.id === ownerStaffId ? OWNER_PROTECTED : undefined)}
      setActive={(row, active) => setStaffActive(row.id, active, row.rowVersion)}
      toValues={(r) => ({ displayName: r.displayName, jobTitle: r.jobTitle ?? "" })}
      searchText={(r) => r.displayName}
      beforeSetActive={beforeSetActive}
      rowActions={(row, reload) => canManageUsers && (
        <>
          <UnlinkLoginAction row={row} onDone={reload} />
          <LegacyLoginAction row={row} onIssued={async (login) => { setIssued(login); setRefreshKey((k) => k + 1); }} />
        </>
      )}
    />
    </>
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
