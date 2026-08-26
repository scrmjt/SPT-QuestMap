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

## 4. Rules that must not be "tidied" into Core

Both exist because Core is shared, and pushing browser-only concepts down breaks the plugin
silently — you find out when you launch the game, not when you build.

- **The map filter stays in the web layer.** `QuestVisibilityNode` carries no location data,
  and the in-game client has its own location controls.
- **`QuestVisibilityOptions.TraderId` stays a single `string?`.** The web filter is
  multi-select; the in-game UI picks one trader. A lone selection passes through Core
  unchanged, and multi-select is narrowed in the web layer afterwards.
