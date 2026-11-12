# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: stage-1.spec.ts >> 1.11 starting MFA enrolment without finishing leaves password sign-in unchanged
- Location: chain\stage-1.spec.ts:177:1

# Error details

```
Test timeout of 30000ms exceeded.
```

# Page snapshot

```yaml
- generic [ref=e2]:
  - generic [ref=e3]:
    - link "Skip to main content" [ref=e4] [cursor=pointer]:
      - /url: "#alv-main-content"
    - complementary [ref=e5]:
      - generic [ref=e6]:
        - generic [ref=e7]: AD
        - generic [ref=e8]:
          - text: DentalKeep
          - generic [ref=e9]: Application
      - navigation "Primary navigation" [ref=e10]:
        - link "Dashboard" [ref=e11] [cursor=pointer]:
          - /url: /
        - link "Patients" [ref=e12] [cursor=pointer]:
          - /url: /patients
        - link "Register Patient" [ref=e13] [cursor=pointer]:
          - /url: /patients/register
        - link "Calendar" [ref=e14] [cursor=pointer]:
          - /url: /calendar
        - link "Visit board" [ref=e15] [cursor=pointer]:
          - /url: /flow
        - link "Schedule" [ref=e16] [cursor=pointer]:
          - /url: /schedule
        - link "Procedures & fees" [ref=e17] [cursor=pointer]:
          - /url: /procedures
        - link "Work queue" [ref=e18] [cursor=pointer]:
          - /url: /work-queue
        - link "My follow-ups" [ref=e19] [cursor=pointer]:
          - /url: /follow-ups
        - link "Referrals & labs" [ref=e20] [cursor=pointer]:
          - /url: /external-cases
        - link "Messages" [ref=e21] [cursor=pointer]:
          - /url: /messages
        - link "Component Showcase" [ref=e22] [cursor=pointer]:
          - /url: /showcase
        - link "System Status" [ref=e23] [cursor=pointer]:
          - /url: /system-status
        - link "Security Administration" [ref=e24] [cursor=pointer]:
          - /url: /admin/users
        - link "Recall settings" [ref=e25] [cursor=pointer]:
          - /url: /admin/recall
        - link "Outside parties" [ref=e26] [cursor=pointer]:
          - /url: /admin/external-parties
        - link "Duplicate patients" [ref=e27] [cursor=pointer]:
          - /url: /admin/patient-merge
        - link "Import a patient" [ref=e28] [cursor=pointer]:
          - /url: /admin/patient-import
        - link "Reports" [ref=e29] [cursor=pointer]:
          - /url: /reports
        - link "Data import" [ref=e30] [cursor=pointer]:
          - /url: /admin/data-import
        - link "Export patient data" [ref=e31] [cursor=pointer]:
          - /url: /admin/exports
        - link "Permission Matrix" [ref=e32] [cursor=pointer]:
          - /url: /admin/permissions
        - link "Audit Log" [ref=e33] [cursor=pointer]:
          - /url: /admin/audit-log
        - link "Practice Configuration" [ref=e34] [cursor=pointer]:
          - /url: /admin/configuration
        - link "Patient Registration Settings" [ref=e35] [cursor=pointer]:
          - /url: /admin/patient-registration
        - link "Form Templates" [ref=e36] [cursor=pointer]:
          - /url: /admin/form-templates
        - link "Document Settings" [ref=e37] [cursor=pointer]:
          - /url: /admin/documents
        - link "Document Storage" [ref=e38] [cursor=pointer]:
          - /url: /admin/document-storage
        - link "Notification history" [ref=e39] [cursor=pointer]:
          - /url: /admin/notifications
        - link "Backup & Recovery" [ref=e40] [cursor=pointer]:
          - /url: /admin/backup
        - link "Multi-Factor Authentication" [ref=e41] [cursor=pointer]:
          - /url: /settings/mfa
      - generic [ref=e42]:
        - link "Notifications, 0 unread" [ref=e43] [cursor=pointer]:
          - /url: /notifications
          - generic [aria-hidden] [ref=e44]: Notifications
        - generic "Account" [ref=e45]:
          - generic [ref=e46]:
            - generic [ref=e47]: stage0-admin
            - generic [ref=e48]: Admin
          - button "Sign out" [ref=e49] [cursor=pointer]
        - button "☀ Light" [ref=e50] [cursor=pointer]
    - generic [ref=e51]:
      - region "Patient context" [ref=e52]:
        - paragraph [ref=e53]:
          - text: No patient selected.
          - link "Find a patient" [ref=e54] [cursor=pointer]:
            - /url: /patients
      - main [ref=e55]:
        - generic [ref=e57]:
          - heading "Dashboard" [level=1] [ref=e58]
          - paragraph [ref=e59]: The primary workspace region. Real content arrives with later feature stories.
        - generic [ref=e60]:
          - paragraph [ref=e61]: Nothing to show yet
          - paragraph [ref=e62]: No feature modules have been built on top of this shell yet. This is the correct state for ALV-N001 — not a bug.
  - status
```