| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |
|---|---|---|---|---|---|---|
| signed note (signed, not finalized): observed, not required | EncounterNotes | `UPDATE [EncounterNotes] SET [Body] = LEFT(N'overwritten by the Gate C attempt', 4000) WHERE [Body] IS NOT NULL` | NOT REJECTED |  |  | True |
| finalized encounter note | EncounterNotes | `UPDATE [EncounterNotes] SET [Body] = LEFT(N'overwritten by the Gate C attempt', 4000) WHERE [Body] IS NOT NULL` | REJECTED | 51043 | TR_EncounterNotes_FinalizedImmutable | True |
| finalized encounter note | EncounterNotes | `DELETE FROM [EncounterNotes]` | REJECTED | 51044 | TR_EncounterNotes_NoDelete | True |
| finalized encounter | Encounters | `UPDATE [Encounters] SET [EncounterAtUtc] = DATEADD(day, 1, [EncounterAtUtc]) WHERE [EncounterAtUtc] IS NOT NULL` | REJECTED | 51030 | TR_Encounters_FinalizedImmutable | True |
| finalized encounter | Encounters | `DELETE FROM [Encounters]` | REJECTED | 51031 | TR_Encounters_NoDelete | True |
| finalized encounter entries | EncounterEntries | `UPDATE [EncounterEntries] SET [Name] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Name] IS NOT NULL` | REJECTED | 51032 | TR_EncounterEntries_FinalizedImmutable | True |
| finalized encounter entries | EncounterEntries | `DELETE FROM [EncounterEntries]` | REJECTED | 51033 | TR_EncounterEntries_NoDelete | True |
| encounter history | EncounterEvents | `UPDATE [EncounterEvents] SET [Detail] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Detail] IS NOT NULL` | REJECTED | 51037 | TR_EncounterEvents_Immutable | True |
| encounter history | EncounterEvents | `DELETE FROM [EncounterEvents]` | REJECTED | 51037 | TR_EncounterEvents_Immutable | True |
| encounter addendum | EncounterAddenda | `UPDATE [EncounterAddenda] SET [Text] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Text] IS NOT NULL` | REJECTED | 51036 | TR_EncounterAddenda_Immutable | True |
| encounter addendum | EncounterAddenda | `DELETE FROM [EncounterAddenda]` | REJECTED | 51036 | TR_EncounterAddenda_Immutable | True |
