# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: documents-library-real-backend.spec.ts >> INACTIVATE: a reason is required; the document leaves the library but is kept, listed under Inactive with the reason, and reactivated
- Location: e2e\documents-library-real-backend.spec.ts:235:1

# Error details

```
Test timeout of 30000ms exceeded.
```

```
Error: locator.click: Target page, context or browser has been closed
Call log:
  - waiting for getByRole('table', { name: 'Documents' }).getByRole('row').filter({ hasText: 'plain.pdf' }).getByRole('button', { name: 'Inactivate plain.pdf' })

```

# Page snapshot

```yaml
- generic [ref=f5e2]:
  - generic [ref=f5e3]:
    - link "Skip to main content" [ref=f5e4] [cursor=pointer]:
      - /url: "#alv-main-content"
    - complementary [ref=f5e5]:
      - generic [ref=f5e6]:
        - generic [ref=f5e7]: AD
        - generic [ref=f5e8]:
          - text: DentalKeep
          - generic [ref=f5e9]: Application
      - navigation "Primary navigation" [ref=f5e10]:
        - link "Dashboard" [ref=f5e11] [cursor=pointer]:
          - /url: /
        - link "Patients" [ref=f5e12] [cursor=pointer]:
          - /url: /patients
        - link "Register Patient" [ref=f5e13] [cursor=pointer]:
          - /url: /patients/register
        - link "Calendar" [ref=f5e14] [cursor=pointer]:
          - /url: /calendar
        - link "Visit board" [ref=f5e15] [cursor=pointer]:
          - /url: /flow
        - link "Schedule" [ref=f5e16] [cursor=pointer]:
          - /url: /schedule
        - link "Procedures & fees" [ref=f5e17] [cursor=pointer]:
          - /url: /procedures
        - link "Work queue" [ref=f5e18] [cursor=pointer]:
          - /url: /work-queue
        - link "My follow-ups" [ref=f5e19] [cursor=pointer]:
          - /url: /follow-ups
        - link "Referrals & labs" [ref=f5e20] [cursor=pointer]:
          - /url: /external-cases
        - link "Messages" [ref=f5e21] [cursor=pointer]:
          - /url: /messages
        - link "Component Showcase" [ref=f5e22] [cursor=pointer]:
          - /url: /showcase
        - link "Recall settings" [ref=f5e23] [cursor=pointer]:
          - /url: /admin/recall
        - link "Outside parties" [ref=f5e24] [cursor=pointer]:
          - /url: /admin/external-parties
        - link "Import a patient" [ref=f5e25] [cursor=pointer]:
          - /url: /admin/patient-import
        - link "Reports" [ref=f5e26] [cursor=pointer]:
          - /url: /reports
        - link "Permission Matrix" [ref=f5e27] [cursor=pointer]:
          - /url: /admin/permissions
        - link "Audit Log" [ref=f5e28] [cursor=pointer]:
          - /url: /admin/audit-log
        - link "Practice Configuration" [ref=f5e29] [cursor=pointer]:
          - /url: /admin/configuration
        - link "Patient Registration Settings" [ref=f5e30] [cursor=pointer]:
          - /url: /admin/patient-registration
        - link "Form Templates" [ref=f5e31] [cursor=pointer]:
          - /url: /admin/form-templates
        - link "Document Storage" [ref=f5e32] [cursor=pointer]:
          - /url: /admin/document-storage
        - link "Backup & Recovery" [ref=f5e33] [cursor=pointer]:
          - /url: /admin/backup
        - link "Multi-Factor Authentication" [ref=f5e34] [cursor=pointer]:
          - /url: /settings/mfa
      - generic [ref=f5e35]:
        - link "Notifications, 0 unread" [ref=f5e36] [cursor=pointer]:
          - /url: /notifications
          - generic [aria-hidden] [ref=f5e37]: Notifications
        - generic "Account" [ref=f5e38]:
          - generic [ref=f5e39]:
            - generic [ref=f5e40]: officemanager-1795383317154
            - generic [ref=f5e41]: OfficeManager
          - button "Sign out" [ref=f5e42] [cursor=pointer]
        - button "☀ Light" [ref=f5e43] [cursor=pointer]
    - generic [ref=f5e44]:
      - region "Patient context" [ref=f5e45]:
        - generic [ref=f5e46]:
          - generic [ref=f5e47]:
            - generic [ref=f5e48]: Ann Alder
            - generic [ref=f5e49]: MRN 63930980121
            - generic [ref=f5e50]: DOB 1985-03-09 (41 y)
            - generic [ref=f5e51]: Female
            - generic [ref=f5e52]: 555-010-0100
          - generic [ref=f5e53]:
            - link "Open workspace" [ref=f5e54] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85
            - button "Close patient" [ref=f5e55] [cursor=pointer]
          - region "Patient safety" [ref=f5e56]:
            - paragraph [ref=f5e57]:
              - strong [ref=f5e58]: "Patient safety:"
              - text: No alerts, active allergies or current medications are recorded.
            - list [ref=f5e59]:
              - listitem [ref=f5e60]: Allergies have not been reviewed. Nothing listed here does not mean none.
              - listitem [ref=f5e61]: Medications have not been reviewed. Nothing listed here does not mean none.
              - listitem [ref=f5e62]: Medical history has not been reviewed. Nothing listed here does not mean none.
            - paragraph [ref=f5e63]:
              - link "Open safety details" [ref=f5e64] [cursor=pointer]:
                - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/safety
      - main [ref=f5e65]:
        - generic [ref=f5e66]:
          - generic [ref=f5e68]:
            - heading "Patient workspace" [level=1] [ref=f5e69]
            - paragraph [ref=f5e70]: Everything about this patient in one place. The patient shown above stays selected while you move between sections.
          - navigation "Patient workspace sections" [ref=f5e71]:
            - link "Details" [ref=f5e72] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85
            - link "Household & guarantor" [ref=f5e73] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/household
            - link "History" [ref=f5e74] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/history
            - link "Forms" [ref=f5e75] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/forms
            - link "Documents" [ref=f5e76] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/documents
            - link "Clinical" [ref=f5e77] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/clinical
            - link "Safety" [ref=f5e78] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/safety
            - link "Odontogram" [ref=f5e79] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/odontogram
            - link "Periodontal" [ref=f5e80] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/periodontal
            - link "Diagnoses" [ref=f5e81] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/diagnoses
            - link "Treatment plan" [ref=f5e82] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/treatment-plan
            - link "Follow-ups" [ref=f5e83] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/follow-ups
            - link "Referrals & labs" [ref=f5e84] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/external-cases
            - link "Billing" [ref=f5e85] [cursor=pointer]:
              - /url: /patients/d82bbf5a-4527-4bfb-9d6c-05c93c7e7b85/billing
          - region [ref=f5e86]:
            - heading "Documents" [level=2] [ref=f5e87]
            - form "Import a document" [ref=f5e88]:
              - generic [ref=f5e89]:
                - generic [ref=f5e90]: File
                - button "File" [ref=f5e91]
                - paragraph [ref=f5e92]: "Largest file: 100 MB. Programs and web pages cannot be imported."
              - generic [ref=f5e93]:
                - generic [ref=f5e94]: Category
                - combobox "Category" [ref=f5e95]:
                  - option "Choose a category…" [selected]
                  - option "Radiograph"
                  - option "Photo"
                  - option "Referral letter"
                  - option "Medical clearance"
                  - option "Lab report"
                  - option "Insurance card"
                  - option "Identification"
                  - option "Correspondence"
                  - option "Other"
              - generic [ref=f5e96]:
                - generic [ref=f5e97]: Date of document
                - textbox "Date of document" [ref=f5e98]: 2026-11-22
              - generic [ref=f5e99]:
                - generic [ref=f5e100]: Encounter (optional)
                - combobox "Encounter (optional)" [ref=f5e101]:
                  - option "Not about one encounter" [selected]
                  - option "11/22/2026"
              - generic [ref=f5e102]:
                - generic [ref=f5e103]: Tooth (optional)
                - combobox "Tooth (optional)" [ref=f5e104]:
                  - option "Not about one tooth" [selected]
                  - option "1 - upper right third molar"
                  - option "2 - upper right second molar"
                  - option "3 - upper right first molar"
                  - option "4 - upper right second premolar"
                  - option "5 - upper right first premolar"
                  - option "6 - upper right canine"
                  - option "7 - upper right lateral incisor"
                  - option "8 - upper right central incisor"
                  - option "9 - upper left central incisor"
                  - option "10 - upper left lateral incisor"
                  - option "11 - upper left canine"
                  - option "12 - upper left first premolar"
                  - option "13 - upper left second premolar"
                  - option "14 - upper left first molar"
                  - option "15 - upper left second molar"
                  - option "16 - upper left third molar"
                  - option "32 - lower right third molar"
                  - option "31 - lower right second molar"
                  - option "30 - lower right first molar"
                  - option "29 - lower right second premolar"
                  - option "28 - lower right first premolar"
                  - option "27 - lower right canine"
                  - option "26 - lower right lateral incisor"
                  - option "25 - lower right central incisor"
                  - option "24 - lower left central incisor"
                  - option "23 - lower left lateral incisor"
                  - option "22 - lower left canine"
                  - option "21 - lower left first premolar"
                  - option "20 - lower left second premolar"
                  - option "19 - lower left first molar"
                  - option "18 - lower left second molar"
                  - option "17 - lower left third molar"
                  - option "E - upper right primary central incisor"
                  - option "D - upper right primary lateral incisor"
                  - option "C - upper right primary canine"
                  - option "B - upper right primary first molar"
                  - option "A - upper right primary second molar"
                  - option "F - upper left primary central incisor"
                  - option "G - upper left primary lateral incisor"
                  - option "H - upper left primary canine"
                  - option "I - upper left primary first molar"
                  - option "J - upper left primary second molar"
                  - option "O - lower left primary central incisor"
                  - option "N - lower left primary lateral incisor"
                  - option "M - lower left primary canine"
                  - option "L - lower left primary first molar"
                  - option "K - lower left primary second molar"
                  - option "P - lower right primary central incisor"
                  - option "Q - lower right primary lateral incisor"
                  - option "R - lower right primary canine"
                  - option "S - lower right primary first molar"
                  - option "T - lower right primary second molar"
              - generic [ref=f5e105]:
                - generic [ref=f5e106]: Oral region (optional)
                - combobox "Oral region (optional)" [ref=f5e107]:
                  - option "Not about a region" [selected]
                  - option "Whole mouth"
                  - option "Upper arch"
                  - option "Lower arch"
                  - option "Upper right quadrant"
                  - option "Upper left quadrant"
                  - option "Lower right quadrant"
                  - option "Lower left quadrant"
                  - option "Soft tissue"
                  - option "Jaw joint (TMJ)"
              - generic [ref=f5e108]:
                - generic [ref=f5e109]: Description (optional)
                - textbox "Description (optional)" [ref=f5e110]
              - button "Import document" [ref=f5e112] [cursor=pointer]
            - search "Filter documents" [ref=f5e113]:
              - generic [ref=f5e114]:
                - generic [ref=f5e115]: Category
                - combobox "Category" [ref=f5e116]:
                  - option "All categories" [selected]
                  - option "Radiograph"
                  - option "Photo"
                  - option "Referral letter"
                  - option "Medical clearance"
                  - option "Lab report"
                  - option "Insurance card"
                  - option "Identification"
                  - option "Correspondence"
                  - option "Other"
              - generic [ref=f5e117]:
                - generic [ref=f5e118]: Encounter
                - combobox "Encounter" [ref=f5e119]:
                  - option "Any encounter" [selected]
                  - option "11/22/2026"
              - generic [ref=f5e120]:
                - generic [ref=f5e121]: Tooth
                - combobox "Tooth" [ref=f5e122]:
                  - option "Any tooth" [selected]
                  - option "1 - upper right third molar"
                  - option "2 - upper right second molar"
                  - option "3 - upper right first molar"
                  - option "4 - upper right second premolar"
                  - option "5 - upper right first premolar"
                  - option "6 - upper right canine"
                  - option "7 - upper right lateral incisor"
                  - option "8 - upper right central incisor"
                  - option "9 - upper left central incisor"
                  - option "10 - upper left lateral incisor"
                  - option "11 - upper left canine"
                  - option "12 - upper left first premolar"
                  - option "13 - upper left second premolar"
                  - option "14 - upper left first molar"
                  - option "15 - upper left second molar"
                  - option "16 - upper left third molar"
                  - option "32 - lower right third molar"
                  - option "31 - lower right second molar"
                  - option "30 - lower right first molar"
                  - option "29 - lower right second premolar"
                  - option "28 - lower right first premolar"
                  - option "27 - lower right canine"
                  - option "26 - lower right lateral incisor"
                  - option "25 - lower right central incisor"
                  - option "24 - lower left central incisor"
                  - option "23 - lower left lateral incisor"
                  - option "22 - lower left canine"
                  - option "21 - lower left first premolar"
                  - option "20 - lower left second premolar"
                  - option "19 - lower left first molar"
                  - option "18 - lower left second molar"
                  - option "17 - lower left third molar"
                  - option "E - upper right primary central incisor"
                  - option "D - upper right primary lateral incisor"
                  - option "C - upper right primary canine"
                  - option "B - upper right primary first molar"
                  - option "A - upper right primary second molar"
                  - option "F - upper left primary central incisor"
                  - option "G - upper left primary lateral incisor"
                  - option "H - upper left primary canine"
                  - option "I - upper left primary first molar"
                  - option "J - upper left primary second molar"
                  - option "O - lower left primary central incisor"
                  - option "N - lower left primary lateral incisor"
                  - option "M - lower left primary canine"
                  - option "L - lower left primary first molar"
                  - option "K - lower left primary second molar"
                  - option "P - lower right primary central incisor"
                  - option "Q - lower right primary lateral incisor"
                  - option "R - lower right primary canine"
                  - option "S - lower right primary first molar"
                  - option "T - lower right primary second molar"
              - generic [ref=f5e123]:
                - generic [ref=f5e124]: Oral region
                - combobox "Oral region" [ref=f5e125]:
                  - option "Any region" [selected]
                  - option "Whole mouth"
                  - option "Upper arch"
                  - option "Lower arch"
                  - option "Upper right quadrant"
                  - option "Upper left quadrant"
                  - option "Lower right quadrant"
                  - option "Lower left quadrant"
                  - option "Soft tissue"
                  - option "Jaw joint (TMJ)"
              - generic [ref=f5e126]:
                - generic [ref=f5e127]: From
                - textbox "From" [ref=f5e128]
              - generic [ref=f5e129]:
                - generic [ref=f5e130]: To
                - textbox "To" [ref=f5e131]
              - generic [ref=f5e132]:
                - generic [ref=f5e133]: Search file names and descriptions
                - searchbox "Search file names and descriptions" [ref=f5e134]
              - generic [ref=f5e135]:
                - generic [ref=f5e136]: Show
                - combobox "Show" [ref=f5e137]:
                  - option "Active documents" [selected]
                  - option "Inactive documents"
                  - option "All documents"
              - generic [ref=f5e138]:
                - button "Apply filters" [ref=f5e139] [cursor=pointer]
                - button "Clear filters" [ref=f5e140] [cursor=pointer]
            - radiogroup "View" [ref=f5e141]:
              - generic [ref=f5e143]:
                - radio "List" [checked] [ref=f5e144]
                - text: List
              - generic [ref=f5e145]:
                - radio "Thumbnails" [ref=f5e146]
                - text: Thumbnails
            - paragraph [ref=f5e147]: PDFs open in your browser's own viewer in a new tab; pictures can also be previewed here. Other files are downloaded.
            - table "Documents" [ref=f5e148]:
              - rowgroup [ref=f5e149]:
                - row [ref=f5e150]:
                  - columnheader "Name" [ref=f5e151]
                  - columnheader "Category" [ref=f5e152]
                  - columnheader "About" [ref=f5e153]
                  - columnheader "Date" [ref=f5e154]
                  - columnheader "Size" [ref=f5e155]
                  - columnheader "Imported by" [ref=f5e156]
                  - columnheader "Actions" [ref=f5e157]
              - rowgroup [ref=f5e158]:
                - row [ref=f5e159]:
                  - cell "plain.pdf No tooth, no encounter." [ref=f5e160]:
                    - text: plain.pdf
                    - generic "No tooth, no encounter." [ref=f5e161]
                  - cell "Referral letter" [ref=f5e162]
                  - cell "—" [ref=f5e163]
                  - cell "10/1/2026" [ref=f5e164]
                  - cell "74 bytes" [ref=f5e165]
                  - cell "OfficeManager 1795383317154" [ref=f5e166]
                  - cell [ref=f5e167]:
                    - button "Open plain.pdf" [ref=f5e168] [cursor=pointer]: Open
                    - link "Download plain.pdf" [ref=f5e169] [cursor=pointer]:
                      - /url: /api/documents/33f7c06e-2625-436d-a2d9-dc7a6588ed5b/content?download=true
                      - text: Download
                    - button "Details plain.pdf" [ref=f5e170] [cursor=pointer]: Details
                    - group [ref=f5e171]:
                      - generic "More actions for plain.pdf" [ref=f5e172] [cursor=pointer]: More
                - row [ref=f5e173]:
                  - cell "region.png Whole lower jaw." [ref=f5e174]:
                    - text: region.png
                    - generic "Whole lower jaw." [ref=f5e175]
                  - cell "Radiograph" [ref=f5e176]
                  - cell "Whole mouth" [ref=f5e177]
                  - cell "10/1/2026" [ref=f5e180]
                  - cell "73 bytes" [ref=f5e181]
                  - cell "OfficeManager 1795383317154" [ref=f5e182]
                  - cell [ref=f5e183]:
                    - button "Preview region.png" [ref=f5e184] [cursor=pointer]: Preview
                    - link "Download region.png" [ref=f5e185] [cursor=pointer]:
                      - /url: /api/documents/c6d5ad5f-098b-4531-8e6a-6a7c897d4fba/content?download=true
                      - text: Download
                    - button "Details region.png" [ref=f5e186] [cursor=pointer]: Details
                    - group [ref=f5e187]:
                      - generic "More actions for region.png" [ref=f5e188] [cursor=pointer]: More
                - row [ref=f5e189]:
                  - cell "tooth16.png Periapical, upper right first molar." [ref=f5e190]:
                    - text: tooth16.png
                    - generic "Periapical, upper right first molar." [ref=f5e191]
                  - cell "Radiograph" [ref=f5e192]
                  - cell "Encounter 11/22/2026 Tooth 3" [ref=f5e193]:
                    - generic [ref=f5e194]:
                      - generic [ref=f5e195]: Encounter 11/22/2026
                      - generic [ref=f5e196]: Tooth 3
                  - cell "10/1/2026" [ref=f5e197]
                  - cell "73 bytes" [ref=f5e198]
                  - cell "OfficeManager 1795383317154" [ref=f5e199]
                  - cell [ref=f5e200]:
                    - button "Preview tooth16.png" [ref=f5e201] [cursor=pointer]: Preview
                    - link "Download tooth16.png" [ref=f5e202] [cursor=pointer]:
                      - /url: /api/documents/887dddc2-8fca-4563-bba7-3ceb291de96a/content?download=true
                      - text: Download
                    - button "Details tooth16.png" [ref=f5e203] [cursor=pointer]: Details
                    - group [ref=f5e204]:
                      - generic "More actions for tooth16.png" [ref=f5e205] [cursor=pointer]: More
  - status
```

# Test source

```ts
  138 | test.afterAll(async () => {
  139 |   writeFileSync(path.join(OUT, "documents-library-evidence.json"), JSON.stringify(evidence, null, 2));
  140 |   for (const s of Object.values(sessions)) await s.context.close();
  141 |   await adminContext?.close();
  142 | });
  143 | 
  144 | test("SETUP: a patient with one encounter and an empty library", async () => {
  145 |   const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `lib-ann-${stamp}` });
  146 |   expect(r.status).toBe(201);
  147 |   ids.ann = r.body.id as string;
  148 |   const enc = await api(sessions.Dentist.page, "post", `/api/patients/${ids.ann}/encounters`, {}, { "Idempotency-Key": `lib-enc-${stamp}` });
  149 |   expect(enc.status).toBe(201);
  150 |   ids.encounter = enc.body.id as string;
  151 |   expect(await entries(admin)).toHaveLength(0);
  152 | 
  153 |   // ALV-N026 (PD-24): the office manager, this walkthrough's librarian, imports radiographs and a referral letter, which are Clinical categories. By default an office manager reads only
  154 |   // NonClinical documents, so the practice grants them ViewClinicalDocumentation through the real permission policy (as a practice may). Without it the imports below are refused
  155 |   // (category_not_permitted), which DocumentClassificationAccessApiTests proves directly.
  156 |   const version = (await api(admin, "get", "/api/permissions/effective")).body.version as number;
  157 |   const grant = await api(admin, "put", "/api/permissions/overrides", { role: "OfficeManager", permission: "ViewClinicalDocumentation", effect: "Grant", reason: "The office manager keeps the document library", expectedVersion: version, confirmed: true }, { "Idempotency-Key": `lib-grant-${stamp}` });
  158 |   expect(grant.status).toBe(200);
  159 |   evidence.officeManagerClinicalGrant = true;
  160 | });
  161 | 
  162 | test("IMPORT: documents are tied to an encounter, a tooth and an oral region through the screen, and the server holds exactly that", async () => {
  163 |   const page = sessions.OfficeManager.page;
  164 |   await gotoDocuments(page);
  165 |   await importFile(page, { name: "tooth16.png", mimeType: "image/png", buffer: png("t16") }, { encounter: /./, tooth: "16", description: "Periapical, upper right first molar." });
  166 |   await importFile(page, { name: "region.png", mimeType: "image/png", buffer: png("reg") }, { region: "x", description: "Whole lower jaw." });
  167 |   await importFile(page, { name: "plain.pdf", mimeType: "application/pdf", buffer: pdf("plain") }, { category: "Referral letter", description: "No tooth, no encounter." });
  168 |   await expect(rowOf(page, "tooth16.png")).toContainText("Tooth");
  169 |   await expect(rowOf(page, "tooth16.png")).toContainText("Encounter");
  170 |   await shot(page, "01-library-with-context.png");
  171 | 
  172 |   const docs = (await entries(admin)).filter((e) => e.kind === "Document");
  173 |   const t16 = docs.find((d) => d.title === "tooth16.png").document;
  174 |   expect([t16.encounterId, t16.toothKey, t16.regionKey ?? null, t16.status]).toEqual([ids.encounter, "16", null, "Active"]);
  175 |   const region = docs.find((d) => d.title === "region.png").document;
  176 |   expect([region.encounterId ?? null, region.toothKey ?? null, region.status]).toEqual([null, null, "Active"]);
  177 |   expect(region.regionKey).toBeTruthy();
  178 | });
  179 | 
  180 | test("FILTERS: by tooth, region, encounter and a search word, with a bad range explained beside its box and Clear bringing everything back", async () => {
  181 |   const page = sessions.OfficeManager.page;
  182 |   await gotoDocuments(page);
  183 |   await applyFilters(page, async (f) => { await f.getByLabel("Tooth", { exact: true }).selectOption("16"); });
  184 |   await expect(rowOf(page, "tooth16.png")).toBeVisible();
  185 |   await expect(rowOf(page, "plain.pdf")).toHaveCount(0);
  186 |   expect(await titles(admin, "?toothKey=16")).toEqual(["tooth16.png"]);
  187 | 
  188 |   await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("Oral region").selectOption({ index: 1 }); });
  189 |   await expect(rowOf(page, "region.png")).toBeVisible();
  190 |   await expect(rowOf(page, "tooth16.png")).toHaveCount(0);
  191 | 
  192 |   await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("Encounter", { exact: true }).selectOption({ index: 1 }); });
  193 |   await expect(rowOf(page, "tooth16.png")).toBeVisible();
  194 |   await expect(rowOf(page, "region.png")).toHaveCount(0);
  195 | 
  196 |   await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("Search file names and descriptions").fill("lower jaw"); });
  197 |   await expect(rowOf(page, "region.png")).toBeVisible();
  198 |   await expect(rowOf(page, "plain.pdf")).toHaveCount(0);
  199 | 
  200 |   await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("From", { exact: true }).fill("2026-10-05"); await f.getByLabel("To", { exact: true }).fill("2026-10-01"); });
  201 |   await expect(filterForm(page).getByRole("alert")).toBeVisible();
  202 |   await shot(page, "02-filter-refused-range.png");
  203 | 
  204 |   await filterForm(page).getByRole("button", { name: "Clear filters" }).click();
  205 |   for (const name of ["tooth16.png", "region.png", "plain.pdf"]) await expect(rowOf(page, name)).toBeVisible();
  206 |   await scanBothThemes(page, "library-list");
  207 | });
  208 | 
  209 | test("THUMBNAILS: a picture is drawn small, anything else is named in words, and fetching a thumbnail leaves no trace in the log", async () => {
  210 |   const page = sessions.OfficeManager.page;
  211 |   await gotoDocuments(page);
  212 |   const before = (await auditOf("DocumentOpened")).length;
  213 |   await page.getByLabel("Thumbnails").check();
  214 |   await expect(page.getByRole("img", { name: "Thumbnail of tooth16.png" })).toBeVisible();
  215 |   await expect.poll(() => page.getByRole("img", { name: "Thumbnail of tooth16.png" }).evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth > 0)).toBe(true);
  216 |   await expect(page.getByRole("img", { name: "Thumbnail of plain.pdf" })).toHaveCount(0);
  217 |   await shot(page, "03-thumbnails.png");
  218 |   const png1 = await documentId("tooth16.png");
  219 |   const pdf1 = await documentId("plain.pdf");
  220 |   expect((await page.request.get(`/api/documents/${png1}/thumbnail`)).status()).toBe(200);
  221 |   expect((await page.request.get(`/api/documents/${pdf1}/thumbnail`)).status()).toBe(415);
  222 |   expect((await auditOf("DocumentOpened")).length).toBe(before);
  223 |   await scanBothThemes(page, "library-thumbnails");
  224 |   await page.getByLabel("List").check();
  225 | });
  226 | 
  227 | test("PREVIEW: Preview shows the picture on the screen", async () => {
  228 |   const page = sessions.OfficeManager.page;
  229 |   await gotoDocuments(page);
  230 |   await rowOf(page, "tooth16.png").getByRole("button", { name: "Preview tooth16.png" }).click();
  231 |   await expect(page.getByRole("img", { name: "tooth16.png", exact: true })).toBeVisible();
  232 |   await page.getByRole("button", { name: "Close preview" }).click();
  233 | });
  234 | 
  235 | test("INACTIVATE: a reason is required; the document leaves the library but is kept, listed under Inactive with the reason, and reactivated", async () => {
  236 |   const page = sessions.OfficeManager.page;
  237 |   await gotoDocuments(page);
> 238 |   await rowOf(page, "plain.pdf").getByRole("button", { name: "Inactivate plain.pdf" }).click();
      |                                                                                        ^ Error: locator.click: Target page, context or browser has been closed
  239 |   const form = page.getByRole("form", { name: "Inactivate document" });
  240 |   await form.getByRole("button", { name: "Make inactive" }).click();
  241 |   await expect(form.getByRole("alert")).toBeVisible();                       // a reason is needed
  242 |   expect(await titles(admin)).toContain("plain.pdf");
  243 |   await form.getByLabel("Why is this document being made inactive?").fill("Scanned under the wrong patient.");
  244 |   await form.getByRole("button", { name: "Make inactive" }).click();
  245 |   await expect(rowOf(page, "plain.pdf")).toHaveCount(0);
  246 |   expect(await titles(admin)).not.toContain("plain.pdf");
  247 |   expect(await titles(admin, "?status=Inactive")).toEqual(["plain.pdf"]);
  248 | 
  249 |   await applyFilters(page, async (f) => { await f.getByLabel("Show", { exact: true }).selectOption("Inactive"); });
  250 |   await expect(rowOf(page, "plain.pdf")).toContainText("Inactive");
  251 |   await rowOf(page, "plain.pdf").getByRole("button", { name: "Details plain.pdf" }).click();
  252 |   await expect(page.getByRole("list", { name: "Status changes" })).toContainText("Scanned under the wrong patient.");
  253 |   await shot(page, "04-inactive-details.png");
  254 |   await page.getByRole("button", { name: "Close details" }).click();
  255 | 
  256 |   await rowOf(page, "plain.pdf").getByRole("button", { name: "Reactivate plain.pdf" }).click();
  257 |   await filterForm(page).getByRole("button", { name: "Clear filters" }).click();
  258 |   await expect(rowOf(page, "plain.pdf")).toBeVisible();
  259 |   await expect.poll(() => titles(admin)).toContain("plain.pdf");                  // the reactivation has reached the server
  260 |   expect(sql(`SET NOCOUNT ON; SELECT COUNT(*) FROM DocumentStatusChanges`)).toBe("2");
  261 | });
  262 | 
  263 | test("THE DENTIST reads but cannot make a document inactive, and the API refuses with 403", async () => {
  264 |   const page = sessions.Dentist.page;
  265 |   await gotoDocuments(page);
  266 |   await expect(rowOf(page, "tooth16.png")).toBeVisible();
  267 |   await expect(page.getByRole("button", { name: /^Inactivate / })).toHaveCount(0);
  268 |   const id = await documentId("tooth16.png");
  269 |   expect((await api(page, "post", `/api/documents/${id}/inactivate`, { rowVersion: "AAAAAAAAB9E=", reason: "no" })).status).toBe(403);
  270 |   expect((await api(page, "get", "/api/document-storage")).status).toBe(403);
  271 | });
  272 | 
  273 | test("STORAGE: the office manager checks every stored file and all are intact", async () => {
  274 |   const page = sessions.OfficeManager.page;
  275 |   await page.goto("/admin/document-storage");
  276 |   await expect(page.getByRole("heading", { name: "Document storage" })).toBeVisible();
  277 |   await page.getByRole("button", { name: "Check all stored files" }).click();
  278 |   await expect(page.locator("p.alv-documents__status")).toContainText("Checked 3 documents: 3 intact, 0 missing, 0 damaged.");
  279 |   await expect(page.getByText("No file needs attention")).toBeVisible();
  280 |   await shot(page, "05-storage-intact.png");
  281 |   await scanBothThemes(page, "storage-intact");
  282 | });
  283 | 
  284 | test("MISSING FILE: a file taken from disk is reported missing, the library says so, opening it is refused, and putting it back makes it intact", async () => {
  285 |   const page = sessions.OfficeManager.page;
  286 |   const file = blobFile("region.png");
  287 |   const parked = `${file}.parked`;
  288 |   expect(existsSync(file)).toBe(true);
  289 |   renameSync(file, parked);
  290 |   try {
  291 |     await page.goto("/admin/document-storage");
  292 |     await page.getByRole("button", { name: "Check all stored files" }).click();
  293 |     await expect(page.locator("p.alv-documents__status")).toContainText("1 missing");
  294 |     await expect(page.getByRole("table", { name: "Files that need attention" })).toContainText("region.png");
  295 |     await expect(page.getByRole("table", { name: "Files that need attention" })).toContainText("Missing");
  296 |     await shot(page, "06-storage-missing.png");
  297 |     await scanBothThemes(page, "storage-missing");
  298 | 
  299 |     await gotoDocuments(page);
  300 |     await expect(rowOf(page, "region.png")).toContainText("File missing");
  301 |     const id = await documentId("region.png");
  302 |     const opened = await page.request.get(`/api/documents/${id}/content`);
  303 |     expect(opened.status()).toBe(404);
  304 |     expect(((await opened.json()) as any).error).toBe("file_missing");
  305 |     expect(JSON.stringify(await opened.json())).not.toMatch(/Storage|alvera-data|walkthrough-/i);
  306 |     await shot(page, "07-library-missing.png");
  307 |   } finally {
  308 |     if (existsSync(parked)) renameSync(parked, file);
  309 |   }
  310 |   await page.goto("/admin/document-storage");
  311 |   await page.getByRole("button", { name: "Check region.png again" }).click();
  312 |   await expect(page.locator("p.alv-documents__status")).toContainText("region.png is intact");
  313 |   await expect(page.getByText("No file needs attention")).toBeVisible();
  314 |   expect((await page.request.get(`/api/documents/${await documentId("region.png")}/content`)).status()).toBe(200);
  315 | });
  316 | 
  317 | test("MISSING OR DAMAGED, AS STAFF SEE IT (ALV-N026 R02): opening an unchecked file that is gone explains it in the app; a file found missing or damaged offers no Open, Preview or Download and says why; nothing names where files are stored", async () => {
  318 |   const page = sessions.OfficeManager.page;
  319 |   const storageWords = /alvera-data|walkthrough-|Test-Data|[A-Za-z]:\\|\\Storage|\.blob|SqlException|stack trace|"error":/i;
  320 |   const row = (name: string) => rowOf(page, name);
  321 |   const noFileActions = async (name: string) => {
  322 |     for (const action of ["Open", "Preview", "Download"]) await expect(row(name).getByRole(action === "Download" ? "link" : "button", { name: `${action} ${name}` })).toHaveCount(0);
  323 |   };
  324 | 
  325 |   // 1. a file that disappears after its last check: the row still offers Open, and Open checks first, so the problem is explained in the app, not in a raw error tab
  326 |   const pdfFile = blobFile("plain.pdf");
  327 |   const parked = `${pdfFile}.parked`;
  328 |   renameSync(pdfFile, parked);
  329 |   try {
  330 |     await gotoDocuments(page);
  331 |     const tabsBefore = page.context().pages().length;
  332 |     await row("plain.pdf").getByRole("button", { name: "Open plain.pdf" }).click();
  333 |     await expect(page.getByText("plain.pdf cannot be opened: its file is missing from storage. The record is intact. Ask the practice administrator to check document storage.")).toBeVisible();
  334 |     expect(page.context().pages().length).toBe(tabsBefore);                                                  // no tab with a raw server answer
  335 |     expect(await page.locator("main").innerText()).not.toMatch(storageWords);
  336 |     await shot(page, "07b-open-unchecked-missing.png");
  337 | 
  338 |     // 2. once a check has found it missing, the row itself says so and offers no way to open it
```