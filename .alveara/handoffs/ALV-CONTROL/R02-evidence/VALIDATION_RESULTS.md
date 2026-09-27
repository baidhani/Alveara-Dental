# ALV-CONTROL R02 — Validation Results

Every check below was executed against the repository at bootstrap implementation commit `1e3f471d87f561a8ed62c47e932f33e16025e703`, re-running all R01 checks plus the three corrections.

| # | Check | Method | Result |
|---|---|---|---|
| 1 | Repository is a Git repository with a configured GitHub remote | `git remote -v` | **PASS** |
| 2 | Course connection baseline exists | `test -f .colaberry/connect.txt` | **PASS** |
| 3 | `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/` inspected without modification | `git diff --stat` against these paths across the whole R02 attempt | **PASS** — zero diff |
| 4 | STORY-000 genuinely verified by course portal | Read `.colaberry/progress.json` | **PASS** — unchanged from R01 |
| 5 | `.alveara/EXECUTION_STATUS.json` parses as valid JSON | `node -e "JSON.parse(...)"` | **PASS** |
| 6 | Exactly 53 active first-release records | Programmatic count | **PASS** — 53 |
| 7 | Exactly 19 course records / 34 ALV records | Filter by `storyType` | **PASS** — 19 / 34 |
| 8 | No duplicate active IDs | `new Set(ids).size === ids.length` | **PASS** |
| 9 | **(R01-01 fix)** Every ALV record's dependencies match its explicit `**Dependencies:**` line in the Execution Plan | Manual line-by-line transcription + diff against `EXECUTION_STATUS.json`, all 34 records | **PASS** — see `DEPENDENCY_CORRECTIONS.md` |
| 10 | **(R01-01 fix)** `ALV-009-C02` includes `STORY-009` | Direct field check | **PASS** |
| 11 | **(R01-01 fix)** `ALV-N013` does not depend on `ALV-N007`/`ALV-N012` | Direct field check | **PASS** — optional-AI-defer path to `ALV-N013` restored |
| 12 | All dependency references point to existing record IDs | Programmatic check across all 53 records | **PASS** — 0 unknown references |
| 13 | Dependency graph has no cycles | DFS cycle detection | **PASS** |
| 14 | Only `STORY-000` is `COMPLETE`; no ALV item or other course story is `COMPLETE`/`IN_PROGRESS` | Filter by `status` | **PASS** |
| 15 | `ALV-N001` is the sole `READY` item | Filter by `status` | **PASS** |
| 16 | **(R01-02 fix)** Gate F carries all 9 evidence-requirement bullets from the Execution Plan, including "Operational runbook and release evidence are complete" | Line-by-line comparison of Gate F's `GATE F EVIDENCE REQUIREMENTS` block (plan lines 3672–3682) against `QUALITY_GATES.md` Gate F rows | **PASS** — F1–F9 all present |
| 17 | No gate marked passed | `grep -c "\| PASS "` in `QUALITY_GATES.md`; separately, `awk` count of actual `\| [A-F][0-9]+ \|` criteria rows (excludes the 2 prose mentions of the phrase) | **PASS** — 0 `PASS` matches; 46 actual criteria rows (9/7/8/5/8/9), up from 45 (9/7/8/5/8/8) before the F9 fix |
| 18 | No fake evidence exists | Manual review — every Gate A–F row (46 total) has empty Evidence/Date columns | **PASS** |
| 19 | No product feature implemented | `find src -type f`, `find tests -type f` | **PASS** — both empty |
| 20 | `HANDOFF_SCHEMA.md` contains attempts, review closure, `BLOCKED`, `CHANGES_REQUIRED`, revalidation, package safety, Git self-reference prohibition | Keyword search (unchanged from R01) | **PASS** |
| 21 | **(R01-03 fix)** Every ZIP entry uses `/` as the path separator, matching `MANIFEST.json` exactly | Built ZIP with forced forward-slash entry names; `.NET ZipArchive.GetEntry("evidence/GIT_STATE.md")` and `GetEntry("review/NEXT_ACTION.md")` both resolve to non-null entries | **PASS** |
| 22 | Packaged control files byte-match the R02 evidence-commit snapshots | `git show <evidence-sha>:<path>` vs. zipped bytes, all 4 controls + HANDOFF.md | **PASS** |
| 23 | Package safety — no secrets/credentials/PHI | Regex scan across all 12 members + manual context check on any hits | **PASS** — only rule-text mentions of the words "secret"/"password"/"token" as prohibitions |

**Overall result: 23/23 PASS.**
