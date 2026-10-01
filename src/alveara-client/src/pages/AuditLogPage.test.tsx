import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { AuditLogPage } from "./AuditLogPage";
import { AUDIT_LOG_WINDOW } from "../services/authApi";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("AuditLogPage (ALV-002-C01)", () => {
  it("renders audit entries with actor, action, entity, and time", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse([
          {
            id: "11111111-1111-1111-1111-111111111111",
            eventType: "RoleChanged",
            targetUserAccountId: "22222222-2222-2222-2222-222222222222",
            performedByUserAccountId: "33333333-3333-3333-3333-333333333333",
            details: "Role changed from Assistant to Hygienist.",
            timestampUtc: "2026-09-30T12:00:00Z",
            entityType: "UserAccount",
            reason: null,
            correlationId: null,
          },
        ])
      )
    );

    render(<AuditLogPage />);

    await waitFor(() => expect(screen.getByText("RoleChanged")).toBeInTheDocument());
    expect(screen.getByText(/Role changed from Assistant to Hygienist/)).toBeInTheDocument();
    expect(screen.getByText("UserAccount")).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("requests exactly the window its description claims (take=500), not the server's smaller default", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    render(<AuditLogPage />);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const requestedUrl = String(fetchMock.mock.calls[0][0]);
    expect(requestedUrl).toContain("/api/auth/audit-log");
    expect(requestedUrl).toContain(`take=${AUDIT_LOG_WINDOW}`);
    expect(screen.getByText(new RegExp(`most recent ${AUDIT_LOG_WINDOW} `))).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("shows an empty state, not a blank table, when there are no audit events yet", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    render(<AuditLogPage />);

    await waitFor(() => expect(screen.getByText("No audit events yet")).toBeInTheDocument());
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("shows permission-denied, not a generic error, when the caller lacks ViewAuditLog", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ error: "permission_denied", required: "ViewAuditLog" }, 403)));

    render(<AuditLogPage />);

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("shows a retryable error state on a network/server failure, distinct from permission-denied", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network error")));

    render(<AuditLogPage />);

    await waitFor(() => expect(screen.getByText("Could not load the audit log")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
    vi.unstubAllGlobals();
  });
});
