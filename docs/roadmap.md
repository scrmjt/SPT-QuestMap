# Roadmap

**Load this when** deciding what to build next, or when picking up an item below.

What is *done* lives in `STATE.md`; what is *ruled out* lives under **Parked** here and in
`development-status.md`'s "Superseded designs". This file is only for work not yet started.

Items are ordered by value, not by sequence. Nothing here is committed to a release.

---

## 1. See your co-op partner's quests — in raid

**The goal:** while you are in a raid with someone, see what quests *they* are working on here,
so you can help each other instead of each running your own errands.

### Why this is smaller than it sounds

Almost all of it is already built, and the part people assume is hard mostly is not.

| Piece | State |
| --- | --- |
| Reading every profile's quests | **Done** — server enumerates via `SaveServer.GetProfiles()` |
| Fetching partner snapshots from the client | **Done** — `NativeCoopDataSource.LoadSnapshotsAsync` |
| Knowing which objectives are outstanding *on this map* | **Done** — `QuestObjectiveMapRules` |
| An in-raid list with map-aware bands | **Done** — `RaidQuestRuntime` + `RaidTrackedQuestListView` |
| Knowing the current map in raid | **Done** — `InRaidQuestContext` |
| Showing a *partner's* rows in that list | **Missing** |
| Knowing who your partners are | **Missing** — you pick the pool by hand |

**What your partner's quests *are* is static during a raid.** Quests are accepted at traders,
between raids. So the whole list can be fetched once, at raid start, and stay correct for the
duration — no syncing, no polling, no Fika.

### What is and is not available in raid

Be precise about this in the UI, because the two get conflated:

- **Which quests they have, and which objectives are outstanding on this map** — available, and
  correct for the whole raid. This is the actual ask.
- **Live progress ticking** (they just got 3/5 scavs) — *not* available. Their progress advances
  on their client and reaches the server when Fika syncs, in practice at raid end. Do not render a
  live-looking counter from raid-start data.

In practice the second matters far less than it sounds: what you want in raid is "does Woulf also
need the Streets item, should we go together", and that answer does not change mid-raid. Label the
progress numbers as of raid start and the feature is honest.

### Design

1. **Fetch partner snapshots once, at raid start** — during the loading screen, not in the raid.
   `NativeCoopDataSource` already does the fetch; it needs a call site on the raid-entry path.
   Never `await` on a Unity path — start the `Task` and poll `IsCompleted` from a coroutine, the
   same pattern the existing loads use.
2. **Filter each partner's quests to the current map** with the rules that already exist —
   `QuestObjectiveMapRules.OutstandingTaskLocations` against their `CompletedObjectiveIds`, then
   `InRaidQuestContext.DefaultLocationIds` for the map aliases. This is the same "where does this
   player still have work" question the co-op planner asks, so it must reuse that rule rather than
   the quest's full map list.
3. **Extend `RaidTrackedQuestListProjection`** with partner bands, and render them in
   `RaidTrackedQuestListView` below your own. The list already does current-map-first banding and
   full-height layout, so this is additive.
4. **No Fika dependency for any of the above.** You select the pool before the raid (or it is
   remembered — see below), and the in-raid view uses whoever is in it.

### Staged

**Stage 1 — remember the pool.** Persist the selected co-op pool so it survives restarts. Cheap,
no dependency, useful on its own, and it is what makes stage 2 usable without Fika.

**Stage 2 — the in-raid partner bands.** The feature above. Depends only on stage 1.

**Stage 3 — auto-detect the roster from Fika.** Replace "the pool you picked" with "whoever is
actually in this session", as a **soft** BepInEx dependency
(`DependencyFlags.SoftDependency`) so QuestMap still loads normally with no Fika installed. This
is a convenience on top of a feature that already works, not a prerequisite for it — which is the
main reason to sequence it last.

**Stage 4 — live in-raid progress (speculative).** Would need partner progress pushed between
clients during the raid, presumably over Fika's own networking. Materially bigger, brings QuestMap
close to profile synchronization, which its authority boundaries and `quest-actions-plan.md`
deliberately leave to Fika. Do not start this without deciding that boundary question first.

### Open questions

1. **Does Fika expose the session roster publicly, and does it yield a profile id** matching what
   `SaveServer.GetProfiles()` returns, or only a nickname needing a lookup? **Unverified** — Fika
   is open source, so read its source rather than guessing, and do not decompile the installed DLL.
   Only blocks stage 3.
2. **Can the client reach `/questmap/client/coop/*` during raid load?** Expected yes — same HTTP
   host the client already uses — but confirm, and confirm it does not extend load time
   measurably. `RaidPerformanceTelemetry` is the place to measure.
3. **Screen space.** The hotkey raid list already uses full available height with its own bands.
   Decide whether partners are extra bands, a toggle, or a separate page before building.
4. **How many partners?** The pool is N, but a raid list showing four players' quests may be
   unreadable. Consider capping what is rendered in raid.
5. **Does upstream want a Fika dependency at all?** Upstream lists Fika compatibility for
   handover/turn-in, so Fika-awareness is not foreign — but a soft dependency on Fika *types* is a
   bigger commitment than tolerating its presence. Ask before stage 3. Stages 1 and 2 raise no such
   question.

### Where this lives

Roster discovery is client-side and Fika-specific, so it does **not** belong in
`SPTQuestMap.Core` — Core is `netstandard2.1` and shared with the server, which has no business
knowing about Fika. Keep it in `SPTQuestMap.Client` behind a small interface with a no-Fika
implementation, so the co-op views depend on "who are my partners" rather than on Fika.

Map filtering, by contrast, **is** a Core rule and already exists there. Do not reimplement it in
the raid runtime.

---

## 2. Validate with more than two profiles

Nothing has ever been run against three or more profiles, or against profiles at very different
levels, outside unit tests. The pool code is written for N but has only ever *lived* at N=2.
`ScramJe1` (level 69, 558 quests, 0 completed) and `Regdab` (level 8) are the awkward cases
already sitting in the test install.

This is cheap and should probably happen before any further co-op feature work, since it would
expose bad assumptions early.

---

## 3. Finish the localization story

Only `en.json` and `ge.json` carry the `catchup.*` keys; the other 15 locales fall back to
English. A completeness test enforces German specifically. Upstream will likely want the rest
before merging. Parked until just before the PR — see `STATE.md`.

---

## 4. Smaller items

- **Objective mix plural forms** — it currently renders "1 builds", and its counts describe what
  the quest asks for rather than what remains.
- **Cross-trader prerequisite chains** in chain depth. Currently longest-path per trader, which
  suits the trader-questline framing but may distort recommendations where a chain crosses
  traders.
- **Standalone quests in series grouping.** Only ~30% of quests belong to a named series; the rest
  appear only in trader gaps. A limit of name-based grouping, not a bug, but worth revisiting if
  the planner feels sparse.

---

## Parked

- **Quest write actions from the browser** (accept/hand-in/restart). Designed in
  `quest-actions-plan.md`, deliberately not built. The native client owns live actions; the
  browser stays read-only.
- **Widening pairwise Compare Mode to N profiles.** The graph draws exactly two status lanes and
  the difference categories are inherently two-way. The N-profile analysis is the separate co-op
  pool; keep them separate.
- **QuestMap owning profile synchronization.** That is Fika's job, and taking it on would put
  QuestMap in the business of writing profiles, which its authority boundaries forbid.
