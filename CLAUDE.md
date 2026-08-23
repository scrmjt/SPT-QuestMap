# SPT-QuestMap fork — Catch-Up Planner

Local fork of [rootdarkarchon/SPT-QuestMap](https://github.com/rootdarkarchon/SPT-QuestMap)
(cloned at release 2.0.0). Working branch: `catch-up-planner`. No personal GitHub fork
exists yet — `origin` is still upstream, so **never push to `origin`**.

> Read `AGENTS.md` first — it is upstream's contributor contract and it governs this work
> (source-first rule, performance-as-a-requirement, maintain `docs/development-status.md`,
> add any new `.csproj` to `src/SPTQuestMap/SPTQuestMap.slnx` in the same change).

## Machine setup (this box)

| Thing | Value |
| --- | --- |
| Repo | `D:\ClaudeProjects\SPT-QuestMap` |
| SPT test install | `D:\SPT4013_Test` — **use this one** |
| SPT live install | `C:\SPT4013` — **never touch; do not build, deploy, read, or start/stop anything here** |
| `SPT_ROOT` | `D:\SPT4013_Test` (the dir *containing* `BepInEx` and `SPT`, not `...\SPT`) |
| .NET SDK | 9.0.317 (`C:\Program Files\dotnet`) |
| PowerShell 7 | `C:\Users\<account>\AppData\Local\Microsoft\WindowsApps\pwsh.exe` — `scripts/*.ps1` need it; `[System.IO.Path]::GetRelativePath` does not exist in Windows PowerShell 5.1 |
| Server URL | `https://127.0.0.1:6969/questmap` (self-signed cert; `curl -k`) |
| Test profiles | Woulf `6a448ba904a64e2130386b7c`, ScramJet `6a448bf004a64e2130386cfb` — both level 38, in `D:\SPT4013_Test\SPT\user\profiles\` |

### Commands

```bash
export SPT_ROOT="D:\SPT4013_Test"
dotnet build src/SPTQuestMap/SPTQuestMap.slnx -c Release   # includes the BepInEx client
dotnet test  src/SPTQuestMap/SPTQuestMap.slnx -c Release
```

Deployment: `scripts/deploy.ps1 -Target Server -SptRoot D:\SPT4013_Test` **run under `pwsh`**.
It builds, tests, stages, hash-compares, and copies into
`D:\SPT4013_Test\SPT\user\mods\SPT-QuestMap`. It restarts the server only if one was already
running; otherwise start it yourself with `Start-Process` so it gets a real console — launching
`SPT.Server.exe` from a redirected/non-interactive shell dies with
`System.Exception: Unable to get console mode`.

Responses from the SPT host are raw **zlib** regardless of `Accept-Encoding`; decompress with
`zlib.decompress(...)` before parsing JSON.

## What this branch adds

A Compare Mode side panel (browser only — the native BepInEx client is untouched):

1. **Trader gaps** — per trader, which quests the comparison profile finished that the primary
   profile hasn't, ranked by that count, nearest-by-chain-depth first.
2. **Shared-map ranking** — which raidable maps have the most quests *both* profiles still need
   a raid for, i.e. the best maps to run together.
3. A **map filter** over the trader gaps, plus persistence of the panel's expanded state and filter.

Pure logic lives in `SPTQuestMap.Core` (`CatchUpRules`, `SharedMapRules`,
`QuestChainDepthCalculator`) and is projected into DTOs by
`QuestMapPageState.RebuildCatchUpModel` / `BuildSharedMapRankings` / `BuildCatchUpChains`.

### Design decisions that must not be undone

- **Trader gap is a per-quest set difference, never a max-completed-depth comparison.** The
  original implementation compared "deepest completed quest depth" per trader and reported *zero
  gaps* for two real level-38 profiles that differed on 15 quests, because both had completed one
  deep unrelated quest under that trader, tying the scalar. Regression test:
  `CatchUpRulesTests.BuildChainGaps_DetectsGapHiddenByTiedMaxDepthOnADifferentBranch`.
- **`ReadyToFinish` does not count as needing a raid** — objectives are already met, it only needs
  a trader hand-in, so it must not inflate the shared-map ranking.
- **`no-location`, `transition`, and `any` are excluded from the shared-map ranking** — they are
  not places to raid. An "any map" quest contributes equally to every map and so cannot
  differentiate them. They remain valid *filter* options, which is a deliberate asymmetry.
- **The map filter narrows trader gaps only.** The shared-map ranking always spans every
  applicable quest, because that ranking is what the user picks the filter from.
- **Catch-up inputs are cached per `SetData`, not recomputed per filter change**, and catch-up
  panel handlers must not call `ChangeViewAsync` — that pushes the whole `QuestGraphUpdate`
  (every visible quest id + every comparison) over JS interop, and no catch-up field is in it.

## Verification status

- Full solution builds in Release with **zero warnings** (`TreatWarningsAsErrors` is on).
- **99/99** shared-core tests, **177/177** server/browser tests.
- Verified against the real Woulf/ScramJet profiles on the running server: 4 trader gaps
  (was 0 before the fix), and a shared-map ranking topped by Streets of Tarkov (10), Customs (7),
  Lighthouse (7).

**Lesson learned — verify against real data, not just tests.** The original depth-scalar bug
passed its unit tests and read as correct; it only surfaced when run against two real profiles.
For anything ranking/filtering real quest data, check the live output before calling it done.

## Not done / open

- Not committed — everything is working-tree changes on `catch-up-planner`.
- No PR opened. **Upstream's `docs/development-status.md` declares the 2.0 scope frozen and says
  new features need separately approved post-2.0 scope — raise an issue/discussion with the
  maintainer before opening a PR.**
- `CLAUDE.md` (this file) is ours, not upstream's. **Exclude it from any upstream PR.**
- Chain depth is longest-path over the whole quest DAG, grouped per trader. Cross-trader
  prerequisite chains are not modelled specially; fine for the trader-questline framing, worth
  revisiting if it distorts recommendations.
- Only `en.json` and `ge.json` carry the `catchup.*` keys. A locale-completeness test
  (`GermanUiCatalog_TranslatesEveryNonInvariantDisplayString`) enforces German specifically;
  the other 15 locales fall back to English. Upstream may want the rest translated before merge.
- Never validated with more than two profiles, or with profiles at very different levels.
