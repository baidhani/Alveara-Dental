import "@testing-library/jest-dom/vitest";
import { expect, vi, beforeEach } from "vitest";
import { toHaveNoViolations } from "jest-axe";

expect.extend(toHaveNoViolations);

// Default: pretend the local API is reachable, so tests unrelated to the
// disconnected state aren't flaky. Individual tests can override this.
beforeEach(() => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue({ ok: true } as Response)
  );
});
