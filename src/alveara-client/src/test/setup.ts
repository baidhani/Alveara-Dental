import "@testing-library/jest-dom/vitest";
import { expect, vi, beforeEach } from "vitest";
import { toHaveNoViolations } from "jest-axe";

expect.extend(toHaveNoViolations);

// Default: pretend the local API is reachable and returns the real health
// payload shape, so tests unrelated to the disconnected state aren't flaky.
// A bare { ok: true } is not enough — useConnectionStatus also checks
// content-type and parses the JSON body, precisely to reject the kind of
// non-API 200 response that N001-R01-01 found. Individual tests override this.
beforeEach(() => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ status: "ok" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    )
  );
});
