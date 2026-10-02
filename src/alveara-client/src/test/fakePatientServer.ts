import { vi } from "vitest";
import type { PatientDetail, PatientLink } from "../services/patientsApi";

/**
 * A small in-memory stand-in for the patient API, installed as the global fetch in component tests. It models only what the UI
 * depends on: the response SHAPES (same as Controllers/PatientsController), row-version checking (a stale PUT is the shared 409),
 * and canned failures a test can inject. The real rules are proven against the real API by the backend tests and the real-backend
 * browser walkthrough; this exists so the UI's handling of each outcome can be tested in isolation.
 */
export interface Call {
  method: string;
  url: string;
  headers: Record<string, string>;
  body: Record<string, unknown> | null;
}

type Stored = Omit<PatientDetail, "guarantor" | "guaranteeFor" | "household" | "age"> & { guarantorId: string | null; householdId: string | null; householdRelationship: string | null; version: number };

export const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

export function makePatient(over: Partial<Stored> & { id: string }): Stored {
  return {
    firstName: "Ann", middleName: "", lastName: "Lee", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", email: "",
    addressLine1: "1 Main St", addressLine2: "", city: "Austin", state: "TX", postalCode: "78701",
    createdAtUtc: "2026-10-01T12:00:00Z", updatedAtUtc: null, isActive: true, rowVersion: "v1",
    guarantorId: null, householdId: null, householdRelationship: null, version: 1, ...over,
  };
}

export class FakePatientServer {
  patients = new Map<string, Stored>();
  permissions: string[] = ["ViewPatientRecords", "RegisterPatients", "EditPatients"];
  calls: Call[] = [];
  history: Record<string, unknown[]> = {};
  /** Responses a test wants for the NEXT matching request: key is `METHOD path-prefix`. */
  private injected = new Map<string, Response[]>();
  /** Per-patient artificial latency for GET /api/patients/:id, so a test can make an earlier request answer last. */
  detailDelay = new Map<string, Promise<void>>();
  /** The practice's extra required fields; rowVersion "" until first saved (as the real API reports). */
  settings = { requireEmail: false, requireSex: false, rowVersion: "", updatedAtUtc: null as string | null };

  add(p: Stored) {
    this.patients.set(p.id, p);
    return p;
  }

  fail(methodAndPath: string, response: Response) {
    const list = this.injected.get(methodAndPath) ?? [];
    list.push(response);
    this.injected.set(methodAndPath, list);
  }

  callsTo(method: string, pathPrefix: string) {
    return this.calls.filter((c) => c.method === method && new URL(c.url, "http://x").pathname.startsWith(pathPrefix));
  }

  install() {
    vi.stubGlobal("fetch", vi.fn((url: string, init?: RequestInit) => this.handle(String(url), init)));
  }

  private link(id: string | null): PatientLink | null {
    const p = id ? this.patients.get(id) : undefined;
    return p ? { id: p.id, displayName: `${p.firstName} ${p.lastName}`, isActive: p.isActive } : null;
  }

  detailOf(id: string): PatientDetail {
    const p = this.patients.get(id)!;
    const byName = (a: Stored, b: Stored) => a.lastName.localeCompare(b.lastName) || a.firstName.localeCompare(b.firstName); // the real server orders by last name, then first
    const members = p.householdId ? [...this.patients.values()].filter((m) => m.householdId === p.householdId).sort(byName) : [];
    return {
      id: p.id, firstName: p.firstName, middleName: p.middleName, lastName: p.lastName, dateOfBirth: p.dateOfBirth, sex: p.sex,
      phone: p.phone, email: p.email, addressLine1: p.addressLine1, addressLine2: p.addressLine2, city: p.city, state: p.state, postalCode: p.postalCode,
      createdAtUtc: p.createdAtUtc, updatedAtUtc: p.updatedAtUtc, isActive: p.isActive, rowVersion: `v${p.version}`, age: 40,
      guarantor: this.link(p.guarantorId),
      guaranteeFor: [...this.patients.values()].filter((o) => o.guarantorId === p.id).map((o) => this.link(o.id)!),
      household: p.householdId
        ? { id: p.householdId, relationship: p.householdRelationship, members: members.map((m) => ({ ...this.link(m.id)!, relationship: m.householdRelationship })) }
        : null,
    };
  }

  /** A subclass can serve other API areas (ALV-N010's forms) ahead of the patient routes; return null to fall through. */
  protected async handleExtra(_path: string, _method: string, _headers: Record<string, string>, _body: Record<string, unknown> | null, _url: URL): Promise<Response | null> {
    return null;
  }

  private async handle(url: string, init?: RequestInit): Promise<Response> {
    const u = new URL(url, "http://x");
    const method = init?.method ?? "GET";
    const headers = (init?.headers ?? {}) as Record<string, string>;
    const body = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : null;
    const path = u.pathname;

    if (path === "/api/auth/csrf-token") return json(200, { token: "csrf-1" });
    if (path === "/api/auth/permissions")
      return json(200, { username: "desk-1", role: "FrontDesk", permissions: this.permissions, sessionExpiresAtUtc: new Date(Date.now() + 1_800_000).toISOString() });
    if (path === "/api/health") return json(200, { status: "ok" });
    const extra = await this.handleExtra(path, method, headers, body, u);
    if (extra) return extra;
    if (!path.startsWith("/api/patients")) return json(200, []);

    this.calls.push({ method, url, headers, body });
    for (const [key, queue] of this.injected) {
      const [m, prefix] = key.split(" ");
      if (m === method && path.startsWith(prefix) && queue.length > 0) return queue.shift()!;
    }

    if (path === "/api/patients/registration-settings") {
      if (method === "GET") return json(200, this.settings);
      if (method === "PUT") {
        if (body?.rowVersion !== this.settings.rowVersion)
          return json(409, { error: "concurrency_conflict", entityType: "PatientRegistrationSettings", entityId: "x", message: "Changed by someone else." });
        this.settings = { requireEmail: body!.requireEmail as boolean, requireSex: body!.requireSex as boolean, rowVersion: `s${Number(this.settings.rowVersion.slice(1) || 0) + 1}`, updatedAtUtc: "2026-10-02T00:00:00Z" };
        return json(200, this.settings);
      }
    }

    const id = path.split("/")[3];
    const sub = path.split("/")[4];

    if (method === "GET" && path === "/api/patients") {
      const q = (u.searchParams.get("q") ?? "").toLowerCase();
      const includeInactive = u.searchParams.get("includeInactive") === "true";
      const rows = [...this.patients.values()]
        .filter((p) => (includeInactive || p.isActive) && (q === "" || `${p.firstName} ${p.lastName}`.toLowerCase().includes(q)))
        .map((p) => ({ id: p.id, firstName: p.firstName, middleName: null, lastName: p.lastName, dateOfBirth: p.dateOfBirth, age: 40, sex: p.sex, phone: p.phone, city: p.city, state: p.state, isActive: p.isActive }));
      return json(200, rows);
    }
    if (method === "GET" && id && !sub) {
      const wait = this.detailDelay.get(id);
      if (wait) await wait;
      return this.patients.has(id) ? json(200, this.detailOf(id)) : json(404, { error: "patient_not_found" });
    }
    if (method === "GET" && sub === "history") return json(200, this.history[id] ?? []);

    if (method === "PUT" && id) {
      const p = this.patients.get(id);
      if (!p) return json(404, { error: "patient_not_found", message: "That patient was not found." });
      if (body?.rowVersion !== `v${p.version}`)
        return json(409, { error: "concurrency_conflict", entityType: "Patient", entityId: id, message: "Changed by someone else." });
      if (!sub) Object.assign(p, Object.fromEntries(Object.entries(body ?? {}).filter(([k]) => k !== "rowVersion")));
      if (sub === "active") p.isActive = body!.isActive as boolean;
      if (sub === "guarantor") p.guarantorId = (body!.guarantorPatientId as string | null) ?? null;
      if (sub === "household") {
        const anchor = body!.anchorPatientId as string | null;
        if (anchor === null) { p.householdId = null; p.householdRelationship = null; }
        else {
          const a = this.patients.get(anchor)!;
          a.householdId ??= `h-${anchor}`;
          a.householdRelationship ??= "Self";
          p.householdId = a.householdId;
          p.householdRelationship = body!.relationship as string;
        }
      }
      p.version++;
      return json(200, { ...this.detailOf(id), rowVersion: `v${p.version}` });
    }
    return json(200, {});
  }
}
