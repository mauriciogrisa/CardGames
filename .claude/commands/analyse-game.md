---
allowed-tools: Bash,Read,Glob,Grep,Write,Edit,Agent
description: Read Pontinho game logs and produce CPU improvement recommendations plus human player tips
---

Read the game log file at `C:\Users\mgrisa\Documents\test\CardGames\bin\Debug\net10.0\logs\game_log.txt` and perform a thorough post-game analysis. You have full permission to run any tool needed — do not ask for confirmation.

## Log format reference

```
=== NEW GAME  |  Players: N  |  Difficulty: VeryHard ===
--- Round N ---
[T1] PlayerName drew from deck: K♠
[T1] PlayerName drew from discard: 7♥  (second draw)     ← second draw after discarding deck card
  PlayerName discarded: 4♦  (h:9)                        ← h: = hand size after discard
  PlayerName laid down: 7♥ 8♥ 9♥
  PlayerName added to [Sequence]: J♠
  PlayerName added to [Triple]: Q♦ Q♣                    ← multi-card add (shows actual cards)
  PlayerName swapped Q♥ for joker in [Sequence]
  PlayerName played all cards — round won
  CPU X discarded first card: 5♣  (drawing second)       ← CPU used second-draw privilege
--- Round N scores ---
  PlayerName: +N pts (total: N)
=== GAME OVER ===  Winner: PlayerName
```

Turn numbers `[TN]` reset each round. `(h:N)` is hand size **after** the discard. `[EDGE CASE]` prefix marks CPU no-legal-discard events.

## What to analyse

Work through ALL complete games and rounds in the log. For each, extract:

### Per-game stats
- Winner, number of rounds, player names, difficulty
- Round winners and their scores per round

### Aggregate stats across all games
Compute these rates (show counts and percentages):

**Draw behaviour**
- Second-draw usage rate per player (times used / times eligible as first player of round)
- Draw-from-discard rate per player (draws from discard / total draws)

**Lay-down efficiency**
- Average hand size at first lay-down per player
- Average number of combos laid on the winning turn

**Discard patterns**
- Average hand size at game-end loss (penalty indicator)
- Players who frequently discard high-value cards (J/Q/K/A/Joker) early

**Joker usage**
- Joker swap events per player
- Joker-related wins (all-joker sequence, joker-at-end win)

**`[EDGE CASE]` events**
- How often CPUs hit no-legal-discard; which CPUs and which rounds

### CPU-specific improvement opportunities
For each CPU pattern found, state:
1. What the CPU is doing suboptimally (with log evidence — cite turn numbers)
2. Which `AiDecisionService` method / difficulty branch is likely responsible
3. A concrete recommendation

Focus especially on:
- CPUs discarding cards that extend table combos (anti-feed failure)
- CPUs not using second-draw when they should
- CPUs drawing from discard but not incorporating the card into combos
- CPUs holding high-point cards late (large penalty at round end)
- Urgency mode not activating when opponent hand is ≤ 4

### Human player tips
Based on the human's play patterns, give 3–5 actionable tips. Be specific — cite turns where the human missed an opportunity or made a suboptimal play. Examples: "In Round 3 T7 you discarded Q♠ which extended the existing Q sequence on the table — the CPU picked it up next turn."

### Summary table
End with a markdown table:

| Metric | Human | CPU avg | Best CPU | Worst CPU |
|--------|-------|---------|----------|-----------|

Include: win rate, avg penalty/round, second-draw usage %, discard-from-discard %, avg hand at laydown.

---

If the log is empty or has fewer than 2 complete rounds, say so and stop — do not fabricate data.
