# Roadmap

**Load this when** deciding what to build next, or when picking up an item below.

What is *done* lives in `STATE.md`; what is *ruled out* lives under **Parked** here and in
`development-status.md`'s "Superseded designs". This file is only for work not yet started.

Items are ordered by value, not by sequence. Nothing here is committed to a release.

---

## 1. See your current co-op partners' quest progress

**The goal:** when you are in a Fika session with other players, QuestMap should already know who
they are and show their quest progress, instead of making you pick profiles from a list by hand.

### What already works

Most of this is built. The pieces are:

| Piece | State |
| --- | --- |
| Reading every profile's quests | **Done.** The server enumerates them via `SaveServer.GetProfiles()`. |
| Shipping N profiles to the client | **Done.** `CoopProfileSnapshot` over `/questmap/client/*`. |
| Showing N profiles' progress in game | **Done.** The Coop Maps and Coop Catch Up tabs. |
| Showing N profiles' progress in the browser | **Done.** The Coop Map Helper. |
| Knowing *which* profiles are your partners | **Missing.** You select the pool by hand. |

So this is **not** a data or display feature. It is a *roster* feature: auto-populate the co-op
pool from the live session. That is a much smaller change than it first appears, and most of the
risk sits in one dependency.

### The constraint that shapes the design

**During a raid, the server's copy of your partner's profile is stale.** Their quest progress
advances on *their* client and reaches the server when Fika syncs it — in practice at raid end,
not continuously. So "live in-raid progress" is not straightforwardly available, and a UI that
implied it would be lying.

Be honest in the UI about which of these it is:

- **Roster live, progress as of last sync** — achievable, and probably what the feature should be.
- **Roster and progress both live** — needs a per-tick sync path that does not exist today, and
  would mean QuestMap taking on profile synchronization, which `quest-actions-plan.md` explicitly
  scopes out as Fika's job.

Between raids, in the hideout or menu, there is no staleness problem at all — that is where this
feature is most useful and most correct.

### Staged approach

**Stage 1 — remember the pool (no Fika dependency).** Persist the selected co-op pool and offer
it on next load. Cheap, no new dependency, and it removes most of the repetitive selection. Worth
doing regardless of whether stage 2 ever happens, and it is the fallback when Fika is absent.

**Stage 2 — read the roster from Fika (soft dependency).** Ask Fika for the players in the current
session, map them to profile ids, and pre-select the pool. Must be a **soft** BepInEx dependency
(`[BepInDependency(..., DependencyFlags.SoftDependency)]`) so QuestMap still loads and behaves
normally with no Fika installed — the mod's whole compatibility story is that it degrades rather
than aborts.

**Stage 3 — refresh on raid end.** Re-pull partner snapshots when the raid ends and Fika has
synced, so the post-raid view is correct. QuestMap already schedules a coalesced topology reload
on raid exit; this would ride along with it.

### Open questions — resolve before designing further

1. **Does Fika expose the session roster publicly, and with what?** Player list, and crucially
   whether it yields a **profile id** that matches what `SaveServer.GetProfiles()` returns, or
   only a nickname / session id needing a lookup. **Unverified.** Fika is open source — check its
   source rather than guessing, and do not decompile the installed DLL.
2. **Is the roster reachable from where QuestMap's plugin runs?** Both are client-side BepInEx
   plugins in the same Unity process, which is promising — no server round-trip needed for the
   roster, only for the progress QuestMap already fetches. Confirm load order and that the type is
   public.
3. **Dedicated/headless host vs P2P host** — does the roster look different, and does the host's
   own profile appear in it?
4. **Version coupling.** Fika here is `2.3.9`, and QuestMap deliberately guards exact SPT/EFT
   versions. Decide what happens on a Fika version it does not recognize: degrade to manual
   selection (consistent with the existing compatibility posture) rather than fail.
5. **Does upstream want a Fika dependency at all?** Upstream already lists Fika compatibility for
   handover/turn-in, so Fika-awareness is not foreign to the project — but a *soft dependency on
   Fika types* is a bigger commitment than tolerating Fika's presence. Ask before building
   stage 2.

### Where this would live

Roster discovery is client-side and Fika-specific, so it does **not** belong in
`SPTQuestMap.Core` — Core is `netstandard2.1` and shared with the server, which has no business
knowing about Fika. Keep it in `SPTQuestMap.Client`, behind a small interface with a
no-Fika implementation, so the co-op views depend on "who are my partners" and not on Fika.

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
