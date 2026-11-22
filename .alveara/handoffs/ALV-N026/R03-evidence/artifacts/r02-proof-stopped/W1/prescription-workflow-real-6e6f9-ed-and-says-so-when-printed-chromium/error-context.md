# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: prescription-workflow-real-backend.spec.ts >> CANCELLING needs a reason: without one nothing is recorded, with one the prescription is cancelled and says so when printed
- Location: e2e\prescription-workflow-real-backend.spec.ts:355:1

# Error details

```
Error: the PDF reached the user as a tab or a download

the PDF reached the user as a tab or a download

expect(received).toBeGreaterThan(expected)

Expected: > 0
Received:   0

Call Log:
- Timeout 5000ms exceeded while waiting on the predicate
```

# Page snapshot

```yaml
- generic [ref=f8e2]:
  - generic [ref=f8e3]:
    - link "Skip to main content" [ref=f8e4] [cursor=pointer]:
      - /url: "#alv-main-content"
    - complementary [ref=f8e5]:
      - generic [ref=f8e6]:
        - generic [ref=f8e7]: AD
        - generic [ref=f8e8]:
          - text: DentalKeep
          - generic [ref=f8e9]: Application
      - navigation "Primary navigation" [ref=f8e10]:
        - link "Dashboard" [ref=f8e11] [cursor=pointer]:
          - /url: /
        - link "Patients" [ref=f8e12] [cursor=pointer]:
          - /url: /patients
        - link "Calendar" [ref=f8e13] [cursor=pointer]:
          - /url: /calendar
        - link "Visit board" [ref=f8e14] [cursor=pointer]:
          - /url: /flow
        - link "Schedule" [ref=f8e15] [cursor=pointer]:
          - /url: /schedule
        - link "Procedures & fees" [ref=f8e16] [cursor=pointer]:
          - /url: /procedures
        - link "Work queue" [ref=f8e17] [cursor=pointer]:
          - /url: /work-queue
        - link "My follow-ups" [ref=f8e18] [cursor=pointer]:
          - /url: /follow-ups
        - link "Referrals & labs" [ref=f8e19] [cursor=pointer]:
          - /url: /external-cases
        - link "Messages" [ref=f8e20] [cursor=pointer]:
          - /url: /messages
        - link "Component Showcase" [ref=f8e21] [cursor=pointer]:
          - /url: /showcase
        - link "Reports" [ref=f8e22] [cursor=pointer]:
          - /url: /reports
        - link "Multi-Factor Authentication" [ref=f8e23] [cursor=pointer]:
          - /url: /settings/mfa
      - generic [ref=f8e24]:
        - link "Notifications, 0 unread" [ref=f8e25] [cursor=pointer]:
          - /url: /notifications
          - generic [aria-hidden] [ref=f8e26]: Notifications
        - generic "Account" [ref=f8e27]:
          - generic [ref=f8e28]:
            - generic [ref=f8e29]: dentist-1795372705425
            - generic [ref=f8e30]: Dentist
          - button "Sign out" [ref=f8e31] [cursor=pointer]
        - button "☀ Light" [ref=f8e32] [cursor=pointer]
    - generic [ref=f8e33]:
      - region "Patient context" [ref=f8e34]:
        - generic [ref=f8e35]:
          - generic [ref=f8e36]:
            - generic [ref=f8e37]: Ann Alder
            - generic [ref=f8e38]: MRN 63930969510
            - generic [ref=f8e39]: DOB 1985-03-09 (41 y)
            - generic [ref=f8e40]: Female
            - generic [ref=f8e41]: 555-010-0100
          - generic [ref=f8e42]:
            - link "Open workspace" [ref=f8e43] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c
            - button "Close patient" [ref=f8e44] [cursor=pointer]
          - region "Patient safety" [ref=f8e45]:
            - paragraph [ref=f8e46]:
              - strong [ref=f8e47]: "Patient safety:"
              - text: "1 active alert (highest: High) · 2 active allergies · 1 current medication"
            - list [ref=f8e48]:
              - listitem [ref=f8e49]: 1 alert you have not acknowledged yet.
              - listitem [ref=f8e50]: Medications have changed or were never confirmed as current.
              - listitem [ref=f8e51]: Medical history has not been reviewed. Nothing listed here does not mean none.
            - paragraph [ref=f8e52]:
              - link "Open safety details" [ref=f8e53] [cursor=pointer]:
                - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/safety
      - main [ref=f8e54]:
        - generic [ref=f8e55]:
          - generic [ref=f8e57]:
            - heading "Patient workspace" [level=1] [ref=f8e58]
            - paragraph [ref=f8e59]: Everything about this patient in one place. The patient shown above stays selected while you move between sections.
          - navigation "Patient workspace sections" [ref=f8e60]:
            - link "Details" [ref=f8e61] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c
            - link "Household & guarantor" [ref=f8e62] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/household
            - link "History" [ref=f8e63] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/history
            - link "Forms" [ref=f8e64] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/forms
            - link "Documents" [ref=f8e65] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/documents
            - link "Clinical" [ref=f8e66] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/clinical
            - link "Safety" [ref=f8e67] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/safety
            - link "Odontogram" [ref=f8e68] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/odontogram
            - link "Periodontal" [ref=f8e69] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/periodontal
            - link "Diagnoses" [ref=f8e70] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/diagnoses
            - link "Treatment plan" [ref=f8e71] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/treatment-plan
            - link "Prescriptions" [ref=f8e72] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/prescriptions
            - link "Prescription composer" [ref=f8e73] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/prescription-composer
            - link "Follow-ups" [ref=f8e74] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/follow-ups
            - link "Referrals & labs" [ref=f8e75] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/external-cases
            - link "Billing" [ref=f8e76] [cursor=pointer]:
              - /url: /patients/cb8bc5c3-7f63-4588-8133-8b45a5f8c54c/billing
          - region [ref=f8e77]:
            - heading "Prescription composer" [level=2] [ref=f8e78]
            - note [ref=f8e79]:
              - strong [ref=f8e80]: "Not configured:"
              - text: "Drug interaction checking and clinical dose-appropriateness checking are not configured in this application. Nothing here checks them: a prescription that passes every check on this screen can still interact with another medication or be the wrong dose."
            - status [ref=f8e81]: Nothing was printed. PDFs cannot be produced right now. The rest of the application is unaffected; ask the practice administrator to check System Status.
            - heading "Drafts" [level=3] [ref=f8e82]
            - paragraph [ref=f8e83]: No draft is open for this patient.
            - button "New prescription draft" [ref=f8e84]
            - heading "Finalized prescriptions" [level=3] [ref=f8e85]
            - list "Finalized prescriptions" [ref=f8e86]:
              - listitem [ref=f8e87]:
                - heading "Amoxicillin" [level=4] [ref=f8e88]
                - paragraph [ref=f8e89]: 250 mg · Three times daily
                - paragraph [ref=f8e90]:
                  - strong [ref=f8e91]: "Status:"
                  - text: Active
                - paragraph [ref=f8e92]: Take one with food until finished.
                - paragraph [ref=f8e93]: Quantity 21 capsule · Refills 0
                - paragraph [ref=f8e94]: Prescribed by Dr. Dana Dentist 1795372705425 on 11/22/2026, 12:38:45 PM.
                - paragraph [ref=f8e95]: No recorded allergy matched this medication's name. That is a name comparison, not a clearance.
                - group [ref=f8e96]:
                  - generic "History" [ref=f8e97]
                - generic [ref=f8e98]:
                  - button "Print Amoxicillin (PDF)" [ref=f8e99]
                  - button "Record copy of Amoxicillin (PDF)" [ref=f8e100]
                  - button "Amend Amoxicillin" [ref=f8e101]
                  - button "Cancel Amoxicillin" [ref=f8e102]
                  - button "Discontinue Amoxicillin" [ref=f8e103]
              - listitem [ref=f8e104]:
                - heading "Penicillin V Potassium" [level=4] [ref=f8e105]
                - paragraph [ref=f8e106]: 500 mg · Four times daily
                - paragraph [ref=f8e107]:
                  - strong [ref=f8e108]: "Status:"
                  - text: "Cancelled: it should not be taken or dispensed."
                - paragraph [ref=f8e109]: Take one with food until finished.
                - paragraph [ref=f8e110]: Quantity 40 capsule · Refills 0
                - paragraph [ref=f8e111]: Prescribed by Dr. Dana Dentist 1795372705425 on 11/22/2026, 12:38:43 PM.
                - paragraph [ref=f8e112]: The medication matched a recorded allergy (Penicillin); the prescriber reviewed it.
                - paragraph [ref=f8e113]:
                  - strong [ref=f8e114]: "Prescriber's review:"
                  - text: Childhood rash, tolerated since; discussed with the patient today.
                - group [ref=f8e115]:
                  - generic "History" [ref=f8e116]
                - button "Record copy of Penicillin V Potassium (PDF)" [active] [ref=f8e118]
              - listitem [ref=f8e119]:
                - heading "Amoxicillin" [level=4] [ref=f8e120]
                - paragraph [ref=f8e121]: 500 mg · Three times daily
                - paragraph [ref=f8e122]:
                  - strong [ref=f8e123]: "Status:"
                  - text: "Superseded: it was replaced by an amendment."
                - paragraph [ref=f8e124]: Take one with food until finished.
                - paragraph [ref=f8e125]: Quantity 21 capsule · Refills 0
                - paragraph [ref=f8e126]: Prescribed by Dr. Dana Dentist 1795372705425 on 11/22/2026, 12:38:42 PM.
                - paragraph [ref=f8e127]: No recorded allergy matched this medication's name. That is a name comparison, not a clearance.
                - group [ref=f8e128]:
                  - generic "History" [ref=f8e129]
                - button "Record copy of Amoxicillin (PDF)" [ref=f8e131]
  - status
```

# Test source

```ts
  9   | // ALV-016-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
  10  | // Excluded from the default mocked run (playwright.config.ts) and run with playwright.prescription-workflow.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
  11  | // A dentist writes a prescription as a draft (saved incomplete, kept across a reload), is told what is missing, finalizes it through a confirmation, and it is logged with who and when; a medication that matches a
  12  | // recorded allergy waits for the dentist's own words; a patient whose allergies were never reviewed gets the same; an allergy added after the draft was started is shown and must be acknowledged; an amendment
  13  | // replaces a prescription without editing it; a cancellation needs a reason; a print is issued on the server and opened as the server's PDF (ALV-N026: never the application page through
  14  | // window.print), and an ended prescription only as a record copy; when the log is unavailable nothing is finalized and the same click works once it is back;
  15  | // a hygienist reads and prints but cannot change anything; and nothing can be edited or deleted. Nothing on any screen claims interaction checking or dose appropriateness.
  16  | 
  17  | test.describe.configure({ mode: "serial" });
  18  | 
  19  | const OUT = process.env.PRESCRIPTION_WORKFLOW_E2E_OUT ?? path.join(process.cwd(), "prescription-workflow-e2e-out");
  20  | mkdirSync(OUT, { recursive: true });
  21  | const evidence: Record<string, unknown> = { axe: {} };
  22  | const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });
  23  | 
  24  | const PASSWORD = "plan-pass-1!";
  25  | type Ctx = { context: BrowserContext; page: Page };
  26  | const sessions: Record<string, Ctx> = {};
  27  | let adminContext: BrowserContext;
  28  | let admin: Page;
  29  | let stamp = 0;
  30  | const ids: Record<string, string> = {};
  31  | const DENTIST_NAME = "Dr. Dana Dentist";
  32  | 
  33  | const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
  34  | 
  35  | async function api(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  36  |   const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  37  |   const response = await page.request[method](url, method === "get" || method === "delete" ? { headers: h } : { headers: h, data });
  38  |   return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
  39  | }
  40  | 
  41  | function sql(statement: string) {
  42  |   const db = process.env.E2E_DB_NAME;
  43  |   if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the failed-log and immutability demonstrations need it)");
  44  |   execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
  45  | }
  46  | 
  47  | async function createUser(role: string, username: string): Promise<string> {
  48  |   const token = await csrf(admin);
  49  |   const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  50  |   expect(reg.ok(), `register ${role}`).toBeTruthy();
  51  |   const { id } = await reg.json();
  52  |   expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  53  |   expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  54  |   return id as string;
  55  | }
  56  | 
  57  | async function signIn(page: Page, username: string, password: string) {
  58  |   await page.goto("/login");
  59  |   await page.getByLabel("Username").fill(username);
  60  |   await page.getByLabel("Password").fill(password);
  61  |   await page.getByRole("button", { name: "Sign in" }).click();
  62  |   await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  63  | }
  64  | 
  65  | async function setTheme(page: Page, theme: "light" | "dark") {
  66  |   await page.getByRole("button", { name: /Light|Dark/ }).waitFor();
  67  |   if ((await page.locator("html").getAttribute("data-theme")) !== theme) await page.getByRole("button", { name: /Light|Dark/ }).click();
  68  |   await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
  69  |   await page.waitForTimeout(300);
  70  | }
  71  | 
  72  | async function scanBothThemes(page: Page, label: string) {
  73  |   for (const theme of ["light", "dark"] as const) {
  74  |     await setTheme(page, theme);
  75  |     const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
  76  |     const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
  77  |     (evidence.axe as Record<string, unknown>)[`${label}-${theme}`] = { blocking: blocking.length, violations: results.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length })) };
  78  |     expect(blocking.map((v) => `${v.id}:${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`), `${label} (${theme}): critical/serious axe violations`).toEqual([]);
  79  |   }
  80  |   await setTheme(page, "light");
  81  | }
  82  | 
  83  | const statusLine = (page: Page) => page.locator("p.alv-clinical__status").first();
  84  | const composer = (page: Page) => page.getByRole("form", { name: "Prescription composer" });
  85  | const confirmation = (page: Page) => page.getByRole("region", { name: "Confirm finalization" });
  86  | const safetyPanel = (page: Page) => page.getByRole("complementary", { name: "Patient safety" });
  87  | const record = (page: Page, status: string, medication: string) => page.locator(`li.alv-rx__item[data-status="${status}"]`).filter({ hasText: medication });
  88  | const recordsOf = async (page: Page, patient: string) => (await api(page, "get", `/api/patients/${patient}/prescription-records`)).body.prescriptions as any[];
  89  | const auditOf = async (type: string) => ((await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[]).filter((e) => e.eventType === type);
  90  | 
  91  | /**
  92  |  * ALV-N026 R02 (review item 2): clicks a print button and returns what the browser did. The button must issue on the server (POST .../issues) and then fetch the issue's PDF; the PDF must reach
  93  |  * the user as a new tab or a download (either is accepted - the implemented flow opens a tab), and the application page must never call window.print().
  94  |  */
  95  | async function pdfFromClick(page: Page, button: Locator) {
  96  |   await page.evaluate(() => { (window as any).__printed = 0; window.print = () => { (window as any).__printed++; }; });
  97  |   const context = page.context();
  98  |   const before = new Set(context.pages());
  99  |   let downloads = 0;
  100 |   const onDownload = () => { downloads++; };
  101 |   page.on("download", onDownload);
  102 |   try {
  103 |     const issued = page.waitForResponse((r) => r.request().method() === "POST" && /\/api\/prescriptions\/[0-9a-f-]+\/issues$/.test(new URL(r.url()).pathname));
  104 |     const pdf = page.waitForResponse((r) => r.request().method() === "GET" && /\/api\/prescription-issues\/[0-9a-f-]+\/pdf$/.test(new URL(r.url()).pathname));
  105 |     await button.click();
  106 |     const issue = await issued;
  107 |     const response = await pdf;
  108 |     const bytes = await response.body();
> 109 |     await expect.poll(() => context.pages().filter((p) => !before.has(p)).length + downloads, { message: "the PDF reached the user as a tab or a download" }).toBeGreaterThan(0);
      |                                                                                                                                                               ^ Error: the PDF reached the user as a tab or a download
  110 |     const tabs = context.pages().filter((p) => !before.has(p)).length;
  111 |     const printed = await page.evaluate(() => (window as any).__printed as number);
  112 |     for (const p of context.pages()) if (!before.has(p)) await p.close();
  113 |     return { issueStatus: issue.status(), issueId: ((await issue.json()) as { id: string }).id, status: response.status(), contentType: response.headers()["content-type"] ?? "", bytes, tabs, downloads, printed, pageUrl: page.url() };
  114 |   } finally {
  115 |     page.off("download", onDownload);
  116 |   }
  117 | }
  118 | 
  119 | /** The safe server-PDF result: a real PDF from the server, no application page printed, and no application address or raw server detail inside it. */
  120 | function expectServerPdf(result: Awaited<ReturnType<typeof pdfFromClick>>) {
  121 |   expect(result.issueStatus).toBe(201);
  122 |   expect(result.status).toBe(200);
  123 |   expect(result.contentType).toMatch(/^application\/pdf/);
  124 |   expect(result.bytes.subarray(0, 5).toString("latin1")).toBe("%PDF-");
  125 |   expect(result.printed).toBe(0);                                                                                 // the application page was never printed
  126 |   const raw = result.bytes.toString("latin1");
  127 |   expect(raw).not.toContain(new URL(result.pageUrl).host);                                                        // no app address
  128 |   expect(raw).not.toMatch(/localhost|\/api\/|Exception|stack trace/i);
  129 | }
  130 | 
  131 | async function gotoComposer(page: Page, patient = ids.ann) {
  132 |   await page.goto(`/patients/${patient}/prescription-composer`);
  133 |   await expect(page.getByRole("heading", { name: "Prescription composer", exact: true })).toBeVisible();
  134 | }
  135 | 
  136 | type Entry = { medication: string; dose?: string; unit?: string; frequency?: string; instructions?: string; quantity?: string; quantityUnit?: string; refills?: string };
  137 | async function fill(page: Page, e: Entry, complete = true) {
  138 |   const c = composer(page);
  139 |   await c.getByLabel("Medication").fill(e.medication);
  140 |   await c.getByLabel("Medication").press("Tab");
  141 |   await c.getByLabel("Dose", { exact: true }).fill(e.dose ?? "500");
  142 |   await c.getByLabel("Unit", { exact: true }).selectOption(e.unit ?? "mg");
  143 |   await c.getByLabel("How often").selectOption(e.frequency ?? "Three times daily");
  144 |   if (!complete) return;
  145 |   await c.getByLabel("Instructions for the patient").fill(e.instructions ?? "Take one with food until finished.");
  146 |   await c.getByLabel("Quantity", { exact: true }).fill(e.quantity ?? "21");
  147 |   await c.getByLabel("Quantity unit").selectOption(e.quantityUnit ?? "capsule");
  148 |   await c.getByLabel("Refills").fill(e.refills ?? "0");
  149 | }
  150 | 
  151 | async function newDraft(page: Page) {
  152 |   await page.getByRole("button", { name: "New prescription draft" }).click();
  153 |   await expect(composer(page)).toBeVisible();
  154 | }
  155 | 
  156 | /** Finalizes the entry in the composer through the confirmation. */
  157 | async function finalize(page: Page, medication: string) {
  158 |   await composer(page).getByRole("button", { name: "Finalize…" }).click();
  159 |   await expect(confirmation(page)).toBeVisible();
  160 |   await confirmation(page).getByRole("button", { name: "Finalize prescription" }).click();
  161 |   await expect(statusLine(page)).toContainText(`${medication} finalized and logged.`);
  162 | }
  163 | 
  164 | test.beforeAll(async ({ browser }: { browser: Browser }) => {
  165 |   stamp = Date.now();
  166 |   adminContext = await browser.newContext();
  167 |   admin = await adminContext.newPage();
  168 |   const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  169 |   expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  170 |   await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  171 |   for (const role of ["Dentist", "Hygienist", "FrontDesk"]) {
  172 |     const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
  173 |     if (role === "Dentist") {
  174 |       ids.dentistUser = userId;
  175 |       expect((await api(admin, "post", "/api/config/staff", { displayName: `${DENTIST_NAME} ${stamp}`, userAccountId: userId })).status, "staff profile for the dentist").toBe(201);
  176 |     }
  177 |     const context = await browser.newContext();
  178 |     const page = await context.newPage();
  179 |     await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
  180 |     sessions[role] = { context, page };
  181 |   }
  182 |   evidence.stamp = stamp;
  183 | });
  184 | 
  185 | test.afterAll(async () => {
  186 |   writeFileSync(path.join(OUT, "prescription-workflow-evidence.json"), JSON.stringify(evidence, null, 2));
  187 |   for (const s of Object.values(sessions)) await s.context.close();
  188 |   await adminContext?.close();
  189 | });
  190 | 
  191 | test("SETUP: Ann has her allergies reviewed (penicillin and latex); Bo's allergies were never reviewed", async () => {
  192 |   const front = sessions.FrontDesk.page;
  193 |   const dentist = sessions.Dentist.page;
  194 |   const patient = async (firstName: string, lastName: string, key: string, dob: string, phone: string, address: string) => {
  195 |     const r = await api(front, "post", "/api/patients", { firstName, lastName, dateOfBirth: dob, sex: "Female", phone, addressLine1: address, city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `${key}-${stamp}` });
  196 |     expect(r.status).toBe(201);
  197 |     return r.body.id as string;
  198 |   };
  199 |   ids.ann = await patient("Ann", "Alder", "rxw-ann", "1985-03-09", "555-010-0100", "1 Main St");
  200 |   ids.bo = await patient("Bo", "Brandt", "rxw-bo", "1972-11-21", "555-010-0222", "9 Oak Ave");
  201 |   for (const [name, reaction, severity] of [["Penicillin", "Hives", "Severe"], ["Latex", "Rash", "Mild"]]) {
  202 |     expect((await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Allergy", name, reaction, severity })).status, name).toBe(200);
  203 |   }
  204 |   expect((await api(dentist, "put", `/api/patients/${ids.ann}/clinical-record/sections/Allergy/review`, { state: "Reviewed" })).status).toBe(200);
  205 |   // what the shared safety framework (ALV-N011) knows besides allergies: a current medication and an active alert, shown while prescribing
  206 |   expect((await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Medication", name: "Lisinopril", dose: "10 mg", frequency: "daily" })).status).toBe(200);
  207 |   expect((await api(dentist, "post", `/api/patients/${ids.ann}/safety/alerts`, { category: "Condition", title: "Prosthetic heart valve", severity: "High", sourceNote: "Reported by the patient" })).status).toBe(200);
  208 | });
  209 | 
```