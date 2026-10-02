# Skills in this repo

<video src="../../artifacts/week-02/skills-demo.mp4" controls width="600"></video>

*Recording of all three skills below being invoked by plain requests that never name them, each firing on its own.* If the player above doesn't render, the file itself is at [`artifacts/week-02/skills-demo.mp4`](../../artifacts/week-02/skills-demo.mp4).

Three project-specific Skills live here, each for a genuinely different job.
None of them fire automatically — they're chosen by matching what you ask
for against each one's `description`, the same way any Skill works. None of
them change how routine story-building work happens; they're additions, not
replacements for anything in `CLAUDE.md` or `directives/`.

---

## api-smoke-test

**What it's for:** Runs the whole backend end-to-end against a real, running
server — every route, in dependency order (companies → accounts → journal
entries → posting, including posting twice to prove it's idempotent) — and
reports pass/fail per route.

**When it fires:** When you want to *see the system actually work right
now*, live, as a whole — not read about it, not run the automated test
suite, but watch real HTTP requests hit a real server.

**What it can touch:** Can run shell commands, start and stop the dev
server, and read source files to find the current list of routes. **Cannot**
edit or write any file — if a route is broken, it reports the failure, it
does not attempt a fix.

**Example request that should invoke it:**
> "Before I show this to my mentor tomorrow, can you check that the whole
> backend still works — companies, accounts, journal entries, posting, all
> of it, together?"

---

## plan-progress-audit

**What it's for:** Sweeps every story already marked done in
`.colaberry/plan.json` and `.colaberry/progress.json` looking for drift —
acceptance-criteria text that doesn't match between the two files, or a
criterion ticked `true` with no code or test actually backing it. This is
the whole-repo, periodic version of a mistake that happened once for real
(STORY-001 shipped correct code against the wrong criteria text).

**When it fires:** When your worry is *correctness* — "did we mark
something done that isn't really done," "did I forget to update a checklist
somewhere" — across everything already claimed done, not the story
currently being built. This is not about whether the code is well
organized; it's about whether the paperwork is telling the truth.

**What it can touch:** Only reads files and searches the codebase for
corroborating evidence. **Cannot** run any command and **cannot** write —
it produces a report; fixing anything it finds is a separate, deliberate
step you decide on afterward.

**Example request that should invoke it:**
> "Can you audit all my stories and check whether progress.json still
> matches the real acceptance criteria for each one?"

---

## release-boundary-retro

**What it's for:** A design reflection across everything built in one
release — not a bug hunt, not a test run. Looks for places where different
owners, risks, or change rates got collapsed into the same module (the kind
of thing that doesn't fail a test, but costs more to unwind the longer it
sits), and proposes one boundary worth drawing before the next release
builds on top of it.

**When it fires:** When your worry is *maintainability* — "is the code
quietly getting tangled in a way that will slow us down later," even though
every test still passes and nothing is currently broken. At the end of a
release (e.g. r0 wrapping up before r1 starts) — a checkpoint activity, not
something that runs mid-release or in place of a story's own acceptance
criteria.

**What it can touch:** Only reads files and searches the codebase. **Cannot**
run any command and **cannot** write or restructure anything — it produces
a proposal in words; acting on it is a separate step.

**Example request that should invoke it:**
> "r0 just wrapped — can we do the architecture retro before starting r1?"

---

## Quick disambiguation

If you're not sure which one applies, ask which worry you actually have:

- **"Does it work right now?"** — live, this moment, watch it run →
  `api-smoke-test`
- **"Did we lie to ourselves about what's done?"** — the checklist says
  finished, is that actually true → `plan-progress-audit`
- **"Is the code getting tangled, even though nothing's broken?"** — a
  slower-building concern about how future work will go, not about today →
  `release-boundary-retro`

A vague "is everything okay?" usually means the first two — start with
whichever of those two matches more closely, and ask a follow-up question
rather than guessing between "okay" meaning "working" versus "well-built."
