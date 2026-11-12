# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: stage-1.spec.ts >> F-037 after the front desk signs out on a patient page, the next person lands on the dashboard with no patient shown
- Location: chain\stage-1.spec.ts:127:1

# Error details

```
Error: expect(locator).toBeVisible() failed

Locator: getByText('Brian Baker').first()
Expected: visible
Timeout: 5000ms
Error: element(s) not found

Call log:
  - Expect "toBeVisible" getByText('Brian Baker').first() with timeout 5000ms
  - waiting for getByText('Brian Baker').first()

```

```yaml
- banner:
  - heading "Sign in" [level=1]
  - paragraph: Enter your username and password to access DentalKeep.
- text: Username
- textbox "Username"
- text: Password
- textbox "Password"
- button "Sign in"
- status
```

# Test source

```ts
  32  | async function signIn(page: Page, username: string, password: string): Promise<void> {
  33  |   await page.goto("/login");
  34  |   await page.getByLabel("Username").fill(username);
  35  |   await page.getByLabel("Password").fill(password);
  36  |   await page.getByRole("button", { name: "Sign in" }).click();
  37  | }
  38  | 
  39  | async function newSession(username: string, password = PASSWORD): Promise<{ context: BrowserContext; page: Page }> {
  40  |   const context = await browserRef.newContext();
  41  |   const page = await context.newPage();
  42  |   await signIn(page, username, password);
  43  |   await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  44  |   return { context, page };
  45  | }
  46  | 
  47  | /** Registers an account, then (unless `keepDisabled`) gives it a role and enables it, as the administrator. */
  48  | async function createUser(username: string, role: string, keepDisabled = false): Promise<string> {
  49  |   const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  50  |   expect(reg.status(), `register ${username}`).toBe(201);
  51  |   const id = (await reg.json()).id as string;
  52  |   if (!keepDisabled) {
  53  |     expect((await call(admin, "put", `/api/auth/${id}/role`, { role })).status).toBe(200);
  54  |     expect((await call(admin, "put", `/api/auth/${id}/enabled`, { enabled: true })).status).toBe(200);
  55  |   }
  56  |   return id;
  57  | }
  58  | 
  59  | test.beforeAll(async ({ browser }) => {
  60  |   browserRef = browser;
  61  |   adminContext = await browser.newContext();
  62  |   admin = await adminContext.newPage();
  63  | });
  64  | 
  65  | test.afterAll(async () => {
  66  |   await adminContext?.close();
  67  | });
  68  | 
  69  | test("1.01 a first visit to a protected page shows the plain sign-in page, never 'session expired' (F-007)", async ({ page }) => {
  70  |   await page.goto("/admin/users");
  71  |   await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  72  |   await expect(page).toHaveURL(/\/login$/);
  73  |   await expect(page.getByText(/your session expired/i)).toHaveCount(0);
  74  | });
  75  | 
  76  | test("1.02 a wrong password and an unknown name get the same plain refusal", async ({ page }) => {
  77  |   for (const name of [ADMIN, "no-such-user"]) {
  78  |     await signIn(page, name, "wrong-password");
  79  |     await expect(page.getByText("Invalid username or password.")).toBeVisible();
  80  |   }
  81  | });
  82  | 
  83  | test("1.03 the administrator signs in (with spaces around the name, F-151) and sees the admin menu", async () => {
  84  |   await signIn(admin, `  ${ADMIN} `, ADMIN_PASSWORD);
  85  |   await expect(admin.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  86  |   await expect(admin.getByRole("link", { name: "Security Administration" })).toBeVisible();
  87  |   ids.dentist = await createUser("chain-dentist", "Dentist");
  88  |   ids.front = await createUser("chain-front", "FrontDesk");
  89  |   ids.billing = await createUser("chain-billing", "Billing");
  90  |   ids.pending = await createUser("chain-pending", "Dentist", true);
  91  | });
  92  | 
  93  | test("1.04 an account that is still disabled cannot sign in", async ({ page }) => {
  94  |   await signIn(page, "chain-pending", PASSWORD);
  95  |   await expect(page.getByText(/this account has been disabled/i)).toBeVisible();
  96  | });
  97  | 
  98  | test("1.05 disabling billing1 refuses its sign-in; enabling it again lets it in", async ({ page }) => {
  99  |   expect((await call(admin, "put", `/api/auth/${ids.billing}/enabled`, { enabled: false })).status).toBe(200);
  100 |   await signIn(page, "chain-billing", PASSWORD);
  101 |   await expect(page.getByText(/this account has been disabled/i)).toBeVisible();
  102 |   expect((await call(admin, "put", `/api/auth/${ids.billing}/enabled`, { enabled: true })).status).toBe(200);
  103 |   await signIn(page, "chain-billing", PASSWORD);
  104 |   await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  105 | });
  106 | 
  107 | test("1.06 revoking a signed-in user's sessions signs that browser out on its next action, with the expiry notice", async () => {
  108 |   const front = await newSession("chain-front");
  109 |   expect((await call(admin, "post", `/api/auth/${ids.front}/revoke-sessions`)).status).toBe(200);
  110 |   await front.page.getByRole("link", { name: "Patients" }).click();
  111 |   await expect(front.page.getByText(/your session expired/i)).toBeVisible();
  112 |   await front.context.close();
  113 | });
  114 | 
  115 | test("2.01 two synthetic patients are registered by the front desk (minimal stage 2)", async () => {
  116 |   const front = await newSession("chain-front");
  117 |   for (const [key, firstName, lastName, dob] of [["brian", "Brian", "Baker", "1979-11-02"], ["alice", "Alice", "Anderson", "1985-03-14"]] as const) {
  118 |     const r = await call(front.page, "post", "/api/patients",
  119 |       { firstName, lastName, dateOfBirth: dob, sex: "Male", phone: "555-010-0102", addressLine1: "1 Main St", city: "Springfield", state: "IL", postalCode: "62701" },
  120 |       { "Idempotency-Key": `chain-stage1-${key}` });
  121 |     expect(r.status, `register ${firstName}`).toBe(201);
  122 |     patients[key] = r.body.id as string;
  123 |   }
  124 |   await front.context.close();
  125 | });
  126 | 
  127 | test("F-037 after the front desk signs out on a patient page, the next person lands on the dashboard with no patient shown", async () => {
  128 |   const shared = await browserRef.newContext();                                                           // one browser window used by two people
  129 |   const page = await shared.newPage();
  130 |   await signIn(page, "chain-front", PASSWORD);
  131 |   await page.goto(`/patients/${patients.brian}`);
> 132 |   await expect(page.getByText("Brian Baker").first()).toBeVisible();
      |                                                       ^ Error: expect(locator).toBeVisible() failed
  133 |   await page.getByRole("button", { name: "Sign out" }).click();
  134 |   await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  135 | 
  136 |   await signIn(page, ADMIN, ADMIN_PASSWORD);
  137 |   await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  138 |   await expect(page).toHaveURL(/\/$/);
  139 |   await expect(page.getByText("Brian Baker")).toHaveCount(0);
  140 |   await shared.close();
  141 | });
  142 | 
  143 | test("1.10 after sign-out the browser Back button shows the sign-in page and no patient information", async () => {
  144 |   const { context, page } = await newSession("chain-front");
  145 |   await page.goto(`/patients/${patients.alice}`);
  146 |   await expect(page.getByText("Alice Anderson").first()).toBeVisible();
  147 |   await page.getByRole("button", { name: "Sign out" }).click();
  148 |   await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  149 |   await page.goBack();
  150 |   await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  151 |   await expect(page.getByText("Alice Anderson")).toHaveCount(0);
  152 |   await expect(page.getByText("1985-03-14")).toHaveCount(0);
  153 |   await context.close();
  154 | });
  155 | 
  156 | test("F-021 inactivating a staff profile disables its login and ends its session; reactivating the profile does not re-enable it", async () => {
  157 |   const created = await call(admin, "post", "/api/config/staff", { displayName: "Dr. Chain Dentist", jobTitle: "Dentist", userAccountId: ids.dentist });
  158 |   expect(created.status).toBe(201);
  159 |   const dentist = await newSession("chain-dentist");
  160 | 
  161 |   const inactivated = await call(admin, "put", `/api/config/staff/${created.body.id}/active`, { isActive: false, rowVersion: created.body.rowVersion });
  162 |   expect(inactivated.status).toBe(200);
  163 | 
  164 |   await dentist.page.getByRole("link", { name: "Patients" }).click();                                   // the open session ends on its next action
  165 |   await expect(dentist.page.getByText(/your session expired/i)).toBeVisible();
  166 |   await signIn(dentist.page, "chain-dentist", PASSWORD);
  167 |   await expect(dentist.page.getByText(/this account has been disabled/i)).toBeVisible();
  168 | 
  169 |   const reactivated = await call(admin, "put", `/api/config/staff/${created.body.id}/active`, { isActive: true, rowVersion: inactivated.body.rowVersion });
  170 |   expect(reactivated.status).toBe(200);
  171 |   await signIn(dentist.page, "chain-dentist", PASSWORD);
  172 |   await expect(dentist.page.getByText(/this account has been disabled/i)).toBeVisible();          // still disabled: only an administrator re-enables it
  173 |   await dentist.context.close();
  174 | });
  175 | 
  176 | test("1.11 starting MFA enrolment without finishing leaves password sign-in unchanged", async () => {
  177 |   const { context, page } = await newSession("chain-billing");
  178 |   expect((await call(page, "post", "/api/auth/mfa/enroll", { currentPassword: PASSWORD })).status).toBe(200);
  179 |   await page.getByRole("button", { name: "Sign out" }).click();
  180 |   await signIn(page, "chain-billing", PASSWORD);
  181 |   await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();                         // no MFA prompt: enrolment was never confirmed
  182 |   await context.close();
  183 | });
  184 | 
  185 | test("13.05 a server outage keeps the signed-in screen and the typed form, and it recovers without signing in again (F-155, browser-level)", async () => {
  186 |   const { context, page } = await newSession("chain-front");
  187 |   await page.goto("/patients/register");
  188 |   await page.getByLabel("First name").fill("Typed During Outage");
  189 | 
  190 |   await page.route("**/api/**", (route) => route.fulfill({ status: 502, body: "Bad gateway" }));        // the API stops answering
  191 |   await page.evaluate(() => window.dispatchEvent(new Event("focus")));                                  // the session check runs and fails
  192 |   await page.waitForTimeout(1_000);
  193 |   await expect(page.getByRole("heading", { name: "Sign in" })).toHaveCount(0);
  194 |   await expect(page.getByLabel("First name")).toHaveValue("Typed During Outage");
  195 | 
  196 |   await page.unroute("**/api/**");                                                                       // the API is back
  197 |   await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  198 |   await expect(page.getByLabel("First name")).toHaveValue("Typed During Outage");
  199 |   await expect(page.getByText(/your session expired/i)).toHaveCount(0);
  200 |   await context.close();
  201 | });
  202 | 
  203 | test("1.12 a short per-user timeout warns first, then signs out with the expiry message (fixed expiry unchanged)", async () => {
  204 |   test.setTimeout(9 * 60_000);
  205 |   expect((await call(admin, "put", `/api/auth/${ids.front}/session-timeout`, { sessionTimeoutMinutes: 5 })).status).toBe(200);
  206 |   const { context, page } = await newSession("chain-front");
  207 |   await page.goto("/patients/register");
  208 |   await page.getByLabel("First name").fill("Unsaved");
  209 | 
  210 |   await expect(page.getByText(/your session will expire soon/i)).toBeVisible({ timeout: 4 * 60_000 });   // about 3 minutes in
  211 |   await expect(page.getByText(/your session expired/i)).toBeVisible({ timeout: 3 * 60_000 });           // at 5 minutes (checked every 15 s)
  212 |   await expect(page).toHaveURL(/\/login\?reason=expired/);
  213 |   await context.close();
  214 | });
  215 | 
```