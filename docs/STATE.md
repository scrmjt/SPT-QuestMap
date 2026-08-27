# Where the work stands

**Load this when** picking the work back up, or when you need to know what is verified and what
is merely built. It is the fork's handoff file — the churny facts live here so root `CLAUDE.md`
can stay small and stable.

**Last updated: 2026-08-27.** Branch `llama-filters-refactor`. Run `git log --oneline -3` for
where the tip actually is.

---

## Verification status

| | |
| --- | --- |
| Build | All three projects Release, **zero warnings** (`TreatWarningsAsErrors` is on). |
| Tests | **160** core + **195** web = **355**, all passing. |
| Web app | Verified in a browser against the real Woulf/ScramJet profiles. |
| Unity client | **Cannot be verified here at all.** Built and deployed only. |

Be exact about what "verified" means for the client — see the `questmap-ui` skill. Never report
a Unity UI change as *working*; report it as built and deployed, and say what is unchecked.

---

## Needs the user's eyes

These are done, deployed, and **unconfirmed on screen**:

1. **"Needle in a Haystack" in the web app.** After the multi-map fix it should appear under
   Streets of Tarkov only — not Ground Zero, Shoreline or Interchange. This is the exact case
   the user reported.
2. **Coop Maps and Coop Catch Up in-game.** Reworked after the first live run (2026-08-26) and
   not seen since. The tab bar itself rendered and highlighted correctly that round.
3. **Coop tab bleed-through.** Fixed (`RebuildGraph` now disposes `_coopView`); the symptom was
   Coop tabs showing through from behind the Tasks list.

---

## Committed, not pushed

Two commits on `llama-filters-refactor`, tree clean:

- `88759a9` — the multi-map objective fix in both UIs, the coop snapshot fields, the Unity
  tab-bar and disposal fixes, the `ru.json` repair, and four new/extended test files.
- the commit that added this file — the docs restructure.

**Nothing has been pushed anywhere.**

**Remotes:** `origin` = `scrmjt/SPT-QuestMap` (the user's own fork — push-safe).
`upstream` = `rootdarkarchon/SPT-QuestMap` — **never push there.**

---

## Deploy coupling

Coop Maps and Coop Catch Up need `CoopProfileSnapshot.ActiveQuestIds` and
`CompletedObjectiveIds` from the server, so **client and server must be deployed together**. An
older server leaves those null, which reads on screen as "no maps shared" and "no legs
finished" — a plausible-looking wrong answer, not an error.

---

## Open items

### Before any upstream PR

- `pr/catch-up-planner` is a clean single-commit branch off `upstream/main` that **predates**
  all of this work. Regenerate it from the final state; do not build on it.
- **Upstream's `docs/development-status.md` declares the 2.0 scope frozen** and says new
  features need separately approved post-2.0 scope. Raise an issue or discussion with
  rootdarkarchon before opening a PR.
- `CLAUDE.md`, `docs/STATE.md`, `docs/INDEX.md` and `.claude/` are **ours, not upstream's** —
  exclude them from any PR.
- **`docs/development-status.md` has not been updated with this fork's work, deliberately.**
  `AGENTS.md` obliges maintaining it, but it is upstream's 37 KB handoff file and this fork's
  state lives in `STATE.md` instead — loading theirs to record ours costs a session's context
  for no benefit while no PR is open. **Backfill it as part of preparing the PR**, not before.
- **Translations are parked until just before the PR.** Only `en.json` and `ge.json` carry the
  `catchup.*` keys; `GermanUiCatalog_TranslatesEveryNonInvariantDisplayString` enforces German
  specifically, and the other 15 locales fall back to English. Upstream may want the rest done.

### Known limits

- Never validated with **more than two profiles**, or with profiles at very different levels.
- Chain depth ignores cross-trader prerequisite chains (see `domain-rules.md` §3).
- Only ~30% of quests belong to a named series, so the rest appear only in trader gaps.
- The objective mix has no plural forms, and its counts describe what the quest asks for rather
  than what remains.

---

## Test profiles on this box

In `D:\SPT4013_Test\SPT\user\profiles\`:

| Nickname | Level | Quests | Completed | Id |
| --- | --- | --- | --- | --- |
| Woulf | 38 | 194 | 125 | `6a448ba904a64e2130386b7c` |
| ScramJet | 39 | 198 | 131 | `6a448bf004a64e2130386cfb` |
| ScramJe1 | 69 | 558 | **0** | `6a77dfa45efa88458028244c` |
| Regdab | 8 | 16 | 4 | `6a8ceb9217a0e4347883e178` |

`ScramJe1` (digit **one**) is a different profile from `ScramJet`, and its 0-completed column is
real data, not a bug — it cost a debugging session once already. `Regdab` at level 8 is the
mixed-level case that exposed the level-filter bug.

---

## Recent history

- **2026-08-27** — Multi-map objective fix landed in the web app (five call sites) after the
  same fix in the Unity client turned out to be the wrong surface for the user's report. Coop
  Map Helper got its first test coverage. Docs restructured for context economy.
- **2026-08-26** — First live in-game run of the Coop tabs. Three defects found and fixed; see
  `bepinex-client-notes.md` §5d.
