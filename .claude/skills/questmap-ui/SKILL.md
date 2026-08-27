---
name: questmap-ui
description: Use when changing any SPT-QuestMap user interface — the Blazor web app or the BepInEx/Unity in-game client. Covers which of the two you are in, how to actually verify a change in each, and the Unity invariants that cannot be caught by tests.
---

# Working on SPT-QuestMap UI

This project has **two unrelated UIs** that share a rules core. They have opposite
verification stories, and the most common failure is applying one's habits to the other.

| | Blazor web app | BepInEx / Unity client |
| --- | --- | --- |
| Code | `src/SPTQuestMap/Presentation/` | `src/SPTQuestMap.Client/UI/` |
| Target | `net9.0` | `netstandard2.1` |
| Styling | `QuestMap.css` | C# — colours, rects and offsets in code |
| **Can you see it?** | **Yes** — serve it and open Chrome | **No.** Never. |
| Feedback loop | seconds | a user launching Tarkov and screenshotting |

**First move on any UI task: decide which one you are in, and say so.** If the request is
ambiguous ("fix the quest list spacing"), ask — the two have separate quest lists.

## Shared rules core

`src/SPTQuestMap.Core/` holds the wire models and the quest-state rules both UIs consume.
`netstandard2.1` constrains it: no `ArgumentNullException.ThrowIfNull`, and records need the
`IsExternalInit` polyfill.

Resolve quest state through `QuestGraphRules.ClassifyProfileDisplayState`, never by reading
`overlay.AuthoritativeDisplayStates` directly — the raw dictionary is the last server snapshot
and misses quests completed in the current session.

**If the change touches classification, filtering, rankings, catch-up, series or objectives,
read `docs/domain-rules.md` before writing anything.** Those rules were each paid for with a
bug and each looks like an obvious simplification from the inside.

Anything that is genuinely a rule belongs in Core with a test. Both UIs then get it, and it
becomes verifiable — which is the only way Unity behaviour gets tested at all.

---

## Blazor web app — verify it, don't reason about it

You can see this one, so **looking is always cheaper than arguing**.

```bash
# from the repo root
python -m http.server 8731 --bind 127.0.0.1     # then browse 127.0.0.1:8731
```

Chrome automation **cannot open `file://` URLs** — serve it first, and stop the server when done.

**Read `docs/web-ui-invariants.md` before editing.** It carries the layout, type, component
and canvas-renderer rules for this app. The three that most often waste a session:

- **Grepping served HTML gives false positives** — the stylesheet is inlined, so a class-name
  grep matches its CSS rule and reports a removed element as present. Strip the
  `<style id="spt-questmap-styles">` block first.
- **Removing a grid child without fixing the template silently mangles the layout.** If a child
  goes, count the columns.
- **Headings use the `font:` shorthand**, so a `font-family:` grep will not find them. Georgia
  is for headings, Inter for anything a person operates.

---

## Unity client — you are building blind

There is no Editor, no way to launch Tarkov here, and the Chrome tools only see browser tabs.
Installing Unity would not help: Tarkov ships compiled DLLs, not a project, and the Harmony
patches need the real runtime object graph.

**Before writing any UI code here, read `docs/client-ui-invariants.md`.** It is the list of
rules that compile fine and fail on screen. The ones that cause the worst bugs:

1. **Each view owns its own header — so each view must draw its own tab bar.** Disposing a
   view destroys its header and every tab on it. Call `BuildModeTabs(header)`. A view without
   tabs is a dead end the player cannot leave.
2. **`Destroy()` is deferred to end of frame — `SetActive(false)` first**, or the outgoing
   absolutely-positioned rows render on top of the incoming ones. This is the "flicker".
3. **`anchoredPosition.x` is meaningless on a stretched rect** — use
   `UnityUiFactory.CreateListRow`.
4. **Never `await` on a Unity path** — start the `Task`, poll `IsCompleted` from a coroutine.
5. **`Mount` / `Resume` / `Suspend` / `Dispose` / `RefreshOverlay` are five separate places a
   new mode must be handled.** They were all written graph-shaped.

Prefer `UnityUiFactory.AddButton` (correct tint + EFT click sounds) over a bare `Toggle`.

### What "verified" is allowed to mean here

Be exact about this, always:

- **Compiles, zero warnings** — real, and the floor.
- **Core rules covered by tests** — real, and the only actual behavioural proof available.
- **Server routes proven with `curl`** — real; do this whenever a route is involved.
- **"The layout looks right"** — *not something you can claim.* Say it is unverified and name
  what the user would need to look at.

Never report a Unity UI change as working. Report it as built and deployed, and say what
remains unchecked.

### Build, deploy, read the log

```bash
export SPT_ROOT="D:\\SPT4013_Test"
dotnet test src/SPTQuestMap/SPTQuestMap.slnx -c Release --nologo -v q
```

That builds all five projects and runs both suites. The BepInEx client is in the solution,
so this is also how you confirm the client still compiles.

Deploy order is **stop server → deploy → start server**. Deploying while the server holds the
DLL fails, and the script will restart anyway, silently leaving stale code running.

**Hard rule: never touch `C:\SPT4013`.** All build, deploy and testing goes to
`D:\SPT4013_Test`.

The only in-game trace is `D:\SPT4013_Test\BepInEx\LogOutput.log`:

```bash
grep -E "QUESTMAP_(M06|COOP)" "D:/SPT4013_Test/BepInEx/LogOutput.log" | tail -40
```

Add a `QUESTMAP_*` log line for every transition you introduce. A caught exception logs
`QUESTMAP_*_ERROR` and **restores the vanilla screen** — so "it went back to normal" means an
exception, not a missing feature. Grep for the phase.

---

## Editing conventions in this repo

Git Bash **eats backslashes in heredocs** — `"\25BE"` became a literal control byte in the CSS,
`"\n"` became a real newline and broke a Razor parse. Large quoted heredocs also fail outright
with `unexpected EOF looking for matching quote`.

For anything beyond a couple of lines, use the **Write tool**, or a Python patch script that
**asserts its match count**:

```python
c = body.count(old)
if c != 1:
    raise SystemExit(f"{label}: expected 1 match, found {c}")
```

A conditional `if old in s:` guard is how a patch silently no-ops and leaves half a feature
behind. Assert, don't skip.

Build backslashes as `chr(92)` when they must survive a shell layer.

## Versioning

The web app and the client version independently. Do not bump the client unless the client
actually changed.
