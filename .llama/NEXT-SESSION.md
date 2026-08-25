# Work queue for the 04:00 session (2026-08-25)

Branch `llama-filters-refactor`. **The working tree has six uncommitted changes** from the
previous round (tile labels, filter tooltips, full-name filter chips, consolidated left
panel, per-map badges, `ShortLabel` length parameter). Build is clean, 309 tests green.

Set `SPT_ROOT=D:\SPT4013_Test` before any build. Deploy order is **stop server, deploy,
start** — see CLAUDE.md.

## 1. Carryover: the tile labels did not reach the browser (start here)

Quest tiles in the canvas graph still draw `A` / `B` instead of `Scram` / `Woulf`. The code
change is done and builds: `QuestGraphUpdate` carries `PrimaryLabel`/`ComparisonLabel`,
`applyConfig` reads them into `state.primaryLabel`/`state.comparisonLabel`, and
`drawCompareStatusLine` measures the label to size its badge.

`QuestMapRenderer.mjs` is an embedded static web asset inside `SPTQuestMap.dll`, not a loose
file in the deployment. Two untested hypotheses, in order of likelihood:

1. The browser cached the old ES module. Hard-reload with cache disabled and re-check.
2. The asset is not being re-embedded on build. Check the build output / the served
   `/questmap/...QuestMapRenderer.mjs` for the string `state.primaryLabel`.

Do not assume it is fixed without seeing `Scram` on a tile.

## 2. Carryover: verify what was never seen

- **Map badges.** Open the map dropdown: each location should show up to three count chips
  (primary / comparison / shared) with hover text. A location neither player needs shows no
  chips at all, deliberately.
- **Tooltips** on every comparison filter chip.
- **Focus mode**: the dropdown panel must not render while `Disabled`. Verified by code
  inspection only, never exercised.

Then commit the round.

## 3. New: fold the top bar into the left sidebar

Goal is to reclaim vertical space at the top of the page.

- Move the **filters** to the **top of the left sidebar**.
- Move **profile selection** (both profiles, the swap control) and **language selection**
  in there too.
- The top header bar should shrink or disappear entirely.

## 4. New: panels become views that push the graph right

- **Quests in Progress**, **Catch-Up Planner**, and the **graph** become views.
- Opening a panel **pushes the graph to the right** rather than overlaying it.

## 5. New: collapse the details pane

- The **"Select a quest"** pane stays collapsed until a quest is actually clicked.
- Maximise the content area for the graph, in-progress, and catch-up views.

## Delegation notes

`GEMINI_API_KEY` is set as a Windows User env var; `.llama/implement.py` takes
`--api-key --model --chat-path`. Gemini's OpenAI surface is
`https://generativelanguage.googleapis.com/v1beta/openai` with `--chat-path /chat/completions`.
Available: `gemini-3.1-pro-preview`, `gemini-flash-latest`. **Free tier — use sparingly**,
and expect transient 503s.

Heuristic established last session: **delegate when the spec would be shorter than the code.**
Layout restructuring across several components is the case where a big-context model earns
its keep, because it can read the files instead of having them described. Local llama needs
the file's entire frame spelled out or it invents `@inject` directives and non-existent types.

Verify by running, not reading. Unit tests do not render CSS: the last round shipped a
clipped, unusable dropdown that passed 309 tests.
