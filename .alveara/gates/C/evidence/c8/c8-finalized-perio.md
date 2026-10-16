| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |
|---|---|---|---|---|---|---|
| finalized perio chart | PerioExams | `UPDATE [PerioExams] SET [RecordedAtUtc] = DATEADD(day, 1, [RecordedAtUtc]) WHERE [RecordedAtUtc] IS NOT NULL` | REJECTED | 51064 | TR_PerioExams_Immutable | True |
| finalized perio chart | PerioExams | `DELETE FROM [PerioExams]` | REJECTED | 51064 | TR_PerioExams_Immutable | True |
| finalized perio readings | PerioReadings | `UPDATE [PerioReadings] SET [ProbingDepthMm] = [ProbingDepthMm] + 1 WHERE [ProbingDepthMm] IS NOT NULL` | REJECTED | 51065 | TR_PerioReadings_Immutable | True |
| finalized perio readings | PerioReadings | `DELETE FROM [PerioReadings]` | REJECTED | 51065 | TR_PerioReadings_Immutable | True |
| finalized perio tooth records | PerioToothRecords | `UPDATE [PerioToothRecords] SET [Mobility] = [Mobility] + 1 WHERE [Mobility] IS NOT NULL` | REJECTED | 51066 | TR_PerioToothRecords_Immutable | True |
| finalized perio tooth records | PerioToothRecords | `DELETE FROM [PerioToothRecords]` | REJECTED | 51066 | TR_PerioToothRecords_Immutable | True |
| finalized perio session | PerioSessions | `UPDATE [PerioSessions] SET [UpdatedAtUtc] = DATEADD(day, 1, [UpdatedAtUtc]) WHERE [UpdatedAtUtc] IS NOT NULL` | REJECTED | 51068 | TR_PerioSessions_ClosedIsFinal | True |
| finalized perio session | PerioSessions | `DELETE FROM [PerioSessions]` | REJECTED | 51067 | TR_PerioSessions_NoDelete | True |
| finalized perio session readings | PerioSessionReadings | `UPDATE [PerioSessionReadings] SET [ProbingDepthMm] = [ProbingDepthMm] + 1 WHERE [ProbingDepthMm] IS NOT NULL` | REJECTED | 51069 | TR_PerioSessionReadings_DraftOnly | True |
| finalized perio session teeth | PerioSessionTeeth | `UPDATE [PerioSessionTeeth] SET [Mobility] = [Mobility] + 1 WHERE [Mobility] IS NOT NULL` | REJECTED | 51069 | TR_PerioSessionTeeth_DraftOnly | True |
