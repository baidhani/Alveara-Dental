# ALV-N019 R06: acceptance evidence

| R05 review requirement | Status | Evidence |
|---|---|---|
| Composite source of ALL fragments of a partial class, deterministic order, before the three regexes | **MET (code)** | `ComposedClasses()` (ordinal file-name order, all texts joined); used by the migrator test |
| Do not suppress or exclude the partial fixture from the guard | **MET (code)** | no `Infrastructure` change in R06; nothing added to any exclusion |
| Focused assertion that the lookup considers both fragments | **MET (code)** | in the same test: at least two `TestDatabaseFixture` fragment files, and every fragment contained in its composite |
| Re-run the five inventory tests and the source-scanning sweep | **NOT DONE** | the product owner instructed that no test be run; stated in `R06.md` |
| Preserve R04 Stage 2 and R05 evidence | **MET** | unchanged in `R05-evidence/` |
