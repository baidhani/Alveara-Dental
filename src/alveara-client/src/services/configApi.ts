/**
 * ALV-N003: typed client for the practice-configuration API (Controllers/ConfigurationController).
 * Reuses authApi's cookie-session + CSRF request helpers. Every update echoes back the
 * `rowVersion` the record was read with; a stale one comes back as the shared 409
 * concurrency-conflict shape (see authApi.isConcurrencyConflict). There is intentionally no delete
 * call for staff/providers/operatories/appointment types/locations - only `setXActive`.
 */
import { request, requestWithCsrf } from "./authApi";

interface Versioned {
  id: string;
  isActive: boolean;
  rowVersion: string;
}

export interface PracticeInfo {
  id: string | null;
  name: string | null;
  phone: string | null;
  addressLine: string | null;
  /** Reflected read-only from the deployment's PracticeTimeZone (ALV-N002) - not editable here. */
  timeZoneId: string;
  /** Reflected read-only; first release is fixed to USD (ALV-N002). */
  currency: string;
  configured: boolean;
  rowVersion: string | null;
}

export interface LocationRecord extends Versioned {
  name: string;
}
export interface OperatoryRecord extends Versioned {
  locationId: string;
  name: string;
}
export interface AppointmentTypeRecord extends Versioned {
  name: string;
  defaultDurationMinutes: number;
}
export interface StaffRecord extends Versioned {
  displayName: string;
  jobTitle: string | null;
  userAccountId: string | null;
}
export interface ProviderRecord extends Versioned {
  staffProfileId: string;
  displayName: string | null;
  specialty: string;
}
export interface LinkableAccount {
  id: string;
  username: string;
  role: string;
  isDisabled: boolean;
  /** The staff profile that already holds this account, or null if it is free. */
  linkedStaffId: string | null;
}

/** One weekly working window, in practice-local wall-clock time. dayOfWeek: 0 = Sunday .. 6 = Saturday. */
export interface AvailabilityWindow {
  dayOfWeek: number;
  startLocal: string;
  endLocal: string;
}
/** A provider's weekly schedule plus the revision a later replacement must present (R02: the schedule is a versioned aggregate). */
export interface AvailabilitySchedule {
  revision: number;
  windows: AvailabilityWindow[];
}
export interface BlockedTime {
  id: string;
  startUtc: string;
  endUtc: string;
  startLocal: string;
  endLocal: string;
  reason: string | null;
}

export interface SchedulingSnapshot {
  timeZoneId: string;
  activeLocationId: string | null;
  activeLocationName: string | null;
  providers: { providerId: string; displayName: string; specialty: string; weeklyAvailability: AvailabilityWindow[] }[];
  operatories: { id: string; name: string }[];
  appointmentTypes: { id: string; name: string; defaultDurationMinutes: number }[];
}

const q = (includeInactive: boolean) => `?includeInactive=${includeInactive}`;

// ---------- Practice ----------
export const getPractice = () => request<PracticeInfo>("/api/config/practice");
export const savePractice = (body: { name: string; phone: string; addressLine: string; rowVersion: string | null }) =>
  requestWithCsrf<PracticeInfo>("/api/config/practice", "PUT", body);

// ---------- Locations ----------
export const listLocations = (includeInactive: boolean) => request<LocationRecord[]>(`/api/config/locations${q(includeInactive)}`);
export const createLocation = (name: string) => requestWithCsrf<LocationRecord>("/api/config/locations", "POST", { name });
export const updateLocation = (id: string, name: string, rowVersion: string) =>
  requestWithCsrf<LocationRecord>(`/api/config/locations/${id}`, "PUT", { name, rowVersion });
export const setLocationActive = (id: string, isActive: boolean, rowVersion: string) =>
  requestWithCsrf<LocationRecord>(`/api/config/locations/${id}/active`, "PUT", { isActive, rowVersion });

// ---------- Operatories ----------
export const listOperatories = (includeInactive: boolean) => request<OperatoryRecord[]>(`/api/config/operatories${q(includeInactive)}`);
export const createOperatory = (name: string) => requestWithCsrf<OperatoryRecord>("/api/config/operatories", "POST", { name });
export const updateOperatory = (id: string, name: string, rowVersion: string) =>
  requestWithCsrf<OperatoryRecord>(`/api/config/operatories/${id}`, "PUT", { name, rowVersion });
export const setOperatoryActive = (id: string, isActive: boolean, rowVersion: string) =>
  requestWithCsrf<OperatoryRecord>(`/api/config/operatories/${id}/active`, "PUT", { isActive, rowVersion });

// ---------- Appointment types ----------
export const listAppointmentTypes = (includeInactive: boolean) =>
  request<AppointmentTypeRecord[]>(`/api/config/appointment-types${q(includeInactive)}`);
export const createAppointmentType = (name: string, defaultDurationMinutes: number) =>
  requestWithCsrf<AppointmentTypeRecord>("/api/config/appointment-types", "POST", { name, defaultDurationMinutes });
export const updateAppointmentType = (id: string, name: string, defaultDurationMinutes: number, rowVersion: string) =>
  requestWithCsrf<AppointmentTypeRecord>(`/api/config/appointment-types/${id}`, "PUT", { name, defaultDurationMinutes, rowVersion });
export const setAppointmentTypeActive = (id: string, isActive: boolean, rowVersion: string) =>
  requestWithCsrf<AppointmentTypeRecord>(`/api/config/appointment-types/${id}/active`, "PUT", { isActive, rowVersion });

// ---------- Staff ----------
export const listStaff = (includeInactive: boolean) => request<StaffRecord[]>(`/api/config/staff${q(includeInactive)}`);
export const listLinkableAccounts = () => request<LinkableAccount[]>("/api/config/linkable-accounts");
export const createStaff = (displayName: string, jobTitle: string, userAccountId: string | null) =>
  requestWithCsrf<StaffRecord>("/api/config/staff", "POST", { displayName, jobTitle, userAccountId });
export const updateStaff = (id: string, displayName: string, jobTitle: string, userAccountId: string | null, rowVersion: string) =>
  requestWithCsrf<StaffRecord>(`/api/config/staff/${id}`, "PUT", { displayName, jobTitle, userAccountId, rowVersion });
export const setStaffActive = (id: string, isActive: boolean, rowVersion: string) =>
  requestWithCsrf<StaffRecord>(`/api/config/staff/${id}/active`, "PUT", { isActive, rowVersion });

// ---------- Providers ----------
export const listProviders = (includeInactive: boolean) => request<ProviderRecord[]>(`/api/config/providers${q(includeInactive)}`);
export const createProvider = (staffProfileId: string, specialty: string) =>
  requestWithCsrf<ProviderRecord>("/api/config/providers", "POST", { staffProfileId, specialty });
export const updateProvider = (id: string, specialty: string, rowVersion: string) =>
  requestWithCsrf<ProviderRecord>(`/api/config/providers/${id}`, "PUT", { specialty, rowVersion });
export const setProviderActive = (id: string, isActive: boolean, rowVersion: string) =>
  requestWithCsrf<ProviderRecord>(`/api/config/providers/${id}/active`, "PUT", { isActive, rowVersion });

// ---------- Availability + blocked time ----------
export const getAvailability = (providerId: string) => request<AvailabilitySchedule>(`/api/config/providers/${providerId}/availability`);
export const replaceAvailability = (providerId: string, windows: AvailabilityWindow[], revision: number) =>
  requestWithCsrf<AvailabilitySchedule>(`/api/config/providers/${providerId}/availability`, "PUT", { windows, revision });
export const listBlockedTime = (providerId: string) => request<BlockedTime[]>(`/api/config/providers/${providerId}/blocked-time`);
export const addBlockedTime = (providerId: string, startLocal: string, endLocal: string, reason: string) =>
  requestWithCsrf<BlockedTime>(`/api/config/providers/${providerId}/blocked-time`, "POST", { startLocal, endLocal, reason });
export const removeBlockedTime = (providerId: string, blockId: string) =>
  requestWithCsrf<void>(`/api/config/providers/${providerId}/blocked-time/${blockId}`, "DELETE");

// ---------- Scheduling read model ----------
export const getSchedulingSnapshot = () => request<SchedulingSnapshot>("/api/config/scheduling");
