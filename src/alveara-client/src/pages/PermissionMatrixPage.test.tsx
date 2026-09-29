import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { PermissionMatrixPage } from "./PermissionMatrixPage";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("PermissionMatrixPage", () => {
  it("renders the full role -> permission matrix", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse([
          { role: "Admin", permissions: ["ManageUsers", "ManageRoles"] },
          { role: "Billing", permissions: ["ManageBilling"] },
        ])
      )
    );

    render(<PermissionMatrixPage />);

    await waitFor(() => expect(screen.getByRole("columnheader", { name: "Admin" })).toBeInTheDocument());
    expect(screen.getByRole("columnheader", { name: "Billing" })).toBeInTheDocument();
    expect(screen.getByText("ManageUsers")).toBeInTheDocument();
    expect(screen.getByText("ManageBilling")).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("shows permission-denied, not a generic error, when the caller lacks ViewPermissionMatrix", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ error: "permission_denied", required: "ViewPermissionMatrix" }, 403)));

    render(<PermissionMatrixPage />);

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
