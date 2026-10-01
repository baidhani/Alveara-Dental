import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { readFileSync } from "node:fs";
import path from "node:path";
import { axe } from "jest-axe";
import { AuditLogPage } from "./AuditLogPage";

// Gate A review GATE-A-02 (ALV-002-C01 R04): the audit table scrolls horizontally, so its scroll region must be a named,
// keyboard-reachable region with a visible focus indicator (axe rule scrollable-region-focusable). Browser-level proof with
// overflowing data and real arrow-key scrolling is in the Gate A evidence (gate-a/gate-a-audit-keyboard.spec.ts).

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const entry = (n: number) => ({
  id: `00000000-0000-0000-0000-${String(n).padStart(12, "0")}`,
  eventType: "RoleChanged",
  targetUserAccountId: "22222222-2222-2222-2222-222222222222",
  performedByUserAccountId: "33333333-3333-3333-3333-333333333333",
  details: `Role changed (${n}).`,
  timestampUtc: "2026-09-30T12:00:00Z",
  entityType: "UserAccount",
  reason: null,
  correlationId: null,
});

afterEach(() => vi.unstubAllGlobals());

describe("AuditLogPage keyboard access to the scrollable table", () => {
  it("exposes the scroll region as a named region that is in the tab order", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([entry(1), entry(2)])));
    render(<AuditLogPage />);

    const region = await screen.findByRole("region", { name: "Audit log entries (scrollable table)" });
    expect(region).toHaveAttribute("tabindex", "0");
    expect(region).toContainElement(screen.getByRole("table"));
  });

  it("a keyboard user can Tab onto the scroll region", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([entry(1)])));
    render(<AuditLogPage />);
    const region = await screen.findByRole("region", { name: "Audit log entries (scrollable table)" });

    await userEvent.tab();

    expect(region).toHaveFocus();
  });

  it("has no detectable axe violations with entries on screen", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([entry(1), entry(2), entry(3)])));
    const { container } = render(<AuditLogPage />);
    await waitFor(() => expect(screen.getAllByText("RoleChanged").length).toBe(3));
    expect(await axe(container)).toHaveNoViolations();
  });

  it("the focused scroll region has a visible focus indicator using the design-system focus-ring token", () => {
    const css = readFileSync(path.join(process.cwd(), "src/pages/AuditLogPage.css"), "utf-8");
    const rule = /\.alv-audit-log-scroll:focus-visible\s*\{([^}]*)\}/.exec(css);
    expect(rule, "a :focus-visible rule for the scroll region").not.toBeNull();
    expect(rule![1]).toMatch(/outline:\s*2px solid var\(--color-focus-ring\)/);
  });
});
