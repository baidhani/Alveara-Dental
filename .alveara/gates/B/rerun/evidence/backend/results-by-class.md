# Gate B - full backend run, results by test class

`dotnet test src/Alveara.Api.Tests` (real SQL Server LocalDB, one database per test class) on HEAD `b0620bfdda1962be9dc1f2700569ca0b72ea700d`: **957 of 957 passing**, 0 failed, 0 not executed, 0 other. Started 2026-10-02T21:55:37.7895910-05:00, finished 2026-10-02T22:42:52.6207067-05:00.

| Class | Passed / Total |
|---|---|
| AccountServiceAuditTests | 4 / 4 |
| AccountServiceBootstrapTests | 4 / 4 |
| AccountServiceLoginTests | 10 / 10 |
| AccountServiceMfaTests | 33 / 33 |
| AccountServicePasswordResetTests | 4 / 4 |
| AccountServiceRegistrationTests | 5 / 5 |
| AccountServiceRoleChangeTests | 3 / 3 |
| AppointmentLifecycleApiTests | 23 / 23 |
| AppointmentLifecycleTests | 25 / 25 |
| AppointmentSchedulerTests | 41 / 41 |
| AppointmentsApiTests | 30 / 30 |
| AuditLogImmutabilityTests | 6 / 6 |
| AuthControllerMfaConfirmSessionTests | 2 / 2 |
| AuthControllerPermissionMatrixTests | 8 / 8 |
| AuthControllerRbacTests | 10 / 10 |
| AuthControllerTests | 11 / 11 |
| AuthCookieAttributesTests | 1 / 1 |
| AuthRateLimitTests | 4 / 4 |
| AvailabilityConcurrencyTests | 5 / 5 |
| BackgroundJobPhiSafeLoggingTests | 1 / 1 |
| BackgroundJobTests | 11 / 11 |
| BackupApiTests | 12 / 12 |
| BackupCryptoTests | 22 / 22 |
| BackupRecoveryHardeningTests | 30 / 30 |
| BackupRestoreTests | 20 / 20 |
| BackupSchedulerTests | 9 / 9 |
| BackupServiceTests | 20 / 20 |
| BackupSnapshotTests | 1 / 1 |
| BlobStorageTests | 4 / 4 |
| CheckInReadinessTests | 15 / 15 |
| ConcurrencyGuardTests | 3 / 3 |
| ConfigurationApiTests | 8 / 8 |
| FormLifecycleTests | 17 / 17 |
| FormSigningTests | 26 / 26 |
| FormTemplateVersionTests | 30 / 30 |
| FormsApiTests | 26 / 26 |
| FrameworkLoggingPhiSafetyTests | 1 / 1 |
| HealthEndpointTests | 1 / 1 |
| IdempotencyGuardTests | 5 / 5 |
| LeastPrivilegeAccessTests | 2 / 2 |
| MeasurementEventTests | 16 / 16 |
| MigrationFailureTests | 1 / 1 |
| MigrationTests | 3 / 3 |
| MigrationUpgradeTests | 1 / 1 |
| MoneyTests | 8 / 8 |
| NoPublicInternetDependencyTests | 1 / 1 |
| PatientDirectoryTests | 7 / 7 |
| PatientDuplicateWarningTests | 12 / 12 |
| PatientEditTests | 14 / 14 |
| PatientFlowApiTests | 13 / 13 |
| PatientFlowLifecycleTests | 13 / 13 |
| PatientFlowRulesTests | 20 / 20 |
| PatientIdentityApiTests | 15 / 15 |
| PatientModelTests | 14 / 14 |
| PatientRegistrationServiceTests | 24 / 24 |
| PatientRelationshipTests | 12 / 12 |
| PatientRequirementsTests | 12 / 12 |
| PatientsApiTests | 7 / 7 |
| Pbkdf2PasswordHasherTests | 8 / 8 |
| PermissionMatrixTests | 8 / 8 |
| PhiSafeLogTests | 2 / 2 |
| PracticeClockTests | 7 / 7 |
| PracticeConfigurationServiceTests | 21 / 21 |
| RealProcessDatabaseOutageTests | 1 / 1 |
| RecordLifecycleGuardTests | 3 / 3 |
| SchedulingConfigurationTests | 7 / 7 |
| SignedFormSnapshotTests | 11 / 11 |
| StaffProviderServiceTests | 22 / 22 |
| StorageIsolationTests | 2 / 2 |
| SystemStatusTests | 2 / 2 |
| TransactionRollbackTests | 2 / 2 |
| VisitAssignmentTests | 11 / 11 |
| VisitBoardTests | 10 / 10 |
| VisitOccupancyTests | 6 / 6 |
| VisitPermissionTests | 12 / 12 |
| VisitStateMachineTests | 54 / 54 |
| VisitWorkflowSchemaTests | 13 / 13 |
| VisitWorkflowTests | 22 / 22 |
| VisitsApiTests | 47 / 47 |

## Classes that bear on each Gate B criterion

- **B1 registration / family / guarantor**: 117 / 117 across PatientDirectoryTests (7/7), PatientDuplicateWarningTests (12/12), PatientEditTests (14/14), PatientIdentityApiTests (15/15), PatientModelTests (14/14), PatientRegistrationServiceTests (24/24), PatientRelationshipTests (12/12), PatientRequirementsTests (12/12), PatientsApiTests (7/7)
- **B2 forms / consents / signatures / check-in readiness**: 125 / 125 across CheckInReadinessTests (15/15), FormLifecycleTests (17/17), FormSigningTests (26/26), FormTemplateVersionTests (30/30), FormsApiTests (26/26), SignedFormSnapshotTests (11/11)
- **B4 scheduler (conflicts, blocked time, reschedule / cancel / no-show, races)**: 131 / 131 across AppointmentLifecycleApiTests (23/23), AppointmentLifecycleTests (25/25), AppointmentSchedulerTests (41/41), AppointmentsApiTests (30/30), AvailabilityConcurrencyTests (5/5), SchedulingConfigurationTests (7/7)
- **B5 patient flow / visit workflow / board**: 221 / 221 across PatientFlowApiTests (13/13), PatientFlowLifecycleTests (13/13), PatientFlowRulesTests (20/20), VisitAssignmentTests (11/11), VisitBoardTests (10/10), VisitOccupancyTests (6/6), VisitPermissionTests (12/12), VisitStateMachineTests (54/54), VisitWorkflowSchemaTests (13/13), VisitWorkflowTests (22/22), VisitsApiTests (47/47)
