# SPT-QuestMap fork — Catch-Up Planner

Local fork of [rootdarkarchon/SPT-QuestMap](https://github.com/rootdarkarchon/SPT-QuestMap)
(cloned at release 2.0.0, not pushed to GitHub — plain local clone in
`C:\Users\<account>\projects\SPT-QuestMap`, no `origin` fork of our own yet).

## Goal

Eric wants a "catch-up planner": given two SPT profiles, figure out which quests a
lagging profile should play next to catch up to a more-advanced profile — prioritizing
whichever trader questline the lagging profile is furthest behind on.

Scope decision (asked and confirmed): **browser tool first**, not the in-game
BepInEx client. The browser tool (Blazor Server, `src/SPTQuestMap`) is testable without
a running EFT client; the in-game client (`src/SPTQuestMap.Client`) needs the actual
game installed and a deploy step, so it's a later phase if wanted.

## What's built (uncommitted, working tree only)

The browser tool already had a two-profile "Compare Mode" (side-by-side diff of quest
status) — that's what this hooks into, rather than a brand-new page.

- `src/SPTQuestMap.Core/Rules/QuestChainDepthCalculator.cs` — pure longest-path depth
  per quest over the quest DAG (Kahn's algorithm, ignores cycles/unknown ids so it can't hang).
- `src/SPTQuestMap.Core/Models/CatchUpModels.cs` + `src/SPTQuestMap.Core/Rules/CatchUpRules.cs`
  — pure ranking logic: groups quests by trader, computes each profile's max completed
  depth per trader, ranks chains by gap (comparison profile ahead of primary) descending,
  and lists the next N quests needed to close each gap.
- `tests/SPTQuestMap.Core.Tests/CatchUpRulesTests.cs` — unit tests for both of the above.
- `src/SPTQuestMap/Presentation/QuestMapPageState.cs` — `BuildCatchUpChains()` bridges the
  live `ProfileStateDto`/`QuestTopologyDto` server DTOs into the pure Core types above,
  called from `SetData()` whenever Compare Mode is active. New `CatchUpChains` /
  `CatchUpExpanded` state.
- `src/SPTQuestMap/Components/CatchUpPanel.razor` — new collapsible side panel (mirrors
  the existing "Quests In Progress" drawer), shown only in Compare Mode, listing ranked
  chains with next-quest recommendations and each quest's current display state.
- `src/SPTQuestMap/Models/QuestMapDtos.cs` — new `CatchUpChainDto` / `CatchUpQuestDto`.
- Wired into `src/SPTQuestMap/Components/Pages/QuestMap.razor` next to the in-progress drawer.
- New locale keys in `src/SPTQuestMap/Localization/Locales/en.json` (`catchup.*`) — only
  English has them; other locale files will fall back to showing the raw key until translated.
- New CSS in `src/SPTQuestMap/Presentation/Assets/QuestMap.css` (`.catchup-*` classes),
  panel docked to the right side of the viewport (in-progress drawer is on the left).

## Current state / what's NOT done

- **Not built or tested.** This machine has .NET runtimes only, no .NET SDK
  (`dotnet --list-sdks` is empty), so `dotnet build` / `dotnet test` can't run here.
  Everything above was hand-verified against existing code patterns and types but is
  unconfirmed. **First thing to do when resuming: install the SDK or move to a machine
  that has one, then `dotnet test tests/SPTQuestMap.Core.Tests` and build the full solution.**
- Not committed to git — `git status` in the repo shows the new/modified files above as
  working-tree changes only.
- No GitHub fork created yet (user chose local-clone-only for now).
- Chain-depth model is currently global-longest-path-per-trader-group, not a
  cross-trader-aware "true" dependency walk — good enough for the trader-questline
  framing the user asked for, but worth revisiting if quests that span traders turn out
  to matter a lot for the recommendation quality.
- No settings persistence for `CatchUpExpanded` (panel always starts collapsed).
- In-game client (BepInEx) port is an explicitly deferred phase 2, not started.

## Next steps

1. Get a .NET SDK available and build/test the solution — fix any compile errors.
2. Manually verify in the running browser tool with two real SPT profiles (there's a
   `reference/test-profiles/README.md` describing how to get test profile saves).
3. Decide whether to commit / push to a personal GitHub fork.
4. If satisfied, consider porting the Core logic into the in-game client.
