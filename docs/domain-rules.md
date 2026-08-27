# Domain rules — decisions that must not be "simplified" back

**Load this when** changing quest classification, filtering, the catch-up planner, the
shared-map / coop rankings, series grouping, or objective display. It covers
`src/SPTQuestMap.Core/Rules/` and the projection layer in
`src/SPTQuestMap/Presentation/QuestMapPageState.cs`.

Every entry below was paid for with a real bug. Each one looks like an obvious
simplification from the inside; each one is wrong.

---

## 1. Where a quest still has work — `OutstandingMaps`

**A quest's map list is the union of its *objectives'* locations, and objectives complete
independently.** "Needle in a Haystack" spans Ground Zero, Shoreline, Interchange and
Streets; once the first three legs are done it is Streets work only.

Two different questions share one data shape and must never share one answer:

| Question | Answer with |
| --- | --- |
| "Which maps does this quest mention?" | the node's full map list (`_nodeMaps` / `ActualMaps`) |
| "Where does this player still have to raid?" | `OutstandingMaps(questId, state)` |

Using the first to answer the second is the multi-map bug. It shipped in **both** UIs
independently and was fixed in both:

- Client: `QuestObjectiveMapRules.OutstandingTaskLocations(node, completedObjectiveIds)`
- Web: `QuestMapPageState.OutstandingMaps(questId, state)`, used at **five** call sites —
  `BuildPlayerCatchUpMaps`, `BuildCoopMapRows`, `MapQuestCounts`, `BuildCatchUpMapRankings`,
  `BuildSharedMapRankings`.

Both fall back to the unfiltered list whenever there is nothing to refine with (no
per-objective progress, or a topology whose objectives carry no locations), so a data gap can
never silently drop a quest off every map.

The last three web call sites needed the **per-player split** spelled out: two profiles finish
different legs, so "shared" must mean a map where *both* still have work, not a map both
quests merely mention.

Covered by `QuestObjectiveMapRulesTests`, `PlayerCatchUpMapsTests`, `CoopMapHelperTests`.

> **Objective ids are globally unique condition ids**, so completion travels as one flat set
> per profile — no need to key it by quest.

---

## 2. Filters

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
- **`PruneUnselectableMapFilter` must run from BOTH `SetData` and `ApplySettings`.** Either
  can be the call that first brings a restored filter together with real data — settings are
  applied on either side of `SetData`. It previously ran only in `SetData`, so a stale map id
  restored afterwards filtered everything away against an unselectable option. Guarded by
  `MultiSelectFilterTests.SetData_DropsRestoredMapIdsTheLoadedProfilesCannotOffer_ButKeepsTheRest`.
- **Core's `QuestVisibilityOptions.TraderId` stays a single `string?`.** It is shared with the
  BepInEx client. A lone trader selection delegates to Core exactly as before; a wider
  selection is narrowed in the app layer, the same asymmetry the map filter already uses.
- **The map filter is applied in the app layer, not in Core's `QuestVisibilityRules`.**
  `QuestVisibilityNode` carries no location data, and this filter is browser-only — the native
  client has its own location controls. Do not widen the shared Core record for it.
- **The level filter is a CAP on the quest's own declared requirement, not a per-profile gate
  check.** It used to hide a quest only when it was level-gated for EVERY applicable profile,
  so in a mixed-level comparison (Regdab 8 vs ScramJet 39) it filtered nothing at all — a
  high-level profile is gated by almost nothing and kept everything visible. It now reads
  `EffectiveRequirements` where `Kind == "Level"` and hides anything above the cap. Presets are
  the loaded profiles' own levels. Quests with no declared level gate are never hidden by it.

---

## 3. Catch-up and trader gaps

- **Trader gap is a per-quest set difference, never a max-completed-depth comparison.** The
  original implementation compared "deepest completed quest depth" per trader and reported
  *zero gaps* for two real level-38 profiles that differed on 15 quests, because both had
  completed one deep unrelated quest under that trader, tying the scalar. Regression test:
  `CatchUpRulesTests.BuildChainGaps_DetectsGapHiddenByTiedMaxDepthOnADifferentBranch`.
- **Catch-up inputs are cached per `SetData`, not recomputed per filter change**, and catch-up
  panel handlers must not call `ChangeViewAsync` — that pushes the whole `QuestGraphUpdate`
  (every visible quest id + every comparison) over JS interop, and no catch-up field is in it.
- **Chain depth is longest-path over the whole quest DAG, grouped per trader.** Cross-trader
  prerequisite chains are not modelled specially; fine for the trader-questline framing, worth
  revisiting if it distorts recommendations.

---

## 4. Map rankings and co-op

- **`ReadyToFinish` does not count as needing a raid** — objectives are already met, it only
  needs a trader hand-in, so it must not inflate the shared-map ranking.
- **Only raidable maps count, in every ranking and in the co-op matrix.** `no-location`,
  `transition` and `any` are excluded — they are not places to raid. An "any map" quest
  contributes equally to every map and so cannot differentiate them. They remain valid *filter* options, which is a deliberate asymmetry.
- **The map filter narrows trader gaps and the graph, never the shared-map ranking.** That
  ranking always spans every applicable quest, because it is what the user picks the filter
  from.
- **`PoolCatchUpRules` is the N-profile analysis; the pairwise compare mode is separate.**
  Roles are derived per chain (the same profile can lead one and trail another). Map priority
  sums steps-behind, and a chain scores once per map however many quests it has there.
- **The co-op profile pool is separate from the pairwise primary/comparison pair.**
  `PoolProfiles` / `PoolProfileIds` feed the Coop Map Helper only, and are populated **only**
  via `SetPoolProfiles(...)` — never automatically by `SetData`. The graph tiles draw exactly
  two status lanes and the difference categories are inherently two-way, so widening compare
  mode to N profiles would break both; picking co-op players must never disturb it.
- **"All" means every selected profile still needs it.** A profile that cannot take a quest at
  all excludes it from the shared count, because the group cannot close it out together. So the
  shared number can be lower than the per-profile columns suggest — that is correct, not a bug.

---

## 5. Quest series (Gunsmith, Spa Tour…)

- **Series grouping matches the *shape* of a quest name, never the English word "Part".**
  Names are localized: `Gunsmith - Part 1` / `Büchsenmacher - Teil 1` / `Armurier - Partie 1`,
  and **Russian uses a period**: `Оружейник. Часть 1`. The regex in `QuestSeriesRules` was
  validated against all 17 installed locales (~180 matches, ~50 series each) with zero false
  positives in English. An English-only pattern silently breaks every other locale.
- **Series position is the highest completed part, over the whole series.** Not a count of
  completed parts, and not restricted to the filtered map — the question is "where is this
  player in the chain", so the denominator must stay stable as the map filter changes.
- Only ~30% of quests (167/558) belong to a named series; the rest are standalone and appear
  only in the trader-gap list. That is a known limit of name-based grouping, not a bug.

---

## 6. Objectives

- **`ObjectiveDefinitionDto.ObjectiveKind` is derived at mapping time, not in the UI.**
  `ConditionType` alone is not enough: `CounterCreator` is over half of all objectives and is a
  wrapper whose inner counter conditions decide whether it is kills, extracts or visits. Inner
  types are tested in priority order because one counter usually carries both a `Kills`
  condition and the `Location` it must happen on — unordered, every kill quest reads as "visit".
- **Required value is a quantity for most kinds and a THRESHOLD for `trader`, `skill` and
  `quest`.** Summing it there turned "reach Loyalty Level 4" into "28 trader". Those kinds
  count objectives instead. See `MixAmount`.
- **Known gap:** the objective mix has no plural forms ("1 builds"), and its counts come from
  the quest template, so they say what the quest asks for, not what remains.

---

## 7. Reading quest state

Resolve quest state through `QuestGraphRules.ClassifyProfileDisplayState`, never by reading
`overlay.AuthoritativeDisplayStates` directly — the raw dictionary is the last server snapshot
and misses quests completed in the current session. Change *detectors* comparing two snapshots
are the one legitimate raw read.

`default(QuestMapDisplayStateKind) == Locked`, so an uninitialised value fails safe.

**"Not completed" is not the same as "can do now."** A catch-up or co-op surface wants
`Available` / `InProgress` / `ReadyToFinish`, not "everything that isn't Completed" — the
latter includes every locked quest in the game and was why Coop Maps once listed every quest
on every map.

---

## Lesson worth keeping

**Verify against real data, not just tests.** The trader-gap depth-scalar bug passed its unit
tests and read as correct; it only surfaced when run against two real profiles. For anything
ranking or filtering real quest data, check the live output before calling it done.

Reading `D:\SPT4013_Test\SPT\user\profiles\*.json` against
`D:\SPT4013_Test\SPT\SPT_Data\database\locales\global\en.json` is the cheap way to settle
"is this number wrong, or is the data just like that". It is how the suspicious `0/N` column in
the co-op table turned out to be correct — `ScramJe1` really does have 558 quests and 0
completed.
