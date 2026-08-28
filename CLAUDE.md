# SPT-QuestMap fork — Catch-Up Planner

A read-only, profile-aware quest dependency map for **SPT 4.0.13**, served by the SPT server at
`/questmap`. This fork adds a catch-up planner and co-op map helper to
[rootdarkarchon/SPT-QuestMap](https://github.com/rootdarkarchon/SPT-QuestMap) (cloned at 2.0.0).

**This file is a router, not a manual.** It holds only what you need *before* you know the task.
Everything else is one hop away — load exactly one thing from the table below.

## Load one of these, not all of them

| Doing | Read first |
| --- | --- |
| **Any UI work** | skill **`questmap-ui`** — decides which of the two UIs you are in, and what "verified" may mean in each |
| Unity / BepInEx UI | `docs/client-ui-invariants.md` |
| Blazor web UI | `docs/web-ui-invariants.md` |
| Rules, filters, rankings, catch-up, series, objectives | `docs/domain-rules.md` |
| Client↔server protocol, plugin data flow | `docs/bepinex-client-notes.md` (sectioned — read the section) |
| A client-facing string | `docs/client-localization.md` |
| **"Where did we leave off?"** | `docs/STATE.md` |
| **"What should we build next?"** | `docs/roadmap.md` |
| Anything else in `docs/` | `docs/INDEX.md` — **never browse `docs/` directly**, it is 250 KB and half of it is historical |
| Headed for an upstream PR | `AGENTS.md` — upstream's contract (source-first rule, register new `.csproj`, maintain `docs/development-status.md`) |

Otherwise: the code answers it. Grep first.

## Hard rules

1. **Never touch `C:\SPT4013`** — the user's live install. No builds, no deploys, no reads, no
   starting or stopping anything. Everything goes to **`D:\SPT4013_Test`**.
2. **Never push to `upstream`** (`rootdarkarchon/SPT-QuestMap`). `origin` is `scrmjt/SPT-QuestMap`,
   the user's own fork, and is push-safe.
3. **Deploy order is stop → deploy → start.** A running `SPT.Server.exe` holds `SPTQuestMap.dll`
   open; the copy fails, the script restarts anyway, and stale code runs looking freshly deployed.
4. **Never claim the Unity client works.** It cannot be seen or tested from this machine. Say
   built-and-deployed, and name what is unchecked.
5. **Don't decompile installed SPT DLLs** — use the source tree under `reference/`.

## Layout

Three projects, all registered in **`src/SPTQuestMap/SPTQuestMap.slnx`** along with the two test
projects. Build the solution, not the projects one by one. **A new `.csproj` must be added to the
`.slnx` in the same change** — `AGENTS.md` requires it.

| Project | Target | What |
| --- | --- | --- |
| `src/SPTQuestMap.Core/` | `netstandard2.1` | Shared rules + wire models. **Anything both sides exchange must be defined here** — the DTOs in `src/SPTQuestMap/Models/` are net9.0 and invisible to the plugin. |
| `src/SPTQuestMap.Client/` | `netstandard2.1` | BepInEx/Unity in-game plugin |
| `src/SPTQuestMap/` | `net9.0` | SPT server mod + Blazor web app |

`netstandard2.1` constrains Core: no `ArgumentNullException.ThrowIfNull`, and records need the
`IsExternalInit` polyfill. The web app and the client version **independently** — don't bump the
client unless the client changed.

Two facts that stop whole lines of wasted investigation:

- **The shared contract is `src/SPTQuestMap.Core/Models/QuestGraphModels.cs`.** The DTOs in
  `src/SPTQuestMap/Models/QuestMapDtos.cs` are net9.0 and invisible to the plugin.
- **The plugin never reads profiles itself.** It asks the server over HTTP
  (`/questmap/client/*`), and the server already enumerates every profile via SPT's
  `SaveServer.GetProfiles()`. So multi-profile features are blocked on Unity UI work, not on
  architecture.

## Commands

```bash
export SPT_ROOT="D:\\SPT4013_Test"    # the dir CONTAINING BepInEx and SPT, not .../SPT

# Builds all five projects and runs both suites (355 tests). This is the one you want.
dotnet test  src/SPTQuestMap/SPTQuestMap.slnx -c Release --nologo -v q
dotnet build src/SPTQuestMap/SPTQuestMap.slnx -c Release --nologo -v q   # build only
```

To iterate on one suite: `dotnet test tests/SPTQuestMap.Core.Tests --nologo -v q` (160) or
`tests/SPTQuestMap.Tests` (195).

Every `dotnet` invocation needs `SPT_ROOT` set or it fails with "Set SptInstallRoot or SPT_ROOT".

**Deploy** (under `pwsh`, not Windows PowerShell 5.1 — `scripts/*.ps1` use
`[System.IO.Path]::GetRelativePath`):

```powershell
scripts/deploy.ps1 -Target Server -SptRoot D:\SPT4013_Test
```

**The script starts the server itself** whenever the server DLL changed — unconditionally,
with no check for whether one was running (`deploy.ps1` line ~146). So do **not** start one
yourself after a server deploy, or you get two processes: only one binds 6969 and the other
is a silent orphan. A client-only deploy leaves `serverChanged` false and starts nothing.

Server exe is `D:\SPT4013_Test\SPT\SPT.Server.exe` (under `SPT\`, not the root). If you do
need to start it by hand, use `Start-Process` so it gets a real console — from a redirected
shell it dies with `Unable to get console mode`. Web app at `https://127.0.0.1:6969/questmap` (self-signed, `curl -k`).
Responses from the SPT host are raw **zlib** regardless of `Accept-Encoding`.

The only in-game trace is `D:\SPT4013_Test\BepInEx\LogOutput.log`:

```bash
grep -E "QUESTMAP_" "D:/SPT4013_Test/BepInEx/LogOutput.log" | tail -40
```

A caught exception logs `QUESTMAP_*_ERROR` and **restores the vanilla screen** — "it went back
to normal" means an exception, not a missing feature.

## Editing conventions

Git Bash **eats backslashes in heredocs** (`\25BE` became a control byte in the CSS) and mangles
non-ASCII; large quoted heredocs fail outright with `unexpected EOF`. For anything beyond a few
lines use the **Write tool**, or a Python patch script that **asserts its match count** — an
`if old in s:` guard is how a patch silently no-ops and leaves half a feature behind.

Locale JSON uses **trailing commas** (Newtonsoft tolerates them; `System.Text.Json` and Python
`json` do not), `en.json` is LF and `ru.json` is CRLF. Read and write them in **binary**.

## Keeping this file cheap

It loads into every session. Facts that churn — test counts, branch state, what is unverified —
belong in `docs/STATE.md`. Rules that were paid for with a bug belong in the invariant files.
**Add a row to the routing table rather than a section here.**
