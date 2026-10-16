| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |
|---|---|---|---|---|---|---|
| diagnosis | Diagnoses | `DELETE FROM [Diagnoses]` | REJECTED | 51071 | TR_Diagnoses_NoDelete | True |
| diagnosis | Diagnoses | `UPDATE [Diagnoses] SET [PatientId] = '73850331-d05e-4a86-80c9-f3f4c0bd84cc'` | REJECTED | 51073 | TR_Diagnoses_Links | True |
| diagnosis history | DiagnosisVersions | `UPDATE [DiagnosisVersions] SET [Label] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Label] IS NOT NULL` | REJECTED | 51072 | TR_DiagnosisVersions_Immutable | True |
| diagnosis history | DiagnosisVersions | `DELETE FROM [DiagnosisVersions]` | REJECTED | 51072 | TR_DiagnosisVersions_Immutable | True |
| diagnosis link | DiagnosisLinks | `UPDATE [DiagnosisLinks] SET [LinkType] = LEFT(N'overwritten by the Gate C attempt', 10) WHERE [LinkType] IS NOT NULL` | REJECTED | 51075 | TR_DiagnosisLinks_Immutable | True |
| diagnosis link | DiagnosisLinks | `DELETE FROM [DiagnosisLinks]` | REJECTED | 51075 | TR_DiagnosisLinks_Immutable | True |
| odontogram finding | ToothFindings | `DELETE FROM [ToothFindings]` | REJECTED | 51055 | TR_ToothFindings_NoDelete | True |
| odontogram history | ToothFindingVersions | `UPDATE [ToothFindingVersions] SET [State] = LEFT(N'overwritten by the Gate C attempt', 10) WHERE [State] IS NOT NULL` | REJECTED | 51056 | TR_ToothFindingVersions_Immutable | True |
| odontogram history | ToothFindingVersions | `DELETE FROM [ToothFindingVersions]` | REJECTED | 51056 | TR_ToothFindingVersions_Immutable | True |
| odontogram link | ToothFindingLinks | `UPDATE [ToothFindingLinks] SET [Reference] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Reference] IS NOT NULL` | REJECTED | 51059 | TR_ToothFindingLinks_Immutable | True |
| odontogram link | ToothFindingLinks | `DELETE FROM [ToothFindingLinks]` | REJECTED | 51059 | TR_ToothFindingLinks_Immutable | True |
