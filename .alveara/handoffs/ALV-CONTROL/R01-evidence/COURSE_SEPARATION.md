# ALV-CONTROL R01 — Course Separation Evidence

## Observed post-STORY-000 course baseline

At the time this bootstrap began, the repository contained the following course/platform-controlled content, all present as a result of the course connection flow and STORY-000's own portal-verified sync — none of it created or altered by this bootstrap:

- `.colaberry/connect.txt` — course pairing proof, present since commit `2623151` ("Connect this folder to Colaberry").
- `.colaberry/plan.json`, `.colaberry/progress.json`, `.colaberry/manifest.json`, `.colaberry/profile.json` — written by the course platform's own sync commits (`aabb5be`, `cc79677`), authored by `Colaberry Build Bot`.
- `docs/STORIES.md`, `docs/REQUIREMENTS.md`, `docs/TRACEABILITY.md`, `docs/DATA_CONTRACT.md`, `docs/stories/STORY-000.md` through `STORY-018.md` — written by the same course sync commits.
- `CLAUDE.md` at repository root — course-provided, present since before STORY-000.

## STORY-000 verification evidence (imported, not manufactured)

From `.colaberry/progress.json` at bootstrap time:

```json
{
  "id": "STORY-000",
  "verification": {
    "state": "verified",
    "criteria_passed": 5,
    "criteria_total": 5,
    "verified_at": "2026-09-27T19:54:59.856Z",
    "commit_sha": "5d67b100eb4d363e714d362cc07414ea7fc4ba7a",
    "commit_url": "https://github.com/baidhani/Alveara-Dental/commit/5d67b100eb4d363e714d362cc07414ea7fc4ba7a",
    "points_awarded": 187,
    "outstanding": []
  }
}
```

This exact evidence (state, counts, timestamp, commit SHA) was copied into `.alveara/EXECUTION_STATUS.json`'s `STORY-000` record. Nothing was invented or assumed.

## Evidence `.colaberry` was not recreated, restructured, repurposed, or falsely advanced

`git diff --stat` for `.colaberry/`, `docs/`, and `CLAUDE.md` across the entire bootstrap (from the commit immediately before `c88f6ff` through the evidence commit) shows **zero changes** to any file under these paths. This bootstrap only added files under `.alveara/`.

Command run and result:

```
$ git diff --stat -- .colaberry/ CLAUDE.md docs/
(no output — zero diff)
```

## Production execution tracking

Production execution tracking for the 53 first-release items begins in `.alveara/EXECUTION_STATUS.json`, not `.colaberry/`. `.colaberry/progress.json` remains the sole authority for course-story (`STORY-*`) completion; `.alveara/EXECUTION_STATUS.json` mirrors course completion for sequencing purposes only and never overrides it.
