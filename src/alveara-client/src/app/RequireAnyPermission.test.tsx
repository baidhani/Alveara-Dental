import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequireAnyPermission } from "./RouteGuards";

// ALV-N018: the account screens open for a holder of any one of the account permissions and show the permission-denied state otherwise.
let held = new Set<string>();
vi.mock("../contexts/AuthContext", () => ({ useAuth: () => ({ hasPermission: (p: string) => held.has(p) }) }));

describe("RequireAnyPermission", () => {
  it("renders its children when the caller holds any one of the permissions", () => {
    held = new Set(["RevokeSessions"]);
    render(<RequireAnyPermission permissions={["ManageUsers", "ManageAccountStatus", "RevokeSessions"]}><p>Accounts</p></RequireAnyPermission>);
    expect(screen.getByText("Accounts")).toBeInTheDocument();
  });

  it("shows the permission-denied state, naming the permission it was told to show, when the caller holds none", () => {
    held = new Set(["ViewSchedule"]);
    render(<RequireAnyPermission permissions={["ManageUsers", "ManageAccountStatus", "RevokeSessions"]} shown="ManageUsers"><p>Accounts</p></RequireAnyPermission>);
    expect(screen.queryByText("Accounts")).toBeNull();
    expect(screen.getByText(/don't have permission/i)).toBeInTheDocument();
    expect(screen.getByText(/"ManageUsers"/)).toBeInTheDocument();
  });
});
