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
`D:\SPT4013_Test\SPT\user\mods\SPT-QuestMap`. **Stop the server BEFORE deploying.** A running `SPT.Server.exe` holds `SPTQuestMap.dll` open, the copy fails with "being used by another process", and the script still restarts a server - leaving stale code running that looks freshly deployed. Order is: stop, deploy, start. The exe lives at `D:\SPT4013_Test\SPT\SPT.Server.exe`, under `SPT\`, not the install root. It restarts the server only if one was already
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
3. **Chain progress** — per named quest series (Gunsmith, Spa Tour…), how far each player has
   advanced, one bar per player, sorted by biggest disagreement first.
4. A **global map filter** in the top filter bar beside the traders, narrowing the graph *and*
   the panel, plus persistence of it and the panel's expanded state.

Pure logic lives in `SPTQuestMap.Core` (`CatchUpRules`, `SharedMapRules`, `QuestSeriesRules`,
`QuestChainDepthCalculator`) and is projected into DTOs by
`QuestMapPageState.RebuildCatchUpModel` / `BuildSharedMapRankings` / `BuildCatchUpChains` /
`BuildSeriesProgress`.

### Design decisions that must not be undone

- **Filters are multi-select sets, and an EMPTY set means "unfiltered".** `TraderFilter` and
  `MapFilter` are `IReadOnlySet<string>`; a populated set is a **union (OR)**, never an
  intersection. Two selected maps show quests on either one — an intersection would show
  nothing, because almost no quest is on two maps.
- **Never change the singular `TraderFilter`/`MapFilter` fields on `QuestMapSettings` into
  collections.** The multi-select values live in *added* `TraderFilters`/`MapFilters`
  properties. A stored `"traderFilter": "abc"` deserialised into a collection throws
  `JsonException`, and `QuestMapSettingsStorage` treats that as "no settings" — silently
  wiping every saved preference for every existing user. Guarded by
  `MultiSelectFilterTests.StoredJsonFromAnOlderBuild_StillDeserialises`.
- **The singular field is written only when exactly one thing is selected**, so an older
  build never reads a silently narrowed filter.
- **The render signature key sorts filter ids before joining** (`FilterKey`). Sets have no
  defined order; hashing one directly makes an identical selection look like a change and
  forces a full graph redraw.
- **Core's `QuestVisibilityOptions.TraderId` stays a single `string?`.** It is shared with
  the BepInEx client. A lone trader selection delegates to Core exactly as before; a wider
  selection is narrowed in the app layer, the same asymmetry the map filter already uses.
- **`PruneUnselectableMapFilter` must run from BOTH `SetData` and `ApplySettings`.** Either
  can be the call that first brings a restored filter together with real data — settings are
  applied on either side of `SetData`. It previously ran only in `SetData`, so a stale map id
  restored afterwards filtered everything away against an unselectable option. Guarded by
  `MultiSelectFilterTests.SetData_DropsRestoredMapIdsTheLoadedProfilesCannotOffer_ButKeepsTheRest`.
- **Compare-mode slot labels come from `State.PrimaryShortLabel`/`ComparisonShortLabel`, and
  nothing may branch on their text.** `QuestDetails` used to pick the slot CSS class with
  `label == "A"`; the helpers now take `bool isPrimary`. Two profiles can share a two-letter
  prefix ("Scav1"/"Scav2" both give "Sc"), so the label cannot identify the slot.
- **`FilterDropdown` renders no panel while `Disabled`.** Disabling only the trigger button
  left an already-open panel live during focus mode, where the state setters ignore changes —
  the checkbox moved and nothing filtered.
- **The level filter is a CAP on the quest's own declared requirement, not a per-profile
  gate check.** It used to hide a quest only when it was level-gated for EVERY applicable
  profile, so in a mixed-level comparison (Regdab 8 vs ScramJet 38) it filtered nothing at
  all - a level-38 profile is gated by almost nothing and kept everything visible. Now it
  reads `EffectiveRequirements` where `Kind == "Level"` and hides anything above the cap.
  Presets are the loaded profiles' own levels. Quests with no declared level gate are never
  hidden by it.
- **`PoolCatchUpRules` is the N-profile analysis; the pairwise compare mode is separate.**
  Roles are derived per chain (the same profile can lead one and trail another). Map priority
  sums steps-behind, and a chain scores once per map however many quests it has there.
- **Panel content scrolls via `flex: 1 1 auto; min-height: 0`, never `height: calc(100% - Npx)`.**
  The old form worked only while the panels were absolutely positioned with a fixed top and
  bottom. As flex children their height is content-driven, so `100%` resolves against `auto`
  and the list runs off the bottom of the page instead of scrolling. `min-height: 0` is the
  part that actually lets a flex child shrink.
- **`ObjectiveDefinitionDto.ObjectiveKind` is derived at mapping time, not in the UI.**
  `ConditionType` alone is not enough: `CounterCreator` is over half of all objectives and is
  a wrapper whose inner counter conditions decide whether it is kills, extracts or visits.
  Inner types are tested in priority order because one counter usually carries both a `Kills`
  condition and the `Location` it must happen on - unordered, every kill quest reads as
  "visit".
- **Required value is a quantity for most kinds and a THRESHOLD for `trader`, `skill` and
  `quest`.** Summing it there turned "reach Loyalty Level 4" into "28 trader". Those kinds
  count objectives instead. See `MixAmount`.
- **Known gap: the objective mix has no plural forms** ("1 builds"), and its counts come from
  the quest template, so they say what the quest asks for, not what remains.
- **`.topbar` is the left sidebar, not a top bar.** `.app-shell` is `grid-template-columns`,
  not rows. Identity (both profiles, swap, language) and every filter stack vertically there
  so the graph gets the window's full height.
- **The side panels are in flow, never absolutely positioned.** `.side-views` is a real
  workspace column, so expanding In Progress or the Catch-Up Planner pushes the graph right
  instead of covering it. `.workspace` is `auto minmax(0,1fr) auto` - panels, graph, details.
- **`.details.collapsed` must stay zero-sized in every breakpoint.** The `max-width: 1050px`
  block sets `min-height: 22rem` on `.details`; without an explicit collapsed override there
  it reserves 22rem for an empty pane. This is invisible in a screenshot whenever a quest
  happens to be selected - check `getComputedStyle`, not just the picture.
- **The canvas renderer is configured by TWO payloads and both must be updated together.**
  `QuestGraphView` feeds `initialize`/`refresh` on first paint; `QuestGraphUpdate` feeds
  `synchronize` on every later change. Both land in the same `applyConfig`. Adding a field to
  only one produces a bug that is invisible on a fresh page load but correct after any filter
  change - or the reverse. The profile tile labels live in both.
- **Profile label lengths are deliberate: 2 for the compact slot chips, 5 for graph tiles,
  and the full nickname on filter chips when it is 10 characters or shorter.**
  `ProfileLabelRules.ShortLabel` takes the element count; it counts text elements, not chars,
  so surrogate pairs are not split.
- **`.filterbar` must keep `overflow: visible`.** It was `hidden` to contain the horizontally
  scrolling trader row the dropdowns replaced. With dropdowns, `hidden` clips the absolutely
  positioned panel to the bar's 78px: only the search box shows and the checkbox list is
  unreachable. The feature builds, tests green, and is unusable. Found only in a browser.
- **No numeric CSS escapes in this stylesheet.** A caret written as a CSS escape sequence
  lost its backslash passing through a shell heredoc and became a literal 0x15 control byte.
  Paste the character itself instead.
- **`FilterDropdown` list items need `@key="item.Id"`.** Without it Blazor reuses `<li>` DOM
  positionally, and narrowing the search can carry a checkbox's state onto a different row.

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
- **The map filter narrows trader gaps and the graph, never the shared-map ranking.** That
  ranking always spans every applicable quest, because it is what the user picks the filter from.
- **The map filter is applied in the app layer, not in Core's `QuestVisibilityRules`.**
  `QuestVisibilityNode` carries no location data, and this filter is browser-only — the native
  client has its own location controls. Do not widen the shared Core record for it.
- **Series grouping matches the *shape* of a quest name, never the English word "Part".**
  Names are localized: `Gunsmith - Part 1` / `Büchsenmacher - Teil 1` / `Armurier - Partie 1`,
  and **Russian uses a period**: `Оружейник. Часть 1`. The regex in `QuestSeriesRules` was
  validated against all 17 installed locales (~180 matches, ~50 series each) with zero false
  positives in English. An English-only pattern silently breaks every other locale.
- **Series position is the highest completed part, over the whole series.** Not a count of
  completed parts, and not restricted to the filtered map — the question is "where is this player
  in the chain", so the denominator must stay stable as the map filter changes.
- Only ~30% of quests (167/558) belong to a named series; the rest are standalone and appear
  only in the trader-gap list. That is a known limit of name-based grouping, not a bug.
- **Catch-up inputs are cached per `SetData`, not recomputed per filter change**, and catch-up
  panel handlers must not call `ChangeViewAsync` — that pushes the whole `QuestGraphUpdate`
  (every visible quest id + every comparison) over JS interop, and no catch-up field is in it.

## Verification status

- Full solution builds in Release with **zero warnings** (`TreatWarningsAsErrors` is on).
- **114/114** shared-core tests, **179/179** server/browser tests.
- Verified against the real Woulf/ScramJet profiles on the running server: 4 trader gaps
  (was 0 before the fix), and a shared-map ranking topped by Streets of Tarkov (10), Customs (7),
  Lighthouse (7).

**Lesson learned — verify against real data, not just tests.** The original depth-scalar bug
passed its unit tests and read as correct; it only surfaced when run against two real profiles.
For anything ranking/filtering real quest data, check the live output before calling it done.

## In-game (BepInEx) client

See **`docs/bepinex-client-notes.md`** for the worked-out notes: how the plugin gets its data
from the server, why compare mode / catch-up is feasible in-game and what it would cost, the
root cause of the vanilla-screen flash, and the feasibility of keeping the vanilla list as a
tab. All of it is referenced by file and line so it can be re-checked after a refactor.

Two headlines from it:

- **The plugin never reads profiles itself.** It asks the server over HTTP
  (`/questmap/client/*`), and the server already enumerates every profile via SPT's
  `SaveServer.GetProfiles()`. So multi-profile features are blocked on Unity UI work, not on
  architecture.
- **Anything both sides exchange must be defined in Core.** The DTOs in
  `src/SPTQuestMap/Models/QuestMapDtos.cs` are net9.0 and invisible to the plugin; the shared
  contract is `src/SPTQuestMap.Core/Models/QuestGraphModels.cs`.

## Not done / open

- The map filter + chain-progress work is **uncommitted** working-tree changes on
  `catch-up-planner` (the earlier catch-up work is committed as `8454592`). `pr/catch-up-planner`
  is a clean single-commit branch off `upstream/main` **predating** this work — regenerate it from
  the final state before opening a PR.
- Remotes: `origin` = `scrmjt/SPT-QuestMap` (the user's fork, push-safe), `upstream` =
  `rootdarkarchon/SPT-QuestMap`. Nothing has been pushed yet.
- UI is mid-iteration; the user is still reviewing the panel layout.
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
