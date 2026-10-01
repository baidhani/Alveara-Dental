import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConcurrencyConflictBanner } from "./ConcurrencyConflictBanner";
import { ApiError, isConcurrencyConflict } from "../services/authApi";
import type { ConcurrencyConflictProblem } from "../services/authApi";

/**
 * ALV-002-C01 R02 (review finding ALV-002-C01-R01-02): the reusable stale-edit/conflict
 * presentation pattern, demonstrated directly since no domain mutable-record edit flow exists yet
 * to wire it into (see AuditLogPage.tsx's own doc comment and R01.md's "Review-relevant
 * limitations" - a clinical/financial edit screen is explicitly out of this story's scope).
 */
describe("ConcurrencyConflictBanner (ALV-002-C01)", () => {
  const problem: ConcurrencyConflictProblem = {
    error: "concurrency_conflict",
    entityType: "StaffProfile",
    entityId: "11111111-1111-1111-1111-111111111111",
  };

  it("names what happened and offers a reload path, without silently discarding the caller's edit", () => {
    const onReload = vi.fn();
    render(<ConcurrencyConflictBanner problem={problem} onReload={onReload} />);

    expect(screen.getByRole("alert")).toHaveTextContent(/someone else changed this/i);
    expect(screen.getByText(/your changes were not saved/i)).toBeInTheDocument();
    expect(screen.getByText(/staffprofile/i)).toBeInTheDocument();
  });

  it("invokes onReload (never a silent resubmit) when the caller chooses to recover", async () => {
    const onReload = vi.fn();
    render(<ConcurrencyConflictBanner problem={problem} onReload={onReload} />);

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" }));

    expect(onReload).toHaveBeenCalledTimes(1);
  });
});

describe("isConcurrencyConflict (ALV-002-C01)", () => {
  it("recognizes a 409 carrying the shared concurrency-conflict shape", () => {
    const err = new ApiError(409, "concurrency_conflict", "conflict", {
      error: "concurrency_conflict",
      entityType: "StaffProfile",
      entityId: "11111111-1111-1111-1111-111111111111",
    });

    expect(isConcurrencyConflict(err)).toBe(true);
    if (isConcurrencyConflict(err)) {
      // Type-narrowed: err.body is ConcurrencyConflictProblem here, not just Record<string, unknown>.
      expect(err.body.entityType).toBe("StaffProfile");
    }
  });

  it("rejects an unrelated 409, a different error shape, or a non-ApiError", () => {
    expect(isConcurrencyConflict(new ApiError(409, "some_other_conflict", "x", { error: "some_other_conflict" }))).toBe(false);
    expect(isConcurrencyConflict(new ApiError(403, "permission_denied", "x", {}))).toBe(false);
    expect(isConcurrencyConflict(new Error("plain error"))).toBe(false);
    expect(isConcurrencyConflict(null)).toBe(false);
  });
});
