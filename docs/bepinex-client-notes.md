# BepInEx client — architecture notes and open findings

Working notes for the in-game (`SPTQuestMap.Client`) side, written up before starting work
there. Everything below was read out of the sources on branch `llama-filters-refactor`, not
recalled — file and line references are given so they can be re-checked after any refactor.

> Companion page (conceptual, shareable):
> <https://claude.ai/code/artifact/6ec329a8-4b80-4a9f-b9e8-d353e513064b>

## 1. How the three projects relate

| Project | Target | Role |
| --- | --- | --- |
| `SPTQuestMap.Core` | `netstandard2.1` | The rules and the wire models. No UI, no web, no Unity, no BepInEx imports. |
| `SPTQuestMap.Client` | `netstandard2.1` | The in-game screen. BepInEx plugin, Unity UI. References Core only. |
| `SPTQuestMap` | `net9.0` | The SPT server mod plus the Blazor page at `/questmap`. References Core only. |

`netstandard2.1` is the constraint that makes sharing possible: Tarkov is Unity on an older
.NET runtime, the SPT server is .NET 9. Core has to speak the subset both understand.

**Practical consequence, hit while adding `PoolCatchUpRules`:**
`ArgumentNullException.ThrowIfNull(x)` does not exist in `netstandard2.1` (CS0117). Write
`if (x is null) throw new ArgumentNullException(nameof(x));` instead. Expect to hit more of
these — modern C# conveniences frequently are not available in Core.

**The DTO split that matters for any shared feature.** The records in
`src/SPTQuestMap/Models/QuestMapDtos.cs` (`CatchUpChainDto`, `MapQuestCounts`,
`ObjectiveDefinitionDto`, …) live in the **net9.0** project, so the plugin cannot reference
them. The shared wire contract is `src/SPTQuestMap.Core/Models/QuestGraphModels.cs`
(`QuestTrader`, `QuestObjectiveDefinition`, `QuestRequirement`, `QuestMapReference`, …).
Anything both sides must exchange has to be defined in Core.

## 2. How the plugin gets its data

The plugin does **not** read profile files. It asks the local SPT server over HTTP, using
EFT's own `RequestHandler`.

| Piece | Location |
| --- | --- |
| Client fetch | `src/SPTQuestMap.Client/Data/ServerQuestTopologySource.cs:32` — `RequestHandler.GetDataAsync(LocalizedRoute(Route))` |
| Client routes | same file, `:25` `/questmap/client/topology`, `:26` `/questmap/client/repeatables` |
| Server handler | `src/SPTQuestMap/Routers/QuestMapClientStaticRouter.cs:25`, `:129` |
| Profile access | `src/SPTQuestMap/Services/QuestMapDataService.cs:19` holds SPT's `SaveServer`; `:73` `GetProfiles()` enumerates every profile on the machine |

### Can the in-game side do compare mode / catch-up?

**Yes, architecturally — the blocking work is UI, not plumbing.**

Already present:

1. `SaveServer.GetProfiles()` — the server can already see every profile; this is what the
   browser's compare mode is built on.
2. A working request/response channel the plugin already uses and parses.
3. Shared wire models in Core, so both sides deserialise the same JSON.
4. `CatchUpRules` and `PoolCatchUpRules` in Core, already unit-tested. They take facts and
   return answers and do not care which screen is asking.

Still to build:

5. **A compare route** returning other profiles' quest state, shaped as **Core** models
   (not the web project's DTOs — see the split above).
6. **Unity UI for the panel.** This is the real cost. The in-game screen is Unity UI; none
   of the Blazor components transfer.

## 3. Open bug: vanilla task screen flashes before the mod's list

**Symptom (reported from in-game):** opening the tasks screen briefly renders the original
EFT task page, then the mod's list appears over it. Reads as janky.

### Root cause

The takeover is deliberately deferred by two frames, and can be deferred much longer.

1. `src/SPTQuestMap.Client/Patches/GlobalTasksLifecyclePatch.cs:15` patches
   `TasksScreen.Show` as a **Postfix** — so the vanilla screen has already been built when
   the mod's code first runs.
2. `QuestScreenCoordinator.ShowGlobalTasksScreen` (`:88`) ends at `:159` by starting
   `MountPendingGlobalAfterVanillaLayout()`.
3. That coroutine (`:346`) is:

   ```csharp
   yield return null;                     // a full frame renders here
   yield return new WaitForEndOfFrame();  // and we wait past its render
   pending.VanillaLayoutReady = true;
   TryMountPendingGlobalScreen(pending.QuestController);
   ```

   So **at least one fully rendered frame shows the vanilla screen**, by design.

4. Worse on a cold open: `TryMountPendingGlobalScreen` (`:295`) returns early at `:306`
   when `topology`/`layout`/`overlay` are still null. The mount is then retried later via
   `TryMountPending`. Until server data arrives, the vanilla screen stays on screen — far
   longer than two frames.

**The wait is not a mistake.** The flag is literally named `VanillaLayoutReady` (`:300`,
`:354`, `:399`): the mod needs Unity to finish laying out the vanilla `RectTransform`s
before it can size its own panel. Mounting synchronously in the Postfix would give zero or
wrong sizes.

### Proposed fix — veil, don't hurry

Do not try to mount earlier. Instead stop those frames from *painting* vanilla:

1. In `ShowGlobalTasksScreen`, immediately before `StartCoroutine(...)` at `:159`, put a
   `CanvasGroup` on the screen root and set `alpha = 0` (also `interactable = false`,
   `blocksRaycasts = false`, so invisible vanilla buttons cannot be clicked).
2. Restore it once the mod has mounted — by then the mod has replaced the vanilla content,
   so revealing shows the mod's UI.

**Use `CanvasGroup.alpha = 0`, never `SetActive(false)`.** Deactivating the GameObject stops
Unity computing the layout the coroutine is waiting for — it would deadlock the very thing
that makes the wait necessary. Alpha keeps layout running while hiding pixels.

**A timeout is mandatory.** Because the mount can be deferred indefinitely waiting on server
data (step 4 above), an un-timed veil risks a permanently blank quest screen, which is far
worse than a flash. Reveal vanilla after a few seconds if the mount has not happened.

Reveal must also happen on every exit path, or the screen stays blank:

- successful mount (`:324`)
- the `catch` around `controller.Mount` (`:327`)
- `FailPendingGlobalScreen` (`:335`)
- `CloseGlobalTasksScreen` (`:162`)
- `Dispose`

**Caveat to check when implementing:** if EFT already puts a `CanvasGroup` on that root and
animates it (screen fade-in), restoring a captured alpha could fight that animation. Prefer
reusing an existing group and restoring the captured values; only add and destroy a
`CanvasGroup` when none was there.

**The cached-resume path does not need this.** `ShowGlobalTasksScreen` returns at `:133`
when a cached controller can resume — that path is synchronous, so there is no flash.

### Not yet verified

This fix is **designed, not implemented or tested.** Verifying it needs Tarkov actually
running; it cannot be checked from the server-side test suite, and nothing in the 316
automated tests covers Unity rendering order.

## 4. Idea: keep vanilla as a tab instead of replacing it

**Verdict: feasible, and mostly reuses machinery that already exists.** Not implemented.

The mod never destroys the vanilla list - it hides it and restores it:

| Piece | Location |
| --- | --- |
| Live reference to EFT's panel (reflection into a private field) | `GlobalTasksScreenController.cs:140` `TasksPanelField.GetValue(_screen) as TasksPanel` |
| Its description object | `:148` |
| Prior active state captured | `:155` `_tasksPanelWasActive`, `:156` `_tasksDescriptionWasActive` |
| Restored on teardown | `:449-453`, logged as `vanillaRestored=True` at `:454` |
| Only the mod's own roots destroyed | `:441-447`, matched by name `"QuestMapGraph"` |

The mod also already switches between multiple content views:

- `IGlobalTasksContentView` with two implementations, `InProgressQuestTableView` and
  `QuestGraphView`
- `_mode` (`GlobalQuestGraphMode.InProgress` / `Full`) at `:84`
- `ShowMode(mode)` at `:457`, view chosen at `:541`
- header chrome that already builds buttons (`BuildGlobalChrome`, `QuestWorkspaceChrome`)

So "a Tasks tab showing the vanilla list" is a third case in an existing switch: hide the
mod's graph root, `SetActive(true)` the panel the controller already tracks.

### Risks to check before doing it

1. **The mode enum is not exhaustively switched.** `:527`, `:541`, `:619` and `:622` all
   test `_mode == GlobalQuestGraphMode.InProgress`, so a third value falls into the "Full"
   branch by default. Adding an enum member compiles clean and misbehaves; audit each site.
2. **`RestoreNativeSidePanelLayout()` becomes re-entrant.** The mod mutates EFT's side panel
   layout, and today that restore runs only on dispose - once per screen close. A tab makes
   it run repeatedly, mid-session, which is a much harsher test of a path that has never had
   to be re-entrant.
3. **Everything here is reflection into private EFT fields** (`RequiredField("_tasksPanel")`).
   Already true, but a vanilla tab deepens the dependency on those internals still behaving
   after the mod has modified them.

### It supersedes the flash fix in section 3

If vanilla is a legitimate view rather than something being replaced, then showing it first
is not jank - it is the honest initial state, and the switch becomes user-driven instead of
a race against layout and server data. Prefer this over the `CanvasGroup` veil if the tab is
going to be built anyway; the veil is the smaller fix if the takeover model stays.

## 5. Planned: COOP MAP HELPER and CATCH UP buttons

These go in the **mod's own header strip** — the one that already has "Tasks" and "Quest Map"
(`GlobalTasksScreenController.BuildGlobalChrome`, the two `AddHeaderButton` calls for
`InProgressTab` at x=12 and `QuestMapTab` at x=130) — **not** beside the game's native task
selectors.

> An earlier attempt added tabs next to the vanilla "Active Tasks" / "Operational Tasks"
> toggles and was reverted (commit `40274af`, reverted immediately after). The technique is
> recorded in section 4 in case it is ever wanted; it is not the direction here.

### Decisions taken

- **Coop columns: one per profile, plus a single "all shared" column.** Not the full power
  set: subsets of N profiles are 2^N − 1, which is 15 columns at four players and 31 at five,
  against roughly a dozen map rows. Past three players nobody picks a map by which particular
  pair overlaps.
- **Build order: web UI first**, then the server compare route, then the Unity views. The Core
  rules carry over unchanged, and iterating in a browser does not require relaunching Tarkov.

### State

| Piece | Status |
| --- | --- |
| `CoopMapRules` — per-map, per-profile and all-shared counts | **Done**, 8 tests |
| `PoolCatchUpRules` — per-chain leader/laggard, steps-behind, maps by summed steps-behind | **Done**, 7 tests |
| Both views in the Blazor UI | **Done** |
| Server compare routes (`/coop/profiles`, `/coop/state/{ids}`) | **Done**, verified live with curl against real profiles |
| Unity views ("Coop Catch Up", "Coop Maps" header buttons) | **Done, built and deployed — never run in Tarkov** |

`CoopMapRules` deliberately treats a quest as shared only when **every** selected profile still
needs it; a profile that cannot take the quest at all excludes it, because the group cannot
close it out together. Guarded by
`CoopMapRulesTests.BuildRows_ExcludesQuestsAProfileCannotTakeFromTheSharedCount`.

### 5b. What actually got built for the Unity side, and where it differs from the web app

Two new header buttons, next to Tasks / Quest Map: **Coop Catch Up** and **Coop Maps**
(`GlobalQuestGraphMode.CoopCatchUp` / `.CoopMapSuggestions`, `GlobalTasksScreenController`).
Both render through a new shared widget, `CoopListView` - a plain scrolling list of titled
sections, deliberately not the node-graph machinery `QuestGraphView`/`InProgressQuestTableView`
use, since Coop content is a read-only summary with nothing to pan, zoom or select. A profile
picker (`BuildCoopPicker`) sits above the list; the same selection feeds both views.

**Coop modes never touch `BuildProjection`, `_graphView`, the trader strip, or the graph's own
filter row.** This is what makes the four "not exhaustively switched" `_mode ==` sites from
section 4's original risk list a non-issue: none of that code ever runs for a Coop mode,
because `ShowMode` branches to `RebuildCoopView()` instead of `RebuildGraph()` before any of it
is reached. The one site that genuinely needed a new branch was the Future/Finished/Level vs
Repeatables/HideCompletedTasks row in `BuildGlobalChrome`, which was an `if (Full) {...} else
{...}` that would otherwise have shown the InProgress-only buttons in Coop mode too.

**Async fetches follow this codebase's own established discipline, not a direct `await`.**
Every other async load in this project (`QuestMapDataRuntime.LoadAndOverlay`) starts a `Task`,
then polls `task.IsCompleted` from a `StartCoroutine`-driven coroutine one frame at a time, so
nothing that touches Unity UI is ever reached off the main thread. The two new fetches
(`LoadCoopProfilesCoroutine`, `LoadCoopSnapshotsCoroutine`) do the same. This needed threading
`QuestScreenCoordinator`'s existing `_coroutineOwner` MonoBehaviour into
`GlobalTasksScreenController`'s constructor, since that controller had never previously run
async work of its own.

**"Chain Progress" is pairwise per teammate, looped; "Coop Maps" is genuinely N-way.** This
mirrors the web app's own split: the web Catch-Up Planner's Chain Progress and per-player
catch-up maps are pairwise (primary vs the one active comparison profile,
`QuestSeriesRules`/an app-layer filter), while its separate Coop Map Helper is the N-way pool
(`CoopMapRules`). Coop Catch Up loops the pairwise comparison once per selected teammate rather
than forcing a pool concept onto something that is naturally two-sided.

**Simplifications versus the web version, made deliberately for a first pass:**

- No trader-chain-gap sub-feature (the "next quest per trader" list). Only named-series chain
  progress and catch-up-by-map, which were the two examples asked for.
- "Outstanding" for a teammate means "not completed", full stop - the server only ever tells
  the client what another profile has *finished* (see section 2's minimality argument), not
  whether an unfinished quest is available, in progress, or gated. The local player's own
  richer state is still used for their own side of every comparison.
- Rows are **not clickable**. Selecting a quest to jump to it on the graph would mean
  switching `_mode` mid-click and reconciling that with the Coop surface's own lifecycle -
  adjacent complexity to what section 4 was written to warn about, and out of scope for
  read-only summary lists in a first pass.
- The picker is a flat multi-select shared by both views; there is no separate "who am I
  catching up to" vs "who is in my pool" distinction the way the web app's compare-pair and
  N-profile pool are conceptually different pools. Simpler, and defensible for a first
  version, but worth reconsidering if people want to compare against one teammate on Catch Up
  while pooling a different, larger group on Maps.

**Not yet verified in-game.** Every piece above compiles, the full solution builds with zero
warnings, and the two server routes were proven live against real profile data with curl (see
section 2). None of that proves the Unity layout looks right, that the picker chips are usable,
or that switching between all four header tabs behaves - there is no way to check any of that
without actually opening Tasks in Tarkov and looking.

### 5c. Review of the Coop work, 2026-08-26

A line-by-line review of everything built in section 5b, done because none of it can be run.
Sixteen defects were found and fixed; all 339 tests pass and the three projects build with zero
warnings. The durable rules are written up in **`docs/client-ui-invariants.md`** — this is the
record of what was actually wrong.

**The serious one.** `BuildGlobalChrome()` is graph-only by construction: it starts with
`if (_graphView is null) return;` and then does `_graphView.Root.Find("Header")`. The tab bar
therefore lives *inside the graph view's root*, and `RebuildCoopView()` disposes that root.
Entering either Coop tab **destroyed all four tab buttons**, and `ShowMode`'s
`if (_mode != mode)` guard made clicking the already-active tab a no-op — so the screen was a
dead end until the player closed and reopened it. Fixed by extracting `BuildModeTabs(header)`
and having every view draw the tab bar onto its own header.

This one also shows why the review had to be done as a whole rather than finding-by-finding:
the `CanResume` fix below, shipped alone, would have made the trap *permanent* (the Coop view
would resume instead of cold-remounting, and the mode is persisted across restarts).

**Lifecycle — the four methods were all written graph-shaped and never revisited:**

| | Was | Now |
| --- | --- | --- |
| `CanResume` | `_graphView?.Root != null` → never true in Coop, so every reopen cold-remounted and dropped the player back on Tasks | also accepts `_coopView` |
| `Mount` | called `BuildGraphOnFullSurface` unconditionally, so a *persisted* Coop mode built a graph under a Coop-highlighted header | branches on `IsCoopMode` |
| `Suspend` | never deactivated the Coop root | deactivates it |
| `RefreshOverlay` | early-returns when `_graphView is null`, so `Resume` never handed the Coop path a new overlay | `Resume` takes the overlay itself in Coop mode |
| `Dispose` | orphan sweep matched only `"QuestMapGraph"`, so the Coop root opted out of the safety net | matches `"QuestMapGraph" or "QuestMapCoop"` |

**Correctness.** The Coop builders read `_overlay.AuthoritativeDisplayStates` directly. That
dictionary is the last *server snapshot*, and `QuestGraphRules.ClassifyProfileDisplayState`
deliberately lets live in-session state win over it — so a quest handed in this session still
counted as outstanding, and the `Available`-without-a-live-quest correction was skipped
entirely. Now goes through `IsCompletedForLocalProfile`.

**Staleness.** Teammate snapshots were fetched once and cached for the controller's whole life,
so a teammate who played in between read stale indefinitely. Now cleared and re-fetched when
the screen is reopened.

**Determinism.** Sections were built by iterating `_coopSelectedProfileIds`, a `HashSet` whose
enumeration order is unspecified — they could reshuffle between refreshes for no reason. Now
ordered through `_coopProfiles`. Section GameObjects were also named from a running float
offset (`Section-{y}`) rather than `Section-{index}`.

**Performance.** `ApplicableQuestIds` is a plain array, so `.Contains()` is a linear scan; it
was being tested once per node, once per teammate — six figures of string comparisons per
rebuild. Hoisted to `HashSet`s outside the loops.

**Unity rect maths.** Every row in `CoopListView` set `anchoredPosition.x` on a
horizontally-stretched rect, where it shifts *both* edges rather than indenting. A sweep of
every non-zero `anchoredPosition.x` in the client confirmed `CoopListView` was the only
offender. Fixed by adding `UnityUiFactory.CreateListRow`, which now owns that geometry.

**Deferred `Destroy`.** `Destroy()` only takes effect at end of frame, so a clear-then-rebuild
draws the outgoing absolutely-positioned rows on top of the incoming ones. A sweep of all ~50
`Destroy` call sites found the codebase already disciplined about this *except three lines*:
`CoopListView.ClearContent`, `InProgressQuestTableView.RebuildAllRows`, and both clear loops in
`RaidTrackedQuestListView.Rebuild` — the last being the in-raid overlay, where it would have
been visible. All now `SetActive(false)` first.

**Smaller items.** The picker chips were bare `Toggle`s (Selectable tint fighting the colour
set on the Image, no EFT click/hover sounds) — now `UnityUiFactory.AddButton`. The picker host
had no `RectMask2D`, so chips spilled into the scrolling content once there were more than a
handful of profiles. The veil timeout coroutine was overwritten without being stopped. And
`ProgressTaskRowHeight` became dead when the objective rows went single-line — removed from
`QuestTableLayoutRules`, the client, and the test that pinned it.

**Still unverified.** None of this changes the headline: no part of the Coop UI has been seen
on a screen. What is now genuinely proven is that it compiles, that the shared rules are
covered by tests, and that the server routes return real data. The layout, the chip sizing, and
the behaviour of switching between all four tabs are unchecked.

### 5d. First in-game run, 2026-08-26

The first time any of this was rendered in Tarkov. The tab-bar fix from 5c held — all four
tabs drew on both Coop screens and highlighted correctly — and three further problems showed
up that no amount of reading was going to find.

**Coop tabs bleeding through the Tasks list.** `RebuildGraph()` never disposed `_coopView`, so
the Coop root stayed in the hierarchy behind the newly built Tasks view. Since 5c gave that
root its own tab bar, its tabs were what showed through. Fixed, and generalised as invariant
§1b: disposal between views has to be symmetric.

**Coop Maps counted every incomplete quest.** "Outstanding" meant `!completed`, which for a
normal profile is ~400 of ~560 quests, so every map showed a few hundred and the ranking was
meaningless. This was a data-model limit, not a display bug: the server only ever sent
completed ids per teammate, so the client *could not* tell available from locked. Fixed by
adding `CoopProfileSnapshot.ActiveQuestIds` (Available / InProgress / ReadyToFinish) to the
route, and counting only those. Maps with nothing shared are now dropped entirely, and each
map reports the shared count plus per-player totals with a unique-to-them count —
`CoopMapRules` already computed all of that, it was only ever being fed the wrong facts.

**The Catch Up numbers were unreadable, but correct.** `6/25 · 0/25` is two fractions with the
same denominator joined by a middot, with the two sides only identified in a subtitle. Now
`6 vs 0 of 25` under "Furthest chain part reached - you vs {profile}".

Worth recording: the `0/N` column looked like a bug and was not. Reading the profile JSON on
disk showed `ScramJe1` — a *different* profile from the player's own `ScramJet`, one character
apart — is level 69 with all 558 quests unlocked and **none completed**. The UI was right; the
name was misread off a screenshot. Checking `SPT/user/profiles/*.json` against
`SPT_Data/database/locales/global/en.json` for quest names is the cheap way to settle "is this
number wrong or is the data like that", and is much faster than reasoning about the builders.

**Found on the way:** `ru.json` had been committed with a missing comma, which made the entire
Russian catalog throw on load (see invariant §13). Now guarded by `ClientLocaleCatalogTests`.

### 5e. Multi-map quests counted against maps they no longer needed

Reported from the Catch Up planner's "Best Maps": "Needle in a Haystack" kept showing as
catch-up work on Ground Zero, Shoreline and Interchange after every leg but Streets was done.

Both Coop builders used `NodeTaskLocations(node)`, which flattens **all** objectives' locations
with no regard for which are finished. That is the right answer for filters and map badges and
the wrong one for "where do I still have to go" - objectives complete independently, so a
four-map quest becomes a one-map quest as it progresses.

Fixed with `QuestObjectiveMapRules.OutstandingTaskLocations(quest, completedObjectiveIds)`,
which drops objectives already done and keeps `NodeTaskLocations`' fallbacks (a quest with no
objectives still belongs to its own location; remaining objectives with no map contribute
nothing, which is correct for a pure hand-in).

Completion data per side:

- **Local player** - already present, `QuestLiveState.Objectives[].Complete`.
- **Teammates** - was not being sent. Added `CoopProfileSnapshot.CompletedObjectiveIds`,
  a flat set of finished objective ids. Objective ids are globally unique condition ids, so no
  per-quest nesting is needed, and the same shape serves both sides.

The rule went into **Core rather than the controller**, which is the point worth remembering:
it is a fact about quest data, not a Unity concern, and Core is the only place in this project
where client behaviour can actually be tested. `QuestObjectiveMapRulesTests` now pins seven
cases, including the reported one verbatim (four legs, three finished, expect Streets only) and
the two that are easy to get wrong: a map with two objectives must survive one of them being
finished, and a quest with everything done must belong to no map at all.

### 5f. The same bug again, in the web app

The Needle in a Haystack report was reproduced from a screenshot of the **Blazor web app**, not
the client - 5e had fixed only the Unity side. The web app carries its own copy of the defect,
in five places, none of which share code with the client.

Where the client uses `NodeTaskLocations`, the web app uses `_nodeMaps`, built once per
`SetData` from `QuestNodeDto.ActualMaps`. `ActualMaps` is itself
`objectives.SelectMany(o => o.TaskLocations)` with special locations dropped, so its ids line up
with objective locations and the same filtering applies. The five callers, all asking "where
does this player still have to raid" and all answering with "every map this quest mentions":

| Surface | Symptom |
| --- | --- |
| `BuildPlayerCatchUpMaps` | the reported one - "Best maps for X to catch up" |
| `BuildCoopMapRows` | Coop Map Helper pool counts |
| `BuildMapQuestCounts` | the map-filter badge numbers |
| `BuildCatchUpMapRankings` | catch-up map ranking from chain next-quests |
| `BuildSharedMapRankings` | shared-map ranking facts |

All five now go through `QuestMapPageState.OutstandingMaps(questId, state)`. The last three
needed the per-player split spelled out: two profiles finish different legs, so "shared" has to
mean a map where **both** still have work, not a map both quests merely mention.

`OutstandingMaps` falls back to the unfiltered list whenever there is nothing to refine with -
no per-objective progress on the state, or a topology whose objectives carry no locations - so a
data gap can never silently drop a quest off every map. That fallback is also why all 189
pre-existing tests kept passing untouched: the fixture built nodes with `ActualMaps` but no
objectives.

**The tests were verified to fail without the fix.** Worth doing deliberately here, because the
first version of `EachPlayersRemainingMapsAreTrackedSeparately` passed against the broken code:
it gave both profiles the same in-progress quest, and a quest only enters a player's catch-up
list when the *other* player has completed it, so the quest appeared in neither list and the
"should not contain" assertions were vacuous. Rewritten with one quest in each direction, both
new tests fail on the old behaviour and pass on the new.

**Lesson worth keeping:** these two UIs share questions, not code. A "which maps" fix in one is
a prompt to grep the other, and a bug report should be pinned to a surface before it is fixed -
the screenshot's URL bar (`127.0.0.1:6969`) was the tell, and 5e went to the wrong one.

## 4. Rules that must not be "tidied" into Core

Both exist because Core is shared, and pushing browser-only concepts down breaks the plugin
silently — you find out when you launch the game, not when you build.

- **The map filter stays in the web layer.** `QuestVisibilityNode` carries no location data,
  and the in-game client has its own location controls.
- **`QuestVisibilityOptions.TraderId` stays a single `string?`.** The web filter is
  multi-select; the in-game UI picks one trader. A lone selection passes through Core
  unchanged, and multi-select is narrowed in the web layer afterwards.
