import { test, expect } from "@playwright/test";

/** Gate A A9 supporting measurement: wall-clock latency of the real recovery-key generation endpoint (RSA-3072 OpenPGP key + generated passphrase). */
test("recovery key generation latency against the real API", async ({ request }) => {
  const username = `gate-timing-${Date.now()}`;
  const password = "gate-timing-password-1!";
  expect((await request.post("/api/auth/bootstrap-admin", { data: { username, password, secret: "e2e-real-backend-secret" } })).ok()).toBeTruthy();
  expect((await request.post("/api/auth/login", { data: { username, password } })).ok()).toBeTruthy();
  const { token } = await (await request.get("/api/auth/csrf-token")).json();
  const times: number[] = [];
  for (let i = 0; i < 6; i++) {
    const t0 = Date.now();
    const res = await request.post("/api/backup/recovery-key", { headers: { "X-CSRF-Token": token }, data: { currentPassword: password, replaceExisting: i > 0 }, timeout: 120_000 });
    times.push(Date.now() - t0);
    expect(res.ok(), `attempt ${i}: ${res.status()}`).toBeTruthy();
  }
  console.log("KEYGEN_MS " + JSON.stringify(times));
  expect(times.length).toBe(6);
});
