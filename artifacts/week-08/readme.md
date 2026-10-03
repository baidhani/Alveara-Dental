# Claude Code Automation Suite — Ledgerly

This directory contains three interconnected automation tools for the Ledgerly project: a post-story command, a pre-build validation hook, and a GitHub Actions CI reviewer. Together they enforce the Colaberry project workflow: build stories, tick tracking files, validate criteria, review on push.

## Demo Recording

[**Demo: All three automation pieces in action**](../../artifacts/week-08/ledgerly-automation-workflow-demo.mp4)
*(Shows story-done command, validate-story-criteria hook, and GitHub Actions reviewer working together)*

---

## 1. Story Completion Command

**File:** `commands/story-done.sh`

**What it does:**
- Automates the three-commit post-story workflow
- Accepts user input for: files_touched, tests_added, notes
- Auto-populates session ID from PROGRESS.md tail
- Updates `.colaberry/progress.json`: marks all criteria as passed, adds metadata (files, tests, notes, timestamp)
- Updates `PROGRESS.md`: creates a narrative entry with full template (date, session, what changed, verification, notes)
- Creates two atomic commits with proper trailers and attribution

**Usage:**
```bash
bash .claude/commands/story-done.sh <STORY-ID>
```

**Example:**
```bash
bash .claude/commands/story-done.sh STORY-017
# Prompts for files_touched, tests_added, notes
# Creates two commits:
#   1. "STORY-017: tick progress.json — all criteria passing"
#   2. "PROGRESS.md: log STORY-017 build"
```

**When to use:**
- After implementing and testing a story
- Before pushing changes
- To avoid manual JSON editing and PROGRESS.md formatting

**Effort saved:**
- ~5 min / story (manual JSON edit, formatting, session ID lookup) → ~2 min with command + edit template lines

---

## 2. Pre-Build Validation Hook

**File:** `hooks/validate-story-criteria.sh`

**What it does:**
- Compares `.colaberry/plan.json` acceptance criteria against `docs/stories/STORY-nnn.md` acceptance criteria
- Checks: criterion count (must match), text identity (byte-for-byte, no rewording)
- Fails loudly with side-by-side diffs if any mismatch
- Read-only: reports drift, does NOT auto-correct (human decides what to change)
- No git or code changes — only validation

**Usage:**
```bash
bash .claude/hooks/validate-story-criteria.sh <STORY-ID>
```

**Example:**
```bash
bash .claude/hooks/validate-story-criteria.sh STORY-005
# Output: "PASS: STORY-005 criteria match between plan.json and story brief (3 criteria, all identical)"
```

**If criteria drift:**
```
FAIL: CRITERIA TEXT MISMATCH: Found 3 criteria, but text differs

Criterion 1:
  plan.json:  "Given a new company setup request, when I enter company details, then the system creates a company profile and sends email."
  docs/stories/STORY-005.md: "Given a new company setup request, when I enter company details, then the system creates a company profile."

ACTION: Edit .colaberry/plan.json to match the story brief exactly before building.
```

**When to use:**
- Before starting a new story implementation
- To catch stale placeholder criteria before building and testing
- Integrates into CI or pre-push workflows (see workflow below)

**Exit codes:**
- `0`: All criteria match (safe to build)
- `1`: Mismatch found (stop, fix plan.json, retry)

---

## 3. GitHub Actions CI Reviewer

**File:** `.github/workflows/review-on-push.yml`

**What it does:**
- Runs on every `push` to main and every `pull_request` against main
- Type checks with `tsc --noEmit`
- Runs test suite with `npm test`
- Scans for exposed secrets (API keys, tokens, passwords, private keys)
- Posts findings as a comment on PRs or step summary on push
- Blocks merge if type errors, test failures, or secrets detected

**Checks performed:**
| Check | Failure condition | Blocks merge |
|-------|---|---|
| TypeScript types | `tsc` produces `error TS` | ✓ Yes |
| Tests | `npm test` fails | ✓ Yes |
| Secrets scan | API keys, tokens, private keys found | ✓ Yes |

**Output:**
- **On PR:** Posts comment with results (Type Check, Tests, Security Scan sections)
- **On push:** Summary in GitHub Actions step summary page
- **On any failure:** Returns exit code 1 (blocks workflow)

**Triggers:**
- `push` to `main` branch
- `pull_request` against `main` branch

**Time cost:** ~30-40 seconds per push (install deps + checks)

**Example comment output:**
```
## Code Review Results

### TypeScript Type Check
✅ **No type errors**

### Tests
✅ **Tests passed**

### Security Scan
✅ **No obvious secrets detected**
```

---

## How They Work Together

```
Story implemented
    ↓
Run: bash .claude/commands/story-done.sh STORY-XXX
    ↓
    ├─ Updates progress.json (marks criteria passed, adds metadata)
    ├─ Updates PROGRESS.md (narrative entry with session ID)
    └─ Creates two commits (progress.json tick + PROGRESS.md log)
    ↓
Before next build, run: bash .claude/hooks/validate-story-criteria.sh STORY-YYY
    ↓
    ├─ Reads plan.json criteria
    ├─ Reads story brief criteria
    └─ Compares: count + text identity
       → PASS: safe to build
       → FAIL: fix plan.json, then retry
    ↓
git push origin main
    ↓
GitHub Actions workflow runs automatically:
    ├─ tsc --noEmit (type check)
    ├─ npm test (unit + integration)
    ├─ Secrets scan (pattern matching)
    └─ Posts comment with findings (PR) or summary (push)
    ↓
    If any check fails: exit 1 (blocks merge)
    If all pass: exit 0 (ready to merge)
```

---

## Setup

These tools are ready to use. No configuration required.

### To use the command:
```bash
bash .claude/commands/story-done.sh STORY-001
```

### To use the hook:
```bash
bash .claude/hooks/validate-story-criteria.sh STORY-001
```

### To trigger the CI reviewer:
```bash
git push origin main
# Or create a PR against main
# Workflow runs automatically
```

---

## Files in This Directory

| File | Purpose |
|------|---------|
| `commands/story-done.sh` | Post-story workflow automation (progress.json tick + PROGRESS.md entry) |
| `hooks/validate-story-criteria.sh` | Pre-build criteria validation (plan.json vs. story brief) |
| `../workflows/review-on-push.yml` | GitHub Actions CI reviewer (types, tests, secrets, comments) |
| `README.md` (this file) | Documentation and usage guide |

---

## Troubleshooting

**Command: "Story ID not found in progress.json"**
- Verify the story ID exists in `.colaberry/progress.json`
- Check spelling (e.g., `STORY-001`, not `STORY-1`)

**Hook: "FAIL: CRITERIA TEXT MISMATCH"**
- Edit `.colaberry/plan.json` to match the story brief exactly
- Check for extra whitespace, rewording, or missing criteria
- Re-run the hook to confirm

**Workflow: "Type errors found" or "Tests failed"**
- Fix the issue locally
- Run `tsc --noEmit` and `npm test` to verify
- Commit and push again

**Workflow: "Secrets detected"**
- Review the flagged file
- If false positive (e.g., "API" in a comment), the pattern will skip it on next run
- If real secret: rotate it immediately, remove from file, rewrite git history if committed
- Push again

---

## Attribution

Created as part of Ledgerly automation suite (Colaberry Agent Project).

- **Command:** `story-done.sh` - Automates post-story workflow (progress.json + PROGRESS.md)
- **Hook:** `validate-story-criteria.sh` - Validates story criteria before build
- **Workflow:** `review-on-push.yml` - GitHub Actions CI reviewer with comment support
