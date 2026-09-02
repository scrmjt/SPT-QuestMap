# Debugging the native UI

How to diagnose a QuestMap bug that lives in EFT's own UI, written after a hunt that took seven
wrong fixes and eight test cycles. Read this **before** changing code to fix a native-UI symptom.

The bug: with QuestMap open, clicking a Character tab (MAP / ACHIEVEMENTS / PRESTIGE) played the
tab's click sound and did nothing. Everything below is measured, not inferred.

---

## 1. The answer, for reference

`RebuildCoopView()` disposed the graph view without clearing the nine chrome fields that pointed
into it. The warning-badge GameObject was destroyed with the view; the field still referenced it.
A destroyed `UnityEngine.Object` is **fake null**, so `is not null` passed and `SetActive` threw
`NullReferenceException` inside `UpdateQuestItemsButtonState`.

That exception surfaced through `Suspend()`, which is a Harmony **prefix** on
`TasksScreen.Close`. A throwing prefix means the close body never runs, so EFT's tab transition
was left half-done with its guard latched — and the tab bar then refused every click, correctly,
until the screen was rebuilt. **EFT swallowed the exception**, so nothing appeared in any log.

Four independent things had to be understood at once, and each one alone looked like a dead end.

---

## 2. Rules this produced

**A Harmony prefix must not throw.** It runs inside the game method's contract; the body it
precedes simply will not run. Wrap the whole of any prefix body in a try/catch that logs and
contains. `QuestScreenCoordinator.CloseGlobalTasksScreen` is the worked example.

**Never `SetActive(false)` a native GameObject you only want to hide.** Deactivating stops every
coroutine on that object, silently and with no exception, and EFT's screen transitions run on
those. Use a `CanvasGroup` (`alpha = 0`, `interactable = false`, `blocksRaycasts = false`) and
leave the object active — `VeilNativeTasksPanel` and `Veil`/`Unveil` do this.

**Clear references when the view that owns them is disposed.** This is the primary defence
against fake null, ahead of any null-check style: a field that is set to `null` cannot be a
corpse. `ClearGraphChromeReferences()` exists for exactly this, and must be called at every site
that disposes `_graphView`.

**Use `== null`, not `is null`, on a Unity object.** Only the overloaded operator reports a
destroyed object as null. `is null`, `is not null`, `?.` and `??` all test the CLR reference and
see a live object. There are still ~300 pattern-style null tests across `UI/` — most are benign
because their targets are never destroyed while referenced, but when touching a field that
outlives a view, convert it. To find them:

```bash
grep -rn "is not null\|is null" src/SPTQuestMap.Client/UI/
```

**An exception you cannot see may still exist.** EFT catches what comes out of a patch. Five
rounds of searching the Unity log for an error found nothing because there was nothing to find
until we caught it ourselves.

---

## 3. The diagnostic ladder

Work down it. Each rung eliminates a whole class and costs one test cycle; guessing costs the
same cycle and eliminates nothing.

| Question | Instrument | Marker |
| --- | --- | --- |
| Is something covering the control? | `EventSystem.RaycastAll` at the real cursor position | `QUESTMAP_M06_CLICK` |
| Who would Unity dispatch to? | `ExecuteEvents.GetEventHandler<IPointerClickHandler>` | `QUESTMAP_M06_TABSTATE` |
| Is the target component enabled and ungated? | component `enabled` flags, ancestor `CanvasGroup`s | `QUESTMAP_M06_TABSTATE` |
| What is the native component's own state? | reflection over its fields | `QUESTMAP_M06_TABFIELDS` |
| Who handles the event, and what state do they decide on? | the delegate's `Method` and `Target` | `QUESTMAP_M06_TABOWNER` |
| When does that state change? | per-frame diff of the owner's fields | `QUESTMAP_M06_TABGUARD` |
| Does the handler run, and does it throw? | Harmony prefix + finalizer on it | `QUESTMAP_M06_HANDLER` |
| What did *we* do in that frame? | every native mutation, frame-stamped | `QUESTMAP_M06_NATIVEOP` |
| Where did our own code stop? | step markers between statements | `op=suspend.step` |
| What did it throw? | try/catch around the call | `QUESTMAP_M06_SUSPEND_FAILED` |

**Frame-stamp everything.** `Time.frameCount` on every marker is what made the answer visible:
the handler entering, our `Suspend` running inside it, and the guard latching were all one frame.
Without frame numbers, no two events can be ordered and every hypothesis stays unfalsifiable.
This was the single biggest waste in the hunt — five rounds of reasoning about co-occurring
events with no way to establish co-occurrence.

---

## 4. EFT's Character tab bar

Tabs live at `Common UI/Common UI/InventoryScreen/Tab Bar/Tabs/<Name>`, each carrying
`RectTransform, CanvasRenderer, Tab, ButtonFeedback, LocalizedText, TabElement`. They are **not**
Unity `Toggle`s.

`ButtonFeedback` (the click sound) and `Tab` (the behaviour) are **separate components**. Unity
skips a disabled behaviour when resolving a handler but still delivers to its enabled siblings,
so *"I can hear the click but nothing happens" always means the click was delivered.* It never
means the click was blocked.

`Tab.action_0` has exactly one subscriber: `GClass3808.method_0`. That object holds the state:

| Field | Meaning |
| --- | --- |
| `Tab_2` | the currently selected tab — advances on every successful switch |
| `Bool_1` | **the busy guard**: false when idle, true while a switch is in flight |
| `Tab_1` | constant `Gear` in every sample |
| `Bool_0` | constant `True` in every sample |

`method_0` is the usual shape — return if busy, set busy, start the transition, clear busy on
completion. `Bool_1 = true` is **normal and transient**; only *staying* true is a bug. Compare:

```
working: Tab_2=Skills, Bool_1=False    <- Tab_2 advancing
stuck:   Tab_2=Tasks,  Bool_1=True     <- frozen, guard latched
```

Grep `QUESTMAP_M06_TABGUARD` first. **If `Bool_1` is true, stop looking at input, rects and
handlers — the click is being refused on purpose, and the fault is whatever stopped the previous
transition finishing.** A bottom-nav round trip always "fixes" it because it rebuilds this
object; that recovery is a symptom of latched state, not a clue about input.

---

## 5. Reading a click log

A press and a release both resolve against the live hierarchy, so the *path* tells you what
happened without needing to see the screen:

- `Tabs/Map/Normal/Hover/...` before and after — Map is not selected and did not become
  selected: **a genuine stall**
- `Tabs/Tasks/Selected/Background/...` — that tab is already current, so a click on it correctly
  does nothing: **not a bug**, and several of these were miscounted as failures
- `hits=3` on press, `hits=0` on release — the hierarchy vanished under the cursor, i.e. the
  screen changed: **the click worked**

---

## 6. What went wrong in the process

Seven fixes were shipped before the cause was known. Each was plausible, each was contradicted by
the next log, and the pattern is worth more than any of the individual findings.

| # | Premise | Disproved by |
| --- | --- | --- |
| 1 | A decorative `Image` swallows the click | `RaycastAll` returned EFT's own tab on top |
| 2 | `Close` fires, so `Suspend` can hide the surface | `SUSPEND` only ever appeared for bottom-nav |
| 3 | The screen deactivates, so `activeInHierarchy` can gate it | Screen stays active; `rootUnderScreen=True` |
| 4 | The tabs are `Toggle`s whose state can be latched | All eight logged `?` — they are EFT `Tab`s |
| 5 | The bug is Coop-specific | Clicks failed in `Full` mode too |
| 6 | The click subscription was lost | `action_0=1subs`, `Interactable=True` while stuck |
| 7 | `ClearNativeOverlays` in `Suspend` kills the transition | No `SUSPEND` anywhere near the latching frame |

Three habits caused all seven:

**Reasoning from source instead of measuring.** Every premise above was read out of the code and
was wrong about the running game. One `EventSystem.RaycastAll` line disproved a week of it.

**Assuming a fact that a one-line check would have settled.** The tabs were assumed to be
`Toggle`s. `Suspend` was called a *postfix* for six rounds — in commit messages and in the docs —
when `PatchRegistration` plainly wires `ClosePrefix`, and that inversion is what made fixes 2, 3
and 7 target the wrong side of the call.

**Treating a working control next to a broken one as evidence the subsystem was fine.** Our own
header tabs kept working, which made graph mode look healthy and aimed four rounds at Coop, which
had nothing to do with it. Check *whose* control is working.

The rule that would have short-circuited all of it: **when a UI symptom survives two fixes, stop
reading source and instrument the runtime.** The instruments in §3 exist now; use them first.

---

## 7. Turning the logging off

The markers cost nothing when `Enable debug logging` is false in
`BepInEx/config/com.rootdarkarchon.sptquestmap.client.cfg` — every one is behind
`QuestMapDebugLog.Enabled`, including the Harmony probe on `method_0`, which is not even
installed when logging is off. Leave the code in place; it is the map for next time.
