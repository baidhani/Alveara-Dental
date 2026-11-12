import { describe, it, expect } from "vitest";
import { ApiError } from "../services/authApi";
import { classifyLoginFailure, destinationAfterSignIn } from "./loginOutcome";

describe("classifyLoginFailure (ALV-N020, F-014)", () => {
  it.each([
    ["the server's own 401", new ApiError(401, "invalid_credentials", "Invalid username or password."), "invalid-credentials"],
    ["a rate limit", new ApiError(429, "unknown_error", "x"), "rate-limited"],
    ["a disabled account", new ApiError(403, "account_disabled", "x"), "account-disabled"],
    ["a locked account", new ApiError(423, "account_locked", "x", { lockedUntilUtc: "2026-01-01T00:00:00Z" }), "locked-out"],
    ["a 502 from the proxy (API down)", new ApiError(502, "unknown_error", "x"), "server-unavailable"],
    ["a 503", new ApiError(503, "unknown_error", "x"), "server-unavailable"],
    ["a 504", new ApiError(504, "unknown_error", "x"), "server-unavailable"],
    ["a 500 with no API body (proxy, API down)", new ApiError(500, "unknown_error", "x"), "server-unavailable"],
    ["a 500 from the live API", new ApiError(500, "internal_error", "x"), "server-problem"],
    ["no connection", new TypeError("Failed to fetch"), "server-unavailable"],
    ["a timeout (abort)", Object.assign(new Error("aborted"), { name: "AbortError" }), "server-unavailable"],
    ["something else", new Error("odd"), "unexpected-error"],
  ])("%s", (_label, err, kind) => {
    expect(classifyLoginFailure(err).kind).toBe(kind);
  });

  it("never reports a server failure as wrong credentials", () => {
    for (const status of [500, 502, 503, 504]) expect(classifyLoginFailure(new ApiError(status, "unknown_error", "x")).kind).not.toBe("invalid-credentials");
  });
});

describe("destinationAfterSignIn (ALV-N020, F-037)", () => {
  const from = { pathname: "/patients/brian", search: "?tab=billing" };

  it("returns the same user to the page of their expired session", () => {
    expect(destinationAfterSignIn("frontdesk1", from, "frontdesk1")).toBe("/patients/brian?tab=billing");
  });

  it("sends a different user to the dashboard", () => {
    expect(destinationAfterSignIn("admin", from, "frontdesk1")).toBe("/");
  });

  it("sends anyone to the dashboard when no expired session was recorded", () => {
    expect(destinationAfterSignIn("admin", from, undefined)).toBe("/");
    expect(destinationAfterSignIn("admin", undefined, undefined)).toBe("/");
  });
});
