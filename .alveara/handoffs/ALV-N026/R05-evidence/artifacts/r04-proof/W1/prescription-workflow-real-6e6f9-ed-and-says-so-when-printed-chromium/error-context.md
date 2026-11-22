# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: prescription-workflow-real-backend.spec.ts >> CANCELLING needs a reason: without one nothing is recorded, with one the prescription is cancelled and says so when printed
- Location: e2e\prescription-workflow-real-backend.spec.ts:368:1

# Error details

```
Error: expect(received).toBe(expected) // Object.is equality

Expected: "%PDF-"
Received: ""
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
            - generic [ref=f8e29]: dentist-1795380910705
            - generic [ref=f8e30]: Dentist
          - button "Sign out" [ref=f8e31] [cursor=pointer]
        - button "☀ Light" [ref=f8e32] [cursor=pointer]
    - generic [ref=f8e33]:
      - region "Patient context" [ref=f8e34]:
        - generic [ref=f8e35]:
          - generic [ref=f8e36]:
            - generic [ref=f8e37]: Ann Alder
            - generic [ref=f8e38]: MRN 63930977716
            - generic [ref=f8e39]: DOB 1985-03-09 (41 y)
            - generic [ref=f8e40]: Female
            - generic [ref=f8e41]: 555-010-0100
          - generic [ref=f8e42]:
            - link "Open workspace" [ref=f8e43] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348
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
                - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/safety
      - main [ref=f8e54]:
        - generic [ref=f8e55]:
          - generic [ref=f8e57]:
            - heading "Patient workspace" [level=1] [ref=f8e58]
            - paragraph [ref=f8e59]: Everything about this patient in one place. The patient shown above stays selected while you move between sections.
          - navigation "Patient workspace sections" [ref=f8e60]:
            - link "Details" [ref=f8e61] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348
            - link "Household & guarantor" [ref=f8e62] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/household
            - link "History" [ref=f8e63] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/history
            - link "Forms" [ref=f8e64] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/forms
            - link "Documents" [ref=f8e65] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/documents
            - link "Clinical" [ref=f8e66] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/clinical
            - link "Safety" [ref=f8e67] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/safety
            - link "Odontogram" [ref=f8e68] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/odontogram
            - link "Periodontal" [ref=f8e69] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/periodontal
            - link "Diagnoses" [ref=f8e70] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/diagnoses
            - link "Treatment plan" [ref=f8e71] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/treatment-plan
            - link "Prescriptions" [ref=f8e72] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/prescriptions
            - link "Prescription composer" [ref=f8e73] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/prescription-composer
            - link "Follow-ups" [ref=f8e74] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/follow-ups
            - link "Referrals & labs" [ref=f8e75] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/external-cases
            - link "Billing" [ref=f8e76] [cursor=pointer]:
              - /url: /patients/ed97546a-ef56-4a3e-ae3b-e92d410b1348/billing
          - region [ref=f8e77]:
            - heading "Prescription composer" [level=2] [ref=f8e78]
            - note [ref=f8e79]:
              - strong [ref=f8e80]: "Not configured:"
              - text: "Drug interaction checking and clinical dose-appropriateness checking are not configured in this application. Nothing here checks them: a prescription that passes every check on this screen can still interact with another medication or be the wrong dose."
            - status [ref=f8e81]: A record copy dated today was issued and opened as a PDF.
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
                - paragraph [ref=f8e94]: Prescribed by Dr. Dana Dentist 1795380910705 on 11/22/2026, 2:55:33 PM.
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
                - paragraph [ref=f8e111]: Prescribed by Dr. Dana Dentist 1795380910705 on 11/22/2026, 2:55:30 PM.
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
                - paragraph [ref=f8e126]: Prescribed by Dr. Dana Dentist 1795380910705 on 11/22/2026, 2:55:27 PM.
                - paragraph [ref=f8e127]: No recorded allergy matched this medication's name. That is a name comparison, not a clearance.
                - group [ref=f8e128]:
                  - generic "History" [ref=f8e129]
                - button "Record copy of Amoxicillin (PDF)" [ref=f8e131]
  - status
```

# Test source

```ts
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
  92  |  * ALV-N026 R02 (review item 2), corrected in R03: clicks a print button and returns what the browser did. The button must issue on the server (POST .../issues) and then fetch the issue's
  93  |  * PDF; the PDF must reach the user as a new tab or a download (either is accepted - the implemented flow opens a tab and sends it to the PDF), and the application page must never call
  94  |  * window.print(). R02's version counted only tabs still open and downloads on the application page, and so missed a headless browser that turns the new tab's navigation to a PDF into a
  95  |  * download on that tab (and may close it): every tab opened during the click is now counted even if it closed, downloads are heard on every page, and where the PDF arrived is recorded.
  96  |  */
  97  | async function pdfFromClick(page: Page, button: Locator) {
  98  |   await page.evaluate(() => { (window as any).__printed = 0; window.print = () => { (window as any).__printed++; }; });
  99  |   const context = page.context();
  100 |   const before = new Set(context.pages());
  101 |   const arrivals: string[] = [];
  102 |   const opened: Page[] = [];
  103 |   const onAppDownload = () => { arrivals.push("download on the application page"); };
  104 |   const onPage = (tab: Page) => {
  105 |     opened.push(tab);
  106 |     arrivals.push("new tab");
  107 |     tab.on("download", () => { arrivals.push("download on the new tab"); });
  108 |   };
  109 |   page.on("download", onAppDownload);
  110 |   context.on("page", onPage);
  111 |   try {
  112 |     const issued = page.waitForResponse((r) => r.request().method() === "POST" && /\/api\/prescriptions\/[0-9a-f-]+\/issues$/.test(new URL(r.url()).pathname));
  113 |     const pdf = page.waitForResponse((r) => r.request().method() === "GET" && /\/api\/prescription-issues\/[0-9a-f-]+\/pdf$/.test(new URL(r.url()).pathname));
  114 |     await button.click();
  115 |     const issue = await issued;
  116 |     const response = await pdf;
  117 |     const bytes = await response.body();
  118 |     await expect.poll(() => arrivals.length, { message: "the PDF reached the user as a tab or a download" }).toBeGreaterThan(0);
  119 |     await page.waitForTimeout(500);                                                                                // let a tab's download, if any, be heard before recording how it arrived
  120 |     const printed = await page.evaluate(() => (window as any).__printed as number);
  121 |     for (const tab of opened) if (!tab.isClosed() && !before.has(tab)) await tab.close();
  122 |     return {
  123 |       issueStatus: issue.status(), issueId: ((await issue.json()) as { id: string }).id, status: response.status(), contentType: response.headers()["content-type"] ?? "", bytes,
  124 |       tabs: opened.length, downloads: arrivals.filter((a) => a.startsWith("download")).length, arrivals: [...arrivals], printed, pageUrl: page.url(),
  125 |     };
  126 |   } finally {
  127 |     page.off("download", onAppDownload);
  128 |     context.off("page", onPage);
  129 |   }
  130 | }
  131 | 
  132 | /** The safe server-PDF result: a real PDF from the server, no application page printed, and no application address or raw server detail inside it. */
  133 | function expectServerPdf(result: Awaited<ReturnType<typeof pdfFromClick>>) {
  134 |   expect(result.issueStatus).toBe(201);
  135 |   expect(result.status).toBe(200);
  136 |   expect(result.contentType).toMatch(/^application\/pdf/);
> 137 |   expect(result.bytes.subarray(0, 5).toString("latin1")).toBe("%PDF-");
      |                                                          ^ Error: expect(received).toBe(expected) // Object.is equality
  138 |   expect(result.printed).toBe(0);                                                                                 // the application page was never printed
  139 |   const raw = result.bytes.toString("latin1");
  140 |   expect(raw).not.toContain(new URL(result.pageUrl).host);                                                        // no app address
  141 |   expect(raw).not.toMatch(/localhost|\/api\/|Exception|stack trace/i);
  142 | }
  143 | 
  144 | async function gotoComposer(page: Page, patient = ids.ann) {
  145 |   await page.goto(`/patients/${patient}/prescription-composer`);
  146 |   await expect(page.getByRole("heading", { name: "Prescription composer", exact: true })).toBeVisible();
  147 | }
  148 | 
  149 | type Entry = { medication: string; dose?: string; unit?: string; frequency?: string; instructions?: string; quantity?: string; quantityUnit?: string; refills?: string };
  150 | async function fill(page: Page, e: Entry, complete = true) {
  151 |   const c = composer(page);
  152 |   await c.getByLabel("Medication").fill(e.medication);
  153 |   await c.getByLabel("Medication").press("Tab");
  154 |   await c.getByLabel("Dose", { exact: true }).fill(e.dose ?? "500");
  155 |   await c.getByLabel("Unit", { exact: true }).selectOption(e.unit ?? "mg");
  156 |   await c.getByLabel("How often").selectOption(e.frequency ?? "Three times daily");
  157 |   if (!complete) return;
  158 |   await c.getByLabel("Instructions for the patient").fill(e.instructions ?? "Take one with food until finished.");
  159 |   await c.getByLabel("Quantity", { exact: true }).fill(e.quantity ?? "21");
  160 |   await c.getByLabel("Quantity unit").selectOption(e.quantityUnit ?? "capsule");
  161 |   await c.getByLabel("Refills").fill(e.refills ?? "0");
  162 | }
  163 | 
  164 | async function newDraft(page: Page) {
  165 |   await page.getByRole("button", { name: "New prescription draft" }).click();
  166 |   await expect(composer(page)).toBeVisible();
  167 | }
  168 | 
  169 | /** Finalizes the entry in the composer through the confirmation. */
  170 | async function finalize(page: Page, medication: string) {
  171 |   await composer(page).getByRole("button", { name: "Finalize…" }).click();
  172 |   await expect(confirmation(page)).toBeVisible();
  173 |   await confirmation(page).getByRole("button", { name: "Finalize prescription" }).click();
  174 |   await expect(statusLine(page)).toContainText(`${medication} finalized and logged.`);
  175 | }
  176 | 
  177 | test.beforeAll(async ({ browser }: { browser: Browser }) => {
  178 |   stamp = Date.now();
  179 |   adminContext = await browser.newContext();
  180 |   admin = await adminContext.newPage();
  181 |   const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  182 |   expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  183 |   await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  184 |   for (const role of ["Dentist", "Hygienist", "FrontDesk"]) {
  185 |     const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
  186 |     if (role === "Dentist") {
  187 |       ids.dentistUser = userId;
  188 |       expect((await api(admin, "post", "/api/config/staff", { displayName: `${DENTIST_NAME} ${stamp}`, userAccountId: userId })).status, "staff profile for the dentist").toBe(201);
  189 |     }
  190 |     const context = await browser.newContext();
  191 |     const page = await context.newPage();
  192 |     await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
  193 |     sessions[role] = { context, page };
  194 |   }
  195 |   evidence.stamp = stamp;
  196 | });
  197 | 
  198 | test.afterAll(async () => {
  199 |   writeFileSync(path.join(OUT, "prescription-workflow-evidence.json"), JSON.stringify(evidence, null, 2));
  200 |   for (const s of Object.values(sessions)) await s.context.close();
  201 |   await adminContext?.close();
  202 | });
  203 | 
  204 | test("SETUP: Ann has her allergies reviewed (penicillin and latex); Bo's allergies were never reviewed", async () => {
  205 |   const front = sessions.FrontDesk.page;
  206 |   const dentist = sessions.Dentist.page;
  207 |   const patient = async (firstName: string, lastName: string, key: string, dob: string, phone: string, address: string) => {
  208 |     const r = await api(front, "post", "/api/patients", { firstName, lastName, dateOfBirth: dob, sex: "Female", phone, addressLine1: address, city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `${key}-${stamp}` });
  209 |     expect(r.status).toBe(201);
  210 |     return r.body.id as string;
  211 |   };
  212 |   ids.ann = await patient("Ann", "Alder", "rxw-ann", "1985-03-09", "555-010-0100", "1 Main St");
  213 |   ids.bo = await patient("Bo", "Brandt", "rxw-bo", "1972-11-21", "555-010-0222", "9 Oak Ave");
  214 |   for (const [name, reaction, severity] of [["Penicillin", "Hives", "Severe"], ["Latex", "Rash", "Mild"]]) {
  215 |     expect((await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Allergy", name, reaction, severity })).status, name).toBe(200);
  216 |   }
  217 |   expect((await api(dentist, "put", `/api/patients/${ids.ann}/clinical-record/sections/Allergy/review`, { state: "Reviewed" })).status).toBe(200);
  218 |   // what the shared safety framework (ALV-N011) knows besides allergies: a current medication and an active alert, shown while prescribing
  219 |   expect((await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Medication", name: "Lisinopril", dose: "10 mg", frequency: "daily" })).status).toBe(200);
  220 |   expect((await api(dentist, "post", `/api/patients/${ids.ann}/safety/alerts`, { category: "Condition", title: "Prosthetic heart valve", severity: "High", sourceNote: "Reported by the patient" })).status).toBe(200);
  221 | });
  222 | 
  223 | test("THE SCREEN: a prescriber gets the composer beside the safety panel and a banner that says no drug-knowledge source is configured", async () => {
  224 |   const page = sessions.Dentist.page;
  225 |   await gotoComposer(page);
  226 |   await expect(page.getByRole("note").filter({ hasText: "Not configured" })).toContainText("Drug interaction checking and clinical dose-appropriateness checking are not configured");
  227 |   await newDraft(page);
  228 |   const panel = safetyPanel(page);
  229 |   await expect(panel).toContainText("Penicillin (high) Hives");
  230 |   await expect(panel.getByRole("list", { name: "Current medications" })).toContainText("Lisinopril");               // current medications and ALV-N011 alerts are visible while prescribing
  231 |   await expect(panel.getByRole("list", { name: "Other active alerts" })).toContainText("Prosthetic heart valve (high)");
  232 |   await expect(panel).toContainText("Not checked: drug interactions, and whether a dose is clinically appropriate");
  233 |   await expect(composer(page)).toContainText("not checked against any clinical dose range");
  234 |   await expect(composer(page)).toContainText("no controlled-substance rule");                          // the quantity and refills limitation
  235 |   await expect(page.locator("body")).not.toContainText(/interaction check(ed|s) passed|dose is (safe|appropriate)|cleared/i);
  236 |   await shot(page, "01-composer.png");
  237 |   await scanBothThemes(page, "composer");
```