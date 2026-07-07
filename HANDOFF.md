# HANDOFF.md — Context Snapshot

**Flushed at:** 2026-07-07T16:05:00Z (manual flush; user: save → /clear → resume)
**Session bundle:** `state/sessions/20260618T222458Z-17e6742f/` — **AUTHORITATIVE; follow RESUME-FIRST from CLAUDE.md first.** This file is only the quick-start pointer.

## CRITICAL — surviving agents (do NOT respawn)
The `/clear` does NOT kill background agents (memory `clear-keeps-background-agents-alive`). Addressable by name via SendMessage:
- **dev-t101** (blue) — IDLE ON HOLD (msg 979ab379): no new assignment until rev-t051's verdict; a 051 fix round is its next work if findings arrive; else dispatch next queue item (TASK-045 or TASK-029 — pick by Editor-boundary fit).
- **rev-t051** — REVIEWING TASK-051 (FULL), pinned at 0d1b052. Audits 3 flagged dev calls (wager gestures, baseStars=0, default-payout scope addition) + constructor-draw seed derivation (third derivation idiom — hardest scrutiny) + climax double-payout unspecced path.
- **rev-t046** — REVIEWING TASK-046 (FULL), pinned at 347cbae. Audits the normalized-convention call (regression sweep for magnitude-sensitive consumers!) + the DirectionToUnit leave-alone disposition.
- Idle/free: **rev-t024, rev-t048, rev-t050, rev-c035, rev-c044, rev-c037, rev-t026, rev-t103, rev-t030** — verdicts recorded in tickets; don't reuse for new tickets; fix-round re-verifies go to the original reviewer.
Before ANY new spawn: `git log --oneline -10`, `git status --short`, try SendMessage to the names above.

## Active tasks
- **TASK-051 ScoringV2↔FSM wiring** (`in_review`, rev-t051): 2c44d2a → 0d1b052, 538/538. ON CLOSE-worthy verdict → `close_task` [2c44d2a, 0d1b052], depth FULL + rubric, chore(state), PUSH. On findings → fix round to dev-t101, rev-t051 re-verifies.
- **TASK-046 shared InputBridge** (`in_review`, rev-t046): e4cfc4b (refactor: shared InputBridge, copies deleted, NORMALIZED convention) → 347cbae (locks: 9-value round-trip + cross-mechanic), 544/544. ON CLOSE-worthy verdict → `close_task` [e4cfc4b, 347cbae], chore(state), PUSH. On findings → fix round to dev-t101, rev-t046 re-verifies.

## Today's ledger (all pushed; origin in sync at 6214163; suite 538/538)
**11 closes:** TASK-025, 026, 030 (overnight) + 048, 024 (Editor-free Hito 1 core COMPLETE), 035, 044, 037 (cloud batch integrated per-ticket; T-108 selector + T-109 scoring + PCG32 §13 done), 050 (validator anti-drift sensor). **Cloud episode:** branch `claude/gdd-hito1-cloud`/PR #1 reviewed per-ticket, integrated via cherry-pick, cloud-024 SUPERSEDED by local (decision in bundle). **Filed:** TASK-048(closed)/049 (CI)/050(closed)/051. **Carry-in nits parked:** TASK-032 (SeededRandom.cs:75 unchecked + RngStream.Session doc), TASK-027 (RenderEntity.cs:30 citation), TASK-040 ([6,2,2,2] asym authoring + test-comment fix).

## HUMAN ITEMS OUTSTANDING (surface these on resume)
1. **PR #1 comment + close unmerged**: `gh pr close 1 --comment "Integrated per-ticket onto master (TASK-035/044/037 closed, 518/518→538/538); cloud TASK-024 superseded by local; chore commits deliberately not picked. Branch kept for provenance."` (permission-gated for the orchestrator).
2. **Restore context-monitor hooks** (stash took them; permission-gated for orchestrator): `git show "stash@{0}^3:.claude/settings.json" > .claude/settings.json` — then optionally `git stash drop` (finalizes the Esquiva3D.unity/EditorBuildSettings.asset regen-noise discard the user was already leaning toward; all bookkeeping in the stash is superseded+committed).
3. **GDD pass (bugs CONFIRMED by review)**: line 496 Zen star row truncated mid-cell («| Estrella Zen | Menor |»); §5.6 vs §6.3 star-count conflict (impl follows §6.3 exactly-2) AND §5.6's bullet decomposition sums 4–6 vs stated 2–4.
4. **Ratify [ASSUMED] calibrations** (all test-pinned, cheap to flip): JOIN window stays open to 30 s unless all 4 claim; JOIN 0-ready timeout advance; wager gestures L=25/U=50/R=75 + timeout default=Half; star-tie=no-holder; empty-pool star fallback=least-harmful; baseStars=0 until Hito 4; regular rounds pay GDD-default tables until per-definition payoutTable threading.
5. **Unity window** (Editor OPEN+FOCUSED + human): TASK-027 StagePresenter (carry-in note on ticket) + TASK-028 scene/UAT; TASK-030 Unity gate (MigrateAll/ValidateAll/PlayMode/LOW-7) + TASK-047 GUID in the same window.

## Standing rules (unchanged + new)
- GDD canonical; push after every close (pre-authorized); one writer on master; reviewers pin detached worktrees; no rework on a surface with an open review; briefings fence ticket text.
- **NEW (13:30Z decision):** orchestrator COMMITS bookkeeping immediately after writing it; agents NEVER clean/stash/checkout tasks/, state/, or untracked files (pause + ping instead); tasks/ cherry-pick conflicts resolve --ours. See memory `orchestrator-commits-bookkeeping-immediately`.
- Env: system-PATH dotnet is runtime-only → `"$HOME/.dotnet/dotnet" test D:/barcade/fast-tests/Barcade.Core.FastTests` (SDK 8.0.422).
- Cloud routine was ONE-SHOT (fired 08:30Z, fully processed). No further cloud runs armed.
- Timestamps: write REAL UTC (check the clock; don't derive from old stamps — this session mislabeled ~5 h early once).

## Next queue after 051+046 close (dependency order, Editor-free)
TASK-045 (generator durations, tests-after) → TASK-029 (input hardening) → TASK-049 (CI workflow, uat-only). Then **TASK-034 (T-107, BIG — consider splitting; MUST retire v1 SequencerDirector per TASK-035 close condition)**. TASK-032 replay is unblocked (PCG32 done) but sequenced later per Annex D.
