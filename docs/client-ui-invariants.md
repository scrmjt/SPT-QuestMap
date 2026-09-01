# BepInEx client UI — invariants that will bite you

This file exists because **the Unity client cannot be tested here.** There is no Editor, no
way to launch Tarkov from this machine, and no way to see the screen. Every rule below was
either paid for with a real bug or proven by reading the surrounding code. Treat them as
load-bearing, not style preferences.

The only feedback channels are `D:\SPT4013_Test\BepInEx\LogOutput.log` (grep `QUESTMAP_`) and
screenshots the user takes. Assume anything not covered by a rule here is unverified.

Read this before touching anything under `src/SPTQuestMap.Client/UI/`.

---

## 1. Each view owns its own header, so each view must draw its own tab bar

**The tab bar is not global.** `QuestGraphView` and `InProgressQuestTableView` each create a
child called `Header` inside their own root, and `CoopListView` creates its own. Switching
modes disposes the outgoing view's root — **and its header, and every tab button on it.**

`BuildGlobalChrome()` starts with `if (_graphView is null ...) return;` and then does
`_graphView.Root.Find("Header")`. It is graph-only by construction.

> A Coop view that did not draw the tabs destroyed the tab bar on entry and left **no control
> to switch away from it**. `ShowMode`'s `if (_mode != mode)` guard then made clicking the
> already-active tab a no-op, so the screen was a dead end until the player closed and
> reopened it.

**Rule:** any new full-surface view must call `BuildModeTabs(header)` against its own header
rect. If you add a mode, add it to `BuildModeTabs` — that one method is the whole tab bar.

## 1b. Disposal between views must be symmetric

`RebuildCoopView()` disposed `_graphView`. `RebuildGraph()` did **not** dispose `_coopView`.
The result on screen: switching Coop → Tasks left the Coop root alive in the hierarchy, and
because (per §1) that root carries its own copy of the tab bar, its tabs showed through from
behind the Tasks list.

**Rule:** every method that mounts a view must dispose *every other* view, not just the one of
its own kind. Grep for the other view's field before adding a new `Rebuild*` method. The
asymmetry is invisible in code review precisely because each method looks correct on its own.

## 1c. The view mode is persisted BY NAME, so removing one strands a saved setting

`GlobalTasksGraphSettingsStore` round-trips the mode through `Enum.TryParse` on
`settings.Mode.ToString()`. Deleting or renaming a `GlobalQuestGraphMode` member therefore
does not just change code - it orphans every setting an earlier build wrote.

When the two Coop tabs were merged into one, `CoopCatchUp` and `CoopMapSuggestions` were kept
in the enum purely so an old saved value still parses, and `NormalizeMode` folds them onto
`Coop` immediately after `LoadSettings()`. Without that fold the restored mode is one no tab
binds to any more: `IsCoopMode` is true so a Coop view builds, but no tab renders as active,
and the tab that would return you to it does not exist.

**Rule:** add a mode freely; never remove one. Fold retired modes onto their replacement at
load, and keep the old member with a comment saying why it survives.

**Corollary - the Sherpa tab is called `Coop` in code.** That tab reads **SHERPA** in game, and the
only occurrence of that word in the repository is the value of `label.coop` in `en.json`
(`label.tasks` and `label.questMap` likewise front `InProgress` and `Full`). Renaming the enum member
to match would strand every saved setting, per the rule above, so it stays `Coop` - which
means grepping for a tab by its on-screen label will miss it. Start from
`GlobalQuestGraphMode` and `BuildModeTabs` instead.

## 1d. Leaving the Tasks tab trips none of the signals you would expect

Switching to a sibling **Character** tab - ACHIEVEMENTS, MAP, PRESTIGE - fires nothing the
client can see. Each of the three obvious signals was tried and each was wrong. All four
statements below are measured, from `QUESTMAP_M06_*` log lines, not inferred from source:

| Candidate signal | What actually happens |
| --- | --- |
| `TasksScreen.Close` -> `Suspend()` | Never called. SUSPEND appears only for **bottom-nav** changes, never a top-tab switch. |
| Screen GameObject deactivated | Stays active, so `activeInHierarchy` never goes false. |
| Our surface covering the tab strip | It is not. An `EventSystem.RaycastAll` over the strip returns EFT's own MAP tab as topmost, identically in Coop and in the graph modes. |
| Surface parented outside the screen | False - `rootUnderScreen=True`. It **is** a child, which is why deactivating the screen would have been enough had EFT ever done it. |

So the clicks always worked and EFT really did switch tabs. Our surface was simply still
painted on top of the new one, and the Tasks tab button still lit.

> Reported three ways over two sessions - "I can't go to any other main tab", "the Tasks tab
> sticks so other tabs appear behind the QuestMap screens", and "it's still stuck on the tasks
> tab after I click COOP". One defect, three framings, and the first framing sent two fixes
> after an input-blocking rect that never existed.

**The signal that does work** is EFT's own tab toggle. `ResolveNativeTabToggle` latches, at Mount
and Resume, whichever tab under `Tab Bar/Tabs` is currently on - by definition Tasks, since that
is the only reason the controller mounts - and `SyncSurfaceVisibility` mirrors it every frame.
Latching by **state** rather than by the name "Tasks" keeps it locale- and build-independent;
it must never be latched from the per-frame watch, where the selected tab may well be MAP.

Every test in that watch is additive, so an EFT change degrades to today's behaviour rather
than to a broken one: an unresolved toggle gates nothing. It also deliberately does **not**
test a CanvasGroup alpha - the coordinator's anti-flicker veil drives alpha to 0 on this very
screen during mount, so reading it would hide the surface while it is being built.

**Rule:** a screen's lifecycle patches tell you when the screen opens and closes, not when it
stops being the visible one. For "is my content still what the player is looking at", read the
native control that decides it.

**Method rule, earned the hard way:** when a UI symptom survives two fixes, stop reading source
and ask the runtime. One `EventSystem.RaycastAll` line disproved a week of plausible reasoning.

## 2. `Destroy()` is deferred to the end of the frame — `SetActive(false)` first

Rows in these views are **absolutely positioned**, not laid out by a layout group. A
destroyed-but-not-yet-collected row still renders (and still occupies its slot) for the rest
of the frame, so a clear-then-rebuild draws the old set on top of the new one. That is the
"garbled frame"/flicker class.

```csharp
foreach (Transform child in _content)
{
    child.gameObject.SetActive(false);   // <- required
    UnityEngine.Object.Destroy(child.gameObject);
}
```

Every clear-then-rebuild site in the client now follows this. `InProgressQuestTableView`'s
row-diff path goes further and wraps the whole rebuild in
`_content.gameObject.SetActive(false)` / `finally { ... SetActive(true); }`, which is stronger
still and is the pattern to copy for anything expensive.

**Exempt:** disposing a root that nothing replaces in the same frame.

## 3. `anchoredPosition.x` means nothing on a horizontally-stretched rect

With `anchorMin.x = 0` and `anchorMax.x = 1`, Unity derives the edges as:

```
offsetMin.x = anchoredPosition.x - sizeDelta.x * pivot.x
```

so `anchoredPosition.x` shifts *both* edges and `sizeDelta.x` is a **width delta, not a
width**. Setting `anchoredPosition.x = 10` to "indent by 10" instead slides the whole row and
leaves the right edge overhanging its parent.

**Rule:** for a full-width row, use `UnityUiFactory.CreateListRow(name, parent, top, height,
left, right)`. It sets the anchors, pivot and `sizeDelta` correctly and takes insets in the
units you actually mean. Only hand-roll a rect when the element is genuinely not stretched
(a right-aligned value column pinned with `anchorMin = anchorMax = (1,1)` is fine).

## 3b. A decorative Image swallows every click over its rect

Unity defaults `Image.raycastTarget` to **true**. A background tint set on a full-surface rect
therefore absorbs input across that whole rect, including any native UI underneath it - and
alpha is irrelevant, so a 0.10-alpha tint you can barely see blocks just as hard as an opaque
one. The client sets `raycastTarget = false` on decorative images in twenty-odd places for
exactly this reason; `UnityUiFactory` does it for the images it creates.

> The Coop view's root, header and viewport backgrounds were created with a bare
> `AddComponent<Image>().color = ...` and kept the default, so they are now set explicitly.
> Worth doing on its own merits - but **it fixed nothing**, and the reasoning that led there was
> wrong. The surface was blamed for the player being stuck on the Coop view; a hit test then
> showed EFT's own MAP tab receiving that click perfectly well. See [1d](#1d-leaving-the-tasks-tab-trips-none-of-the-signals-you-would-expect)
> for what was actually happening.

**Rule:** every Image that exists only to paint something gets `raycastTarget = false`. Leave it
on only where the element must actually receive input - a ScrollRect's viewport (its drag
target), a scrollbar, or a real button's own graphic.

**Symptom to recognise:** input works in one region of the screen and not another, split
spatially rather than by control - a covering rect, not a broken handler.

**Confirm it before acting on it.** That symptom also describes a control that receives the
click and does its job while stale content stays painted on top, which is what actually
happened here and looks identical from the player's chair. One `EventSystem.RaycastAll` at the
dead coordinates separates the two in a single line of log, and costs far less than a fix
aimed at the wrong cause.

## 4. Read quest state through `QuestGraphRules`, never the raw dictionary

`QuestProfileOverlay.AuthoritativeDisplayStates` is the **last server snapshot**.
`QuestGraphRules.ClassifyProfileDisplayState` deliberately lets live in-session state win
over it:

```csharp
var exact = ClassifyDisplayState(state?.ExactStatus, state?.HasLiveQuest == true, node.Restartable);
if (TryClassifyLiveDynamicState(exact, out var liveDynamic)) return liveDynamic;   // live wins
if (overlay.AuthoritativeDisplayStates.TryGetValue(node.Id, out var authoritative)) { ... }
```

Reading the dictionary directly misses quests handed in **this session** and skips the
`Available`-without-a-live-quest correction. Use `IsCompletedForLocalProfile(node)` /
`ClassifyProfileDisplayState`.

**The one legitimate raw read** is a *change detector* comparing a prior snapshot to a current
one (`QuestOverlayChangeDetector`, `InProgressQuestTableView.HasAuthoritativeChange`) — those
genuinely want the snapshot, not the resolved state.

Note `default(QuestMapDisplayStateKind) == Locked`, so a missing key fails safe (hidden), not
open.

## 5. Never `await` on a path that touches Unity

Unity objects are main-thread only. The established discipline — `QuestMapDataRuntime`,
`LoadCoopProfilesCoroutine`, `LoadCoopSnapshotsCoroutine` — is: start the `Task`, then poll
`task.IsCompleted` one frame at a time from a `StartCoroutine` coroutine, and touch UI only
after it completes.

This is why `GlobalTasksScreenController` takes a `_coroutineOwner` MonoBehaviour in its
constructor.

## 6. Lifecycle: `Mount` / `Resume` / `Suspend` / `Dispose` must each handle every mode

Two separate bugs came from these four methods being written graph-shaped and never revisited
when a non-graph view was added:

- `CanResume` was `_graphView?.Root != null`, so a Coop view was never resumable — every
  reopen cold-remounted and silently dropped the player back on Tasks.
- `Mount` called `BuildGraphOnFullSurface` unconditionally, so a **persisted** Coop mode
  (the mode is saved and restored across restarts) built a graph while the header highlighted
  a Coop tab.
- `Suspend` never deactivated the Coop root.
- `RefreshOverlay` early-returns when `_graphView is null`, so the Coop path has to take the
  new overlay itself or the local player's side of every comparison goes stale.

**Rule:** when adding a mode, walk all five of `Mount`, `Resume`, `Suspend`, `Dispose`,
`RefreshOverlay` and ask what each does when your view is the mounted one. `IsCoopMode(mode)`
is the existing branch point.

## 7. `Dispose`'s orphan sweep matches on root **name**

```csharp
_screen.GetComponentsInChildren<RectTransform>(true)
       .Where(rect => rect.name is "QuestMapGraph" or "QuestMapCoop")
```

This is the safety net for a root that was created but never assigned to its field (an
exception mid-build). A new view with a new root name **silently opts out of it**. Either
reuse an existing name or add yours to the sweep — `TraderTasksScreenController` has its own
two-name copy of this.

## 8. Prefer `UnityUiFactory.AddButton` over a bare `Toggle`

`AddButton` sets `colors.normalColor = Color.white` so the Image's own colour shows untinted,
and attaches `QuestMapButtonFeedback` for EFT's click/hover sounds. A raw `Toggle` gets
neither: its Selectable tint fights any colour you set on the Image, its checkmark graphic
goes unused, and it is silent. For a chip whose selected state you paint yourself, a button
plus your own state is strictly simpler.

Also note `AddText`: if the GameObject **already has a `Graphic`**, it creates a stretched
`Label` child; otherwise it adds the TMP component directly. That changes what the returned
component's parent is — relevant if you then position it.

## 9. Determinism: never iterate a `HashSet` to build ordered UI

`HashSet<T>` enumeration order is unspecified. Building sections straight from
`_coopSelectedProfileIds` let them reshuffle between refreshes for no visible reason. Order
through the source list instead — `OrderedSelectedCoopProfileIds()` projects the selection
through `_coopProfiles`.

Same class of bug: naming elements `Section-{y}` from a running float offset rather than
`Section-{index}` produces unstable, colliding names.

## 10. Watch the accidental O(n²)

`QuestGraphTopology.ApplicableQuestIds` is a **plain array** (`.ToArray()`), so `.Contains()`
is a linear scan. Testing it once per node over ~500 nodes, once per teammate, is six figures
of string comparisons per rebuild. Hoist a `HashSet` outside the loop —
`ApplicableQuestIdSet()`, and `completedByProfile` in the map-suggestion builder.

## 11. A scrolling axis clips the other axis

A `RectMask2D` / `ScrollRect` on one axis will cut absolutely-positioned children on the
other. If something is mysteriously chopped off at a container edge, this is usually why.
`CoopListView` puts the mask on the picker host deliberately, so overflowing chips are clipped
rather than spilling into the scrolling content below.

## 12. "Not completed" is not "can do now"

Roughly 500 of ~560 quests are incomplete for any normal profile, because almost everything is
locked behind level and prerequisites. Any count built from `!completed` is therefore close to
constant and says nothing useful — the first Coop Maps build showed 300-400 "outstanding"
quests on every map, which the player correctly read as noise.

For "what can we do together", the state must be **Available / InProgress / ReadyToFinish**.
For the local player that is `IsActionableForLocalProfile`; for a teammate it needs the server
to send it, which is what `CoopProfileSnapshot.ActiveQuestIds` is for. Completion alone only
answers "who is ahead", which is the Catch Up question, not the Coop Maps one.

## 12b. A multi-map quest belongs only to the maps it still needs

A quest's maps are the union of its *objectives'* locations, and objectives finish
independently. "Needle in a Haystack" spans Ground Zero, Shoreline, Interchange and Streets;
once three of those legs are done, the quest is work on Streets only.

`NodeTaskLocations(node)` answers "every map this quest mentions" and is right for filters and
badges. For "where do I still have to go", use
`QuestObjectiveMapRules.OutstandingTaskLocations(quest, completedObjectiveIds)`, which drops
objectives already finished. Getting this wrong is invisible in aggregate counts and only shows
up when someone recognises a specific quest on a map they have finished with.

Objective ids are globally unique condition ids, so completion travels as one flat set per
profile - `QuestLiveState.Objectives` locally, `CoopProfileSnapshot.CompletedObjectiveIds` for
a teammate.

**The web app has this bug independently, and had it in five places.** Its equivalent of
`NodeTaskLocations` is `_nodeMaps` (built from `QuestNodeDto.ActualMaps`, itself derived from
`objectives.SelectMany(o => o.TaskLocations)`, so the ids line up). `QuestMapPageState.
OutstandingMaps(questId, state)` is the web-side filter. When you fix a "which maps" bug in one
UI, **grep the other** - the two surfaces share a question, not code.

## 13. The embedded locale JSON is unvalidated and throws on load

`ClientLocale.LoadCatalog` calls `JsonConvert.DeserializeObject` with **no try/catch**. A
malformed file throws on the first `ClientLocale.Text` call, which inside the Tasks screen is
caught by the mod's own error handling — so the whole mod silently reverts to the vanilla
panel for every player using that language, with nothing in the log naming the locale file.

A missing comma in `ru.json` shipped in the committed tree exactly this way and broke the
entire Russian catalog. `ClientLocaleCatalogTests` now asserts every file parses, has no
duplicate keys, and holds only string values.

Two things to know when editing these files by hand:

- They use **trailing commas**, which Newtonsoft accepts but `System.Text.Json` and Python's
  `json` both reject unless told otherwise. Strip them before validating, or pass
  `AllowTrailingCommas`.
- `en.json` is **LF** and `ru.json` is **CRLF**. A text-mode read/write round-trip in Python
  silently rewrites the whole file to LF and buries the real change in a full-file diff. Read
  and write these in binary.

---

## Debugging without a screen

- Every meaningful transition logs a `QUESTMAP_*` line. `QUESTMAP_M06_MOUNT`,
  `QUESTMAP_M06_RESUME`, `QUESTMAP_M06_SUSPEND`, `QUESTMAP_M06_VIEW`, `QUESTMAP_COOP_VIEW`,
  `QUESTMAP_COOP_LIST_DISPOSE`. Add one when you add a transition — it is the only trace you
  get.
- Errors log `QUESTMAP_M06_ERROR` / `QUESTMAP_COOP_ERROR` and then **restore vanilla** rather
  than leaving a broken screen. If the user reports "the tasks screen went back to normal",
  that is this path firing — grep the log for the phase.
- `RebuildCoopView` and `RebuildGraph` both catch, `Dispose()`, and re-show the native panel.
  A silent revert to vanilla is a caught exception, not a missing feature.
