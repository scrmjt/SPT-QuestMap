# docs/ index — read this before opening anything in here

`docs/` holds ~250 KB of markdown. Loading the wrong file costs more context than the task.
**Pick from the table below; do not browse the directory.**

Files are marked:

- **CURRENT** — describes how the code works today; trust it, and update it when you change
  that area.
- **HISTORICAL** — a finished piece of work, kept for provenance. Useful for "why is it like
  this", useless for "how does it work now". **Do not load one to answer a how-to question.**
- **UPSTREAM** — belongs to `rootdarkarchon`'s contributor contract, not to this fork.

---

## Task → file

| If you are… | Load |
| --- | --- |
| changing **any** UI | invoke the skill **`questmap-ui`** (`<repo>/.claude/skills/questmap-ui/SKILL.md`) |
| editing the **Unity/BepInEx** UI | + `client-ui-invariants.md` |
| **a native EFT control stops responding**, or any bug inside EFT's own UI | `debugging-native-ui.md` — **before** changing code |
| editing the **Blazor** UI | + `web-ui-invariants.md` |
| changing **rules, filters, rankings, catch-up, series, objectives** | `domain-rules.md` |
| working on the **client↔server protocol** or client data flow | `bepinex-client-notes.md` |
| adding a **client-facing string** | `client-localization.md` |
| asking **"where did we leave off"** | `STATE.md` |
| asking **"what next"**, or picking up planned work | `roadmap.md` |
| **building, deploying, or cycling the server** | root `CLAUDE.md` — it has the commands |

If none of these match, the question is probably answered by the code. Grep first.

---

## Every file

### Current

| File | Size | What it is |
| --- | --- | --- |
| `STATE.md` | 5 K | Where the work stands, what is unverified, what is committed. **The handoff file.** |
| `roadmap.md` | 7 K | Work not yet started, with the open questions each item needs answered. Headline item: seeing a co-op partner's quests in raid. |
| `domain-rules.md` | 11 K | Quest-state, filter, ranking, series and objective rules that must not be "simplified" back. Covers both UIs. |
| `client-ui-invariants.md` | 13 K | Unity rules that compile fine and fail on screen. Read before touching `SPTQuestMap.Client/UI/`. |
| `debugging-native-ui.md` | 9 K | Playbook for bugs inside EFT's own UI: the diagnostic ladder, the `QUESTMAP_M06_*` markers and what each answers, EFT's tab-bar internals, and the seven wrong fixes that produced it. |
| `web-ui-invariants.md` | 7 K | Blazor/CSS/Razor equivalents. Read before touching `SPTQuestMap/Presentation/`. |
| `bepinex-client-notes.md` | 28 K | How the plugin gets data from the server, what compare mode costs in-game, worked-out findings §1–§5f. Sectioned — read the section, not the file. |
| `client-localization.md` | 3 K | Embedded JSON catalogs, and the CRLF / trailing-comma hazards. |
| `deployment.md` | 3 K | Deployment layout background. The actual commands are in root `CLAUDE.md`. |
| `testing.md` | 4 K | Test strategy. |
| `performance.md` | 9 K | Rendering-performance requirements; still binding. |
| `project-layout.md` | 2 K | Repository layout. |
| `state-model.md` | 15 K | Quest and profile state model — behaviour, provisional type names. |
| `architecture.md` | 13 K | Architectural guidance, deliberately not a class diagram. |
| `requirements.md` | 8 K | Product requirements. |
| `open-questions.md` | 1 K | Questions still to resolve. |

### Historical — provenance only

| File | Size | What it was |
| --- | --- | --- |
| `in-game-client-design.md` | **60 K** | Milestone 0 investigation for the native client. Superseded for day-to-day work by `bepinex-client-notes.md`. **The single largest file in the repo — do not open it casually.** |
| `blazor-migration.md` | 22 K | The migration to Blazor, completed. |
| `ingame-ui-branch-changes.md` | 20 K | The `ingame-ui` branch, merged into `main` 2026-08-19. |
| `acceptance.md` | 7 K | Acceptance checklist as of 2026-08-04. |
| `quest-actions-plan.md` | 14 K | Quest transactions — design only, not built. |
| `milestones.md` | 2 K | Original milestone list. |
| `visual-reference.md` | 1 K | Pointer to the static prototype in `reference/`. |
| `ingame-map/` | dir | Milestone M06 audit, shared-project contract, source leads. |
| `archive/` | dir | Superseded copies. |

### Upstream

| File | Size | What it is |
| --- | --- | --- |
| `development-status.md` | **37 K** | Upstream's required handoff document. `AGENTS.md` obliges you to maintain it. It is *their* file — it is not where this fork's state goes; that is `STATE.md`. Declares the 2.0 scope frozen. |
| `../AGENTS.md` | 8 K | Upstream's contributor contract: source-first rule, performance-as-a-requirement, register new `.csproj` files. Binding on anything headed for a PR. |

---

## Keeping this cheap

When you add a doc, add a row here. When a doc stops describing the present, move its row to
**Historical** rather than deleting it — a stale file that is *labelled* stale costs nothing;
an unlabelled one costs a session.
