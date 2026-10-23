# ALV-N015 R01 - Next-story impact
- **ALV-N017** (effective permission policy) starts only after this story is COMPLETE. Four permissions were added (`SendMessages`, `ReceiveDocumentNotifications`, `ViewNotificationAuditHistory`, `ViewMessageAuditContent`); the matrix has 39 permissions, so ALV-N017's enum-generated proofs grow. The consumer resolves recipients from `PermissionMatrix.RoleHas(role, ReceiveDocumentNotifications)` in `NotificationConsumer`; ALV-N017 must replace that call with the effective-policy service (one seam), and `MayViewPatients` in `NotificationsController` likewise.
- **ALV-N018** and later: a new producer registers one more mapping in `NotificationConsumer` (a cursor row per source and a stable SourceKey); nothing else changes.
- Error codes 51195 to 51203 are used; 51204 onward are free.
- The client polls `notifications/summary` every 60 s per open tab.
