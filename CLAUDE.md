# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Coding Standard

All code must follow **Clean Code** principles by Robert C. Martin: meaningful names that reveal intent, small functions that do one thing, named constants instead of magic numbers, no redundant comments (rename instead), DRY (extract shared logic), Single Responsibility per class/method, guard clauses instead of nested ifs.

---

## Regression Prevention

There is an automated E2E test suite in `CardGames.Tests/` (NUnit + Playwright, Chromium). Run with `dotnet test CardGames.Tests` — requires the app to be running on `localhost:5000` first. High-risk changes should also be validated manually with the checklist below.

### Static asset versioning — ALWAYS do this first

| File changed | Required action |
|---|---|
| `app.js` | Increment `?v=N` on the `<script src="app.js?vN">` tag in `App.razor` |
| `app.css` | Increment `?v=N` on the `<link href="app.css?vN">` tag in `App.razor` |

Browsers cache these files aggressively. Forgetting this causes stale JS/CSS to run silently.

### Blazor Server async event handlers — strict rules

- **Never introduce a new `async Task` event handler** in `Game.razor` without careful review.
- **`ondrop` and `ondragend` fire in rapid succession.** If an `async` drop handler `await`s anything (including JS interop), Blazor may start the `ondragend` handler while the drop handler is suspended. This corrupts shared state (`_dragFrom`, `_draggingFromDeck`, etc.).
- **Rule:** All drag-state reads and all `ClearDragState()` calls must happen **synchronously before the first `await`** in any drop handler.
- **Rule:** Never call JS interop from `ondrop`/`ondragend` handlers. The `ondrop` / `ondragend` pair creates a race that breaks component state.
- Existing async handlers (`OnDropToDiscard`) are safe because they call `ClearDragState()` before their first `await`.

### `ShouldRender()` / `_skipRender` pattern

`Game.razor` overrides `ShouldRender()` using an opt-in skip flag `_skipRender`. The flag defaults to `false` (all renders pass through); only handlers that return early with **no visible state change** set it to `true`.

- **Do** set `_skipRender = true` immediately before an early `return` in a synchronous void handler that mutates nothing (wrong phase, non-target drop, etc.).
- **Do not** set `_skipRender = true` in any path that mutates game state, drag state, or any field that affects rendering.
- `@bind`, `@onchange`, and inline `@onclick` lambdas are managed by Blazor and never touch `_skipRender` — they are unaffected by this pattern.

### Hand-card reorder architecture (`HandleHandCardDrop`)

Hand-card drag reorder is **not handled via Blazor's `@ondrop`** event. Instead:
- `app.js` registers a capture-phase `document.addEventListener('drop', ...)` listener that fires **before** Blazor's event delegation.
- When a hand-card drop occurs on `.hand-fan`, the listener calls `_handDropRef.invokeMethodAsync('HandleHandCardDrop', _from, _dropTarget)`.
- `HandleHandCardDrop` is a `[JSInvokable]` method on the `Game` component. It calls `GameSvc.ReorderHand(from, target)` then `InvokeAsync(StateHasChanged)`.
- `OnDrop()` in `Game.razor` is now a **synchronous void** method that only handles deck/discard drags to hand cards. It must stay synchronous — no `await`.
- The `DotNetObjectReference<Game>` (`_dotNetRef`) is registered in `OnAfterRenderAsync(firstRender)` and disposed in `DisposeAsync`.
- **Why this design:** Blazor's `@ondrop` event delegation does NOT fire for synthetic `DragEvent` objects dispatched via `dispatchEvent()` in JS (used by E2E tests). The JS→DotNet direct invocation works for both real and synthetic events.
- **`dragover` position indicators**: the JS `dragover` listener shows `drop-before`/`drop-after` highlights on hand cards for **all** drag sources — not just hand-card reorder. The early-exit `if (_from < 0) return` was intentionally removed so that dragging from the deck or discard pile also shows the insertion indicator. The no-op guard (`ins === _from || ins === _from + 1`) is conditioned on `_from >= 0` since it only applies to reorder drags.

### `allowJokerAtEnds` and `isWinningMove` — thread through every layer

These two booleans must be passed explicitly at every call site. Missing one silently rejects valid winning moves:

```
WebGameService.TryLayDown / TryAddToCombo
  → CombinationValidator.Classify(cards, allowJokerAtEnds)
  → TurnService.ExecuteLayDown(…, allowJokerAtEnds)
  → Combination.AddCard(card, isWinningMove)
    → Combination.CanAccept(card, isWinningMove)
```

- `allowJokerAtEnds = isWinningMove = (Hand.Count <= cards.Count + 1)` for lay-down
- `isWinningMove = (Hand.Count <= 2)` for single-card add-to-table
- `isWinningMove = (Hand.Count <= cards.Count + 1)` for multi-card add-to-table
- For `TryPartition`: `allowJokerAtEnds = true` **only** when all hand cards are selected
- **Near-win lay-down**: laying `Hand.Count - 1` cards also satisfies `isWinningMove`. `AiDecisionService.DetermineLayDownPlan` tries greedy → joker-at-ends → exhaustive partition → near-win per-card exhaustive search (iterates each non-joker as candidate discard, calls `TryPartitionAll(hand minus that card, allowJokerAtEnds: true)` — catches cases where greedy joker allocation blocks the optimal split).
- **`FindBestCombinationSet` sort order**: primary key = card count descending (maximise cards removed); secondary key = total point value of the combo descending (prefer laying down higher-value cards so the remaining hand carries less penalty). Changing this sort breaks the anti-overlap greedy and is high-risk.
- **`ScorePlan` / `swapImprovesScore` in `TryJokerSwaps`**: a joker swap is accepted for non-winning cases only when `ScorePlan(simHand, simCombosNormal) > ScorePlan(hand, bestCombos)`, where `ScorePlan = cards_removed × 1000 − remaining_penalty`. Without this guard, the greedy can pick a joker-sequence that displaces a larger non-joker combo, reducing coverage or inflating penalty. Winning and near-win swap paths bypass this guard (they always accept).

### Phase guards — every new `WebGameService` public method needs one

```csharp
if (Phase != GamePhase.AwaitingAction) return;  // or the appropriate phase
```

Without this, the method is silently callable during CPU turns.

### Coding conventions that must not be violated

1. **`SelectedCards` uses `ReferenceEqualityComparer.Instance`** — never use value-equality `.Contains` on it.
2. **`ClearDragState()` before any game mutation in drop handlers** — never after.
3. **Never discard a joker** — check `IsJoker` before any discard path.
4. **`_dragFrom`, `_draggingFromDeck`, `_draggingFromDiscard` are read-before-write** — always clear them synchronously before mutating game state.

### Manual test checklist after high-risk changes

Run these scenarios after any change to game logic, drag-and-drop, or turn flow:

1. **Second-draw privilege**: go first → draw from deck → discard that exact card → second draw offered. Draw from discard instead → no second draw.
2. **Joker at ends (winning only)**: lay down a sequence with joker at end when 1 card remaining → succeeds. With 2+ cards remaining → fails.
3. **Triple duplicate-suit card**: add a duplicate-suit card to a triple → visible during turn → gone from triple after discard, appears as discard top.
4. **Joker swap enforcement**: take joker via swap → discard attempt blocked → use joker in combo → discard succeeds.
5. **Discard-drawn card enforcement**: draw from discard → try to discard another card → blocked → use drawn card in combo → discard succeeds. Return drawn card via drag-to-deck → phase reverts to AwaitingDraw.
6. **Multi-combo lay-down**: select 6+ cards forming 2 valid combos → lay down → both appear on table.
7. **All-joker winning sequence**: reach a state with 3 jokers as last cards → lay down as sequence → round won.
8. **CPU joker-swap chain to win**: CPU has Q♥ in hand and two J♥ JKR K♥ sequences on table → CPU swaps both Q♥s for the jokers → lays down JKR JKR JKR → round won.

### Risk classification

**High-risk** (run full checklist above):
- Any new play action in `WebGameService`
- Any change to `CombinationValidator`, `CombinationFinder`, `TurnService`
- Any change to `allowJokerAtEnds` / `isWinningMove` logic
- Any new `async Task` event handler in `Game.razor`
- Any change to turn-state fields (`_needFirstTurn`, `_firstDrawnCard`, `_tripleDiscardsPending`, `_forfeitedSwaps`, `Player.SwappedJoker`, `Player.DiscardDrawnCard`)
- Any change to `CombinationValidator.IsValidSequence` or `IsValidTriple`
- Any change to `AiDecisionService.TryJokerSwaps`, `ExecuteCpuPlayStep`, `DetermineLayDownPlan`, `DecideDiscard`, or `DecideDrawSourceHard`
- **`DecideDiscard` contract**: the local `hand` variable is pre-sorted descending by `PointValue` at the top of the method. All `.Where()` passes below it rely on this ordering — do not re-sort or insert an `.OrderByDescending` anywhere inside `DecideDiscard`.
- Any change to `AdvanceToNextPlayerPhase` or player elimination logic

**Low-risk** (verify visually, no full checklist needed):
- CSS / layout / text changes (including deal animation CSS)
- `LanguageService` string additions
- `GameLogger` format changes
- `GetSeat()` mapping (but note: swapping left↔right reverses the anti-clockwise flow for all counts 1–5)
- Score table / round-over panel display
- `CardClass()` helper

---

## Commands

`CardGames.Tests/` is a **sibling** of `CardGames/`, not a child. All `dotnet test` commands must be run from the **parent directory** (one level up from the project root, where both `CardGames/` and `CardGames.Tests/` live).

```bash
# Run the app (auto-opens browser) — from the parent directory
dotnet run --project CardGames

# Build only — from the parent directory
dotnet build CardGames

# If the build fails with a file-lock error, kill the running process first:
taskkill //IM CardGames.exe //F

# Run E2E tests (app must already be running on localhost:5000) — from parent directory
dotnet test CardGames.Tests

# Run a specific test
dotnet test CardGames.Tests --filter "TestName"

# Run only unit tests (no Playwright / no running server required)
dotnet test CardGames.Tests --filter "Category=Unit"

# Run unit + integration tests (no Playwright)
dotnet test CardGames.Tests --filter "Category=Unit|Category=Integration"
```

### Test categories

| Category | Files | Requires |
|---|---|---|
| `Unit` | `CombinationFinderTests`, `CombinationTests`, `CombinationValidatorTests`, `ModelTests`, `TurnServiceTests`, `AiDecisionServiceInternalTests` | Nothing |
| `Integration` | `AiDecisionServiceTests`, `WebGameServiceTests` | Nothing |
| E2E (no attribute) | `HandDragTests`, `BurnAnimationTests`, `DiscardPileTests`, `CpuMultiComboAnimationTests` | App on `localhost:5000` + Playwright/Chromium |
| `E2E` (attribute) | `Phase5VerificationTests` | App on `localhost:5000` + Playwright/Chromium |

`HandDragTests`, `BurnAnimationTests`, `DiscardPileTests`, and `CpuMultiComboAnimationTests` extend Playwright's `PageTest` but do not carry `[Category("E2E")]`. They are excluded by the `Category=Unit|Category=Integration` filter, so the filter-based commands above are safe even without an explicit E2E category.

## Architecture

**CardGames** is a Pontinho (Brazilian Rummy variant) for 1 player vs 1–7 CPUs (up to 8 players total), built with **Blazor Server** (.NET 10). All game logic runs server-side; the browser updates via SignalR. There is a single page (`/`) rendered by `Components/Pages/Game.razor`.

### Game Rules Implemented
- 2 full decks + 4 Jokers (108 cards total); 9 cards dealt per active player
- **Triple**: 3–6 cards of the same rank, all different suits. **Sequence**: 3+ cards of the same suit in rank order.
- A duplicate-suit card may be added to a triple during a turn (visible there temporarily); at turn end it is removed from the triple and placed on the discard pile behind the player's discarded card.
- Jokers act as wildcards in sequences but **cannot be at either end**, and two or more adjacent jokers are also disallowed — both exceptions apply only on the winning move. All-joker sequences (JKR JKR JKR etc.) are valid on a winning move. `CombinationValidator.IsValidSequence` returns `allowJokerAtEnds` immediately when `nonJokers.Count == 0`.
- Jokers **cannot be discarded**.
- First turn of the round: only the player who goes **first** gets the second-draw privilege (draw from deck → discard it → draw again). If that player drew from the discard pile and returns the card, the privilege is restored.
- Drawing from the discard pile requires using that card in a combination (or returning it).
- **Joker swap**: a player may take a joker from a table sequence by placing the card it represents. The joker enters the hand and cannot be discarded. A card that can replace a table joker **cannot be discarded** — `TryDiscard` blocks it, adds it to `_forfeitedSwaps`, and also locks `TrySwapJoker` / `TryAddToComboByDrop` for the rest of that turn. `_forfeitedSwaps` is cleared on the next draw.
- **1-card draw-from-discard restriction**: if the player has exactly 1 card in hand and the discard top can be accepted by any existing table combo, drawing from the discard pile is blocked.
- **Multi-combo lay-down**: selecting cards that don't form a single combo triggers `TryPartition` to split them into 2+ valid combinations. `allowJokerAtEnds` is `true` only when all hand cards are selected.

### CPU Difficulty

All games use a single fixed difficulty (previously called `VeryHard`). The `Difficulty` enum and all lower-difficulty branches have been removed. CPU behaviour:

| Dimension | Behaviour |
|---|---|
| Draw from discard | If the card forms a new hand combo OR extends an existing table combo |
| Second-draw (first-to-act) | Redraw if drawn card is unprotected (no near-combo partner) and is a high-tier isolated card |
| Add to table | Yes — after lay-down, greedily adds cards to existing combos |
| Discard strategy | Near-combo protected + anti-feed (threshold ±1 of discard top, all opponent discards as hot zones) + opponent-discard pattern filter |

### Service Layer (key files)

| Service | Role |
|---------|------|
| `WebGameService` | Scoped. Central orchestrator. Owns the `GamePhase` state machine and all player action methods. Entry point for everything the UI calls. |
| `AiDecisionService` | Decides each CPU action (draw source, combos to lay, cards to add, what to discard). Returns a `CpuTurnSummary` after each turn. |
| `CombinationValidator` | Pure static logic — validates triples and sequences; used by both AI and human action paths. `GetRejectionReason(cards, allowJokerAtEnds)` returns a `ComboRejection` enum value explaining *why* a set of cards is invalid (e.g. `SeqMixedSuits`, `SeqJokerAtEndNotWinning`, `TripleDuplicateSuit`). Used by `WebGameService.TryLayDown` to populate `ErrorMessage` with a specific explanation. |
| `CombinationFinder` | Finds best non-overlapping combination set in a hand; also identifies "protected" cards (near-complete combos) to guide CPU discard decisions. `FindAllSequences` and `FindBestCombinationSet` accept `allowJokerAtEnds`. |
| `TurnService` | Atomic actions: draw, lay down, add to table, discard. Mutates `GameState`. `ExecuteLayDown` and `ExecuteAddToTable` accept `allowJokerAtEnds` / `isWinningMove`. |
| `LanguageService` | Scoped. All UI strings in English and Portuguese. Set via JS interop on first render from `navigator.languages`. |
| `GameLogger` | Singleton. Appends timestamped events to `bin/Debug/net10.0/logs/game_log.txt`. Useful for post-game bug analysis. `[EDGE CASE]` prefix marks CPU no-legal-discard events for easy grepping. Round header lists all active players uniformly by name with `(CPU)` tag for CPU players (detected via `p is CpuPlayer`), left-padded to 14 chars. |

### State Machine (`GamePhase` enum)
```
Welcome → AwaitingDraw → AwaitingAction → CpuTurn → RoundOver → GameOver
                ↑                ↓              ↓
                └────────────────┘              └→ AdvanceRound → (new round or GameOver)
```
`WebGameService` enforces phase guards on every public method. The UI renders based on the current phase. With multiple CPUs, `CpuTurn` may repeat for each CPU before returning to `AwaitingDraw`; `Game.razor` handles this with a `RunCpuTurns()` loop (`while Phase == CpuTurn`).

### Key design decisions

- `Card` is a **class** (not record) — reference equality is intentional because two decks produce duplicate rank/suit cards that must be distinguishable by identity. `SelectedCards` and all joker-swap lookups use `ReferenceEqualityComparer.Instance`.

- **`isWinningMove` for add-to-table is `Hand.Count <= 2`** (single card) or `Hand.Count <= cards.Count + 1` (multi-card). With 2 cards in hand, a player may add one (joker-at-end) and then discard the other to win. For lay-down, `isWinningMove` is `Hand.Count <= cards.Count + 1`.

- **`TryPartition`** (`WebGameService`): backtracking exhaustive search that splits a card list into 2+ valid combos. Called by `TryLayDown` whenever single-combo `Classify` fails. `allowJokerAtEnds` is `isWinningMove`, so joker-at-end partitions require all hand cards to be selected. Minimum 6 cards required.

- **`Combination.IsWinningLaydown`**: set to `true` by `TurnService.ExecuteLayDown` when `player.Hand.Count <= 1` after cards are removed. Used by `Game.razor` to suppress ghost slots entirely on that combo. Only the newly laid-down combo is flagged — earlier combos laid down the same turn are unaffected.

- **`Combination.IsWinningAddition`**: set to `true` by `TurnService.ExecuteAddToTable` when `player.Hand.Count <= 1` after the card is removed. Used by `Game.razor` via the `suppressNewBatch` boolean: when a winning add fills the last ghost slot in the current batch (`Cards.Count % 3 == 0`), `batchGhostCount` is forced to 0 — suppressing the 1→3 jump that would otherwise start a new batch. Mid-batch adds (3→2 or 2→1) need no suppression — ghost count simply decrements. **Applies to sequences only** — triples don't batch-cycle so no suppression is needed. Width stability is automatic: with `batchGhostCount=0`, `ghostPad=0`, so the combo-row width = `1 + (Count-1)×seqStep` stays identical to the previous card count's width.

- **Constraint golden rule**: `Player.SwappedJoker` and `Player.DiscardDrawnCard` apply equally to human and CPU. `TurnService` is the single enforcement layer — `ExecuteDiscard` throws `InvalidOperationException` for all four hard violations (joker discard, must-swap-joker, unused swap joker, unused discard-drawn card). Exception: must-swap-joker is gated by `player.Hand.Count > 1` (winning discard allowed). `WebGameService.TryDiscard` mirrors these checks as early-return guards for user-facing errors.

- **Joker swap preserves hand position**: `TurnService.ExecuteSwapJoker` uses `Hand.IndexOf(replacement)` to find where the swapped card sits, then `Hand.Insert(pos, joker)` to place the obtained joker at that same index. The joker is also added to `SelectedCards`. `Hand.IndexOf` and `Hand.Insert` are dedicated methods on the `Hand` class (not on the `IReadOnlyList<Card>` view).

- **`InternalsVisibleTo` for unit testing**: `CardGames.csproj` declares `InternalsVisibleTo("CardGames.Tests")`. Several `AiDecisionService` methods are `internal` for direct unit testing: `IsUrgent`, `TryWinByAddingFirst`, `WinsByAddingToTable`, `CanAddToSimCombo`, `FindViableNearCombinationCards`. `Deck.AddToDrawTop(card)` is also `internal` — places a card on top of the draw pile (next to be drawn). Use it in tests instead of `ReturnToDeck`, which inserts at the **bottom**. These tests live in `AiDecisionServiceInternalTests.cs` under `[Category("Unit")]`.

- **Turn-state tracking fields in `WebGameService`** (non-obvious ones):
  - `_needFirstTurn` — `HashSet<string>` containing `_firstPlayerId` at round start; removed on first draw. **Restored** if the player returns a discard-pile draw — so the second-draw privilege comes back.
  - `_firstDrawnCard` — the exact card drawn from deck on the first turn; only discarding *this exact reference* triggers the second draw. Must be cleared on the *normal* discard path too — a stale reference causes a spurious second-draw offer later.
  - `_forfeitedSwaps` — `HashSet<Card>` (reference equality). Set when player tries to discard a swap-eligible card; blocks `TrySwapJoker` / `TryAddToComboByDrop` for the same turn. Cleared on each draw.
  - `_tripleDiscardsPending` — `List<(int ComboIndex, Card Card)>` tracking duplicate-suit cards added to a triple during the player's turn. Flushed by `FlushTripleDiscards()` just before the player's final discard. CPU equivalent: `CpuTurnSummary.TripleDiscardsPending`. Both paths call `ComputeBurnTargets` then fire `burnComboCards` fire-and-forget before discard.

- **`_roundAdvancing` guard** (`Game.razor`): `bool` field set to `true` at the start of `DoAdvanceRound` and reset in a `finally` block. Prevents double-advance when the auto-countdown fires `DoAdvanceRound` at the same time the user clicks "Next Round" — the second entry returns immediately. `DoAdvanceRound` performs exactly one round (deal + CPU turns); the countdown mechanism (`MaybeStartCountdown`) drives all subsequent rounds including simulation ones. `DoAdvanceRound` also guards against `Phase == Welcome` at its entry — if the user quit while `DoAdvanceRound` was suspended at an `await`, the resumed continuation exits without calling `GameSvc.AdvanceRound()`. **Quit race condition**: `DoQuit()` and `OnQuitClick()` cancel both `_countdownCts` **and** `_simCts` before calling `GoToWelcome()` — cancelling only `_countdownCts` left `DoAdvanceRound` running and able to overwrite the Welcome phase.

- **`IsSimulationLayout`** (`Game.razor`): `= IsSimulationMode && Phase != RoundOver`. Used instead of `IsSimulationMode` everywhere the layout switches — `UpperCpus`, seat-bottom rendering, and `ActiveLayoutCount` in `WebGameService`. The one-phase delay prevents a jarring DOM reconciliation flash on elimination: during the elimination `RoundOver` the layout stays in normal mode (player at `seat-bottom`, all CPUs in upper positions) and only switches to the simulation layout (CPU 0 at `seat-bottom`, `UpperCpus = ActiveCpus.Skip(1)`) once the next round starts. `UpperCpus` drives the entire upper-seat rendering; `SimSeatCount = ActiveLayoutCount - 1` controls the `GetSeat` index mapping.

- **CPU add-to-table legal-discard guard**: before adding a card to a combo, `AiDecisionService` simulates the hand after removal and checks `HasLegalDiscard`. If no legal discard would remain (and it's not a winning move), the add is skipped — that card stays for discard instead.

- **CPU joker-pairing guard** (`DecideCardsToAddToTable`): a non-joker card is NOT added to a Triple when the hand contains a Joker AND no Sequence exists on the table (and it is not a winning move). Rationale: keeping the card preserves the path "add card to Triple + add Joker to a future Sequence = win with 0 cards". Without this guard, the CPU would dump the combo card onto the triple and be permanently stuck holding an undiscardable Joker with nowhere to place it.

- **CPU joker swap winning-move lookahead**: `TryJokerSwaps` simulates with both `allowJokerAtEnds: false` (normal) and `allowJokerAtEnds: true` (winning). A swap is accepted if the post-swap hand can be fully laid down as a winning move — enabling chained swap paths (e.g. swap twice to accumulate jokers then lay down all-joker sequence).

- **`HasSecondDrawPrivilege`**: `true` when `Phase == AwaitingAction && _firstDrawnCard != null`. **Bug to avoid**: `_firstDrawnCard` must be cleared at the start of the *normal* discard path (not only the second-draw path) — if left stale it triggers a spurious second draw when the same card reference is discarded later.

- **Multi-player turn flow**: after any player finishes, `AdvanceToNextPlayerPhase()` checks `State.CurrentPlayer` to set `AwaitingDraw` (player) or `CpuTurn` (CPU). `GameState.Players` only contains active players (score < 100) for the current round. `WebGameService.Cpus` always contains all CPUs including eliminated ones — the UI uses `RoundCpus` (snapshot of active CPUs at round start) for rendering and seat assignment.

- **CPU turn is async in the UI**: `Game.razor`'s `RunCpuTurns()` loops `while Phase == CpuTurn`, calling `StateHasChanged()` + delay + `ExecuteCpuTurn()` each iteration. This gives "playing..." feedback (`MsgCpuThinking`) and handles consecutive CPU turns automatically.

- **CPU play animation** (`_cpuRevealComboCount`, `_cpuHiddenCards`, `_cpuHandBonus`, `_animatingCpuId`): purely rendering gates — the service mutates combos atomically; `RunCpuTurns` reveals them one card at a time. `_cpuHiddenCards` is a `Dictionary<int, HashSet<Card>>` (keyed by combo index, values use `ReferenceEqualityComparer.Instance`) that tracks which cards in a combo are still hidden; the template renders only cards NOT in the hidden set. Cards are removed from the set one at a time as each animation completes, triggering re-render. **`animateCardDraw` flight duration is 380 ms** — all `await Task.Delay(380)` calls in `RunCpuTurns` must match exactly. **`_cpuHandBonus`**: set to the total cards about to fly before the first `StateHasChanged()`; `DealCardCount` adds it so the CPU hand shows the right face-down count during flight; decremented once before each subsequent `StateHasChanged()`. A `finally` block always resets all four fields.
  - **CPU second-draw animation**: three steps at 700 ms / 700 ms / 430 ms — (1) deck→hand, (2) hand→pile + `_secondDrawToast` shown + `_cpuHandBonus = -1` + `StateHasChanged()` so the pile immediately shows the discarded card face-up while the hand count stays at N, (3) deck→hand + `_cpuHandBonus = 0`. The `_cpuHandBonus = -1` trick offsets the second drawn card that is already in game state but not yet animated. `_secondDrawToast` is cleared just before the CPU's final discard animation fires (`ExecuteCpuStep3Discard`).
  - **New combo animation ordering**: before the animation phases start, `Table.Combinations` at indices `[combosBeforePlay, combosAfterPlay)` is physically reordered via `TableState.ReorderNewCombinations(startIdx, desiredOrder)` so the screen position matches the reveal order — DFD combo (Phase 2) first, then `jokerSwapNewCombos` (Phase 3), then `newCombos` (Phase 5). `_cpuHiddenCards`, `_newComboIndices`, and the index lists are remapped via a `remap` dictionary immediately after. This ensures the CPU's combos always appear on screen in the same order they animate in.
  - **Joker swap animation**: `RunCpuTurns` captures `handBeforePlay` (the CPU's hand list) just before `ExecuteCpuStep2Play()`. After the swap, `playSum.SwappedJokers` gives the replacement cards; their pre-swap hand index is found via `ReferenceEquals` in `handBeforePlay`, giving the correct `.card:nth-child(N)` source selector. Without this snapshot the animation always flies the wrong card (`.card:first-child`).
  - **Add-to-combo animation**: `cardRefsBeforePlay` (per-combo card snapshots) is captured before play. After play, new cards are identified by `ReferenceEquals` against those snapshots to find their actual sorted position (`pi + 1`) as the animation target — necessary because `Combination.AddCard` calls `Sort()`, so new cards can land at any position, not just the end.

- **Human play animations**: the player's **discard** animates via `animateCardsPlay` (`.hand-fan .card.selected` → `.pile-stack`) with `await` before `TryDiscard()` so the DOM is captured before state changes. **Lay-down and add-to-combo do NOT animate** — `DoLayDown()`, `OnDropToTable()`, and `OnComboRowClick()` are all synchronous void methods that call the service directly. Do not re-add JS interop calls to these handlers.

- **`TryAddToComboByDrop`** (drag-and-drop to an existing combo): tries `CanAccept` first, then falls back to `CanReplaceJoker` (joker swap) only if add fails. This ensures that on a winning move the player wins immediately rather than taking a joker back into their hand. Clicking a joker card directly in a combo row calls `TrySwapJoker` explicitly. Clicking a combo row with a joker selected calls `TryReturnJokerToCombo`.

- **No hint highlights on combo rows**: combo rows do NOT highlight or show badges to suggest possible actions (no "Add card", no "Swap joker" badges, no green highlight when a selected card can be added). These were intentionally removed — do not reintroduce them. The only combo-row highlight that remains is `drop-target` (active drag-over) and `combo-can-accept` (for "Return joker" — only when `SwappedJokerComboIndex` matches that exact combo). Individual joker cards within a combo still receive the `joker-swappable` CSS class (via `canSwap && tableCard.IsJoker`) so clicking a joker directly triggers the swap; this is interaction affordance, not a hint.

- **Auto-select on draw**: cards drawn from the **discard pile** arrive in hand already selected (`SelectedCards` is set in `DrawFromDiscard`). Cards drawn from the **deck** are NOT auto-selected. Jokers obtained via swap (`TrySwapJoker`, `TryAddToComboByDrop` swap path) and the displaced card from `TryReturnJokerToCombo` are also auto-selected.

- **Drag-from-discard positioning**: `OnDropOnHandCard(int targetIdx)` replaces the generic `@ondrop="OnDrop"` on hand cards. When `_draggingFromDiscard` is set, it calls `DrawFromDiscardToPosition(targetIdx)` which draws the card (appended to end, auto-selected) then calls `ReorderHand(Hand.Count-1, targetIdx)` if needed. Drop on the pre-hand slot (`OnDropToStart`) also routes to `DrawFromDiscardToPosition(0)`.

- **Simulation mode** (`IsSimulationMode`): `true` when `MainPlayer.Score >= 100 && Phase != Welcome && Phase != GameOver`. Adds `sim-mode` CSS class to the game container. All CPU hands shown face-up via `OrderHandForDisplay` (combo-grouped, leftovers by suit/rank). Round progression uses the same countdown-driven flow as normal play: `DoAdvanceRound` performs exactly one round, then `MaybeStartCountdown` fires again to schedule the next. A "Skip simulation" button (`btn-skip-sim`) cancels both `_countdownCts` and `_simCts`, then calls `GameSvc.RunToGameOver()` to fast-forward synchronously — hidden once `isGameOver` is true. `RunToGameOver` loops `ExecuteCpuStep1Draw / Step2Play / Step3Discard` and `AdvanceRound` with all the same `_log.Log` calls as a live game, so the log is complete after skipping. The button is rendered **outside** `panel-messages-area`, directly between the score table and the message area. The Next Round button and countdown are also shown during simulation. **Player section in simulation**: every CPU stays in the exact seat it held when the game started (no CPU is moved to the player's bottom slot). The player's `seat-bottom` section is always rendered — in simulation mode it shows only the player's name with an empty hand (class `sim-eliminated`). This applies uniformly for all player counts (2–8). `ActiveLayoutCount` always returns `RoundCpus.Count + 1`, so the `players-N` CSS class is identical in simulation mode to what it was at game start. The `seat-left-stack` / `seat-right-stack` second-section rule uses `:nth-child(2)` (not `:last-child`) so that it targets the second section only when two sections are present.

- **Fireworks / Falling Ashes** (`app.js`, `Game.razor`): Two end-of-game animations using a full-screen canvas overlay (`position: fixed`, `pointer-events: none`). `window.startFireworks()` — 6.5 s, particle rockets + Web Audio sawtooth/noise sounds — triggers when `GameWinner ?? PendingGameWinner` matches `MainPlayer.Id`. `window.startAshes()` — **runs indefinitely** (no auto-stop), slowly drifting dark particles + Web Audio lowpass wind — triggers when the winner is someone else. Ashes are stopped explicitly by: `StopAllAnimations()` (new game / quit) and `DoAdvanceRound` when `IsSimulationMode` is true (simulation round starts). Guarded by `_fireworksTriggered` / `_ashesTriggered` bools (reset **after** `StartNewGame()` in `DoStartGame`, not before — resetting before would re-trigger on intermediate renders where `GameWinner` is still set). Both expose `window.stopFireworks()` / `window.stopAshes()`. Canvas is transparent between frames so the game UI remains visible underneath. `window._soundEnabled` guards all Web Audio calls.

- **Overlay toasts** (`Game.razor`): three independent fixed-position banners, all `position: fixed; top: 18px; left: 50%`. Each is a separate field — do not merge them.
  - `_lastCardToast` (orange, `.last-card-toast`): "one card remaining" warning. Set by `ShowLastCardToast(message, sound)` which plays a sound and auto-dismisses after 6 s via a versioned `InvokeAsync` callback. Click to dismiss early.
  - `_secondDrawToast` (teal, `.second-draw-toast`): CPU second-draw notice. **No sound.** Lifecycle is controlled directly in the animation sequence: set when the discard animation fires, cleared just before `ExecuteCpuStep3Discard`. `ShowLastCardToast` also clears it so they don't overlap.
  - `_roundEndToast` / `_gameEndToast`: round/game result banners. `ShowRoundEndToast` dismisses both `_secondDrawToast` and `_lastCardToast` first. `ClearAllToasts()` resets all three.

- **Ghost card slots**: **Triples** show `MaxTripleSize - Cards.Count` ghost slots using inline flex items (`card-ghost-triple`, 33% card-width); `ghostPad = 0`. **Sequences** batch-cycle 3→2→1→3… using absolutely-positioned `card card-ghost`. `seqStep = _combosShrunk ? 0.33 : 0.5`. `ghostStart = 1 + (N-1)×seqStep`; `ghostPad = batchGhostCount×seqStep` (the `padding-right` that covers the last ghost). Batch-width invariant: `ghostStart + ghostPad` = constant within a batch (normal: 3.50 / 5.00; shrunk: ≈2.65 / ≈3.64). `suppressNewBatch`: when `combo.IsWinningAddition && !isTriple && Cards.Count % 3 == 0`, `batchGhostCount` is forced to 0, preventing the 1→3 jump at a batch boundary. Ghost slots suppressed entirely when `combo.IsWinningLaydown`. Burn cards use `margin-left: -0.67*card-w; z-index: 2`. **Ghost slot visibility**: `.card-ghost` and `.card-ghost-triple` have `opacity: 0` by default — invisible but layout-preserving (spacing and width behavior unchanged). `_showGhostSlots = false` (default); toggling it to `true` adds `show-ghost-slots` to the game container, which sets `opacity: 1` via CSS override. Table layout and ghost geometry are identical regardless of visibility.

### Table layout (CSS grid)

Turn order is **anti-clockwise** — CPU 0 always to the player's right, then right column → top → left.

`GetSeat(int i, int count)` maps active CPU index → seat for counts 1–5:

| Active CPUs | Seat order (CPU 0 → …) |
|-------------|------------------------|
| 1 | top |
| 2 | right, left |
| 3 | right, top, left |
| 4 | right, top-right, top-left, left |
| 5 | right, top-right, top (inner), top-left, left |

For 6 and 7 active CPUs, `Game.razor` uses explicit `@if` blocks: stacks of 2 on left/right (`seat-left-stack` / `seat-right-stack`, `grid-row: 1/3`) and a pair at top (`seat-top-pair`). 7-player adds CPU 0 at `seat-bottom-side side-right` beside the player.

**`ActiveLayoutCount`** returns `RoundCpus.Count + 1` during gameplay (falls back to `Cpus.Count + 1` before the first round). The `players-N` class on the game container therefore changes dynamically as players are eliminated, applying the same layout as if the game had started with that many players.

**`expand-table`** is added when `ActiveCpus.Count` is not 1, 3, 6, or 7 (i.e. `top` grid area is empty) — sets `grid-row: 1/3` on `.seat-center` so the table fills the full center column.

**5-CPU**: top-center CPU (index 2) is rendered inside `seat-center` via `seat-top-inner`, leaving `top` empty so `expand-table` spans cleanly.

### Table overflow expansion (`Game.razor`)

`_tableExpanded` **defaults to `true`** and is reset to `true` (not `false`) by `ResetTableExpansionState()` — the table always uses full width. `table-expanded` on `.game-container` is therefore always active: CSS collapses `.game-left-spacer` (`flex: 0 0 0; overflow: hidden`) so the table grows to the left screen edge.

When the `.combos-scroll` element overflows its height, `OnAfterRenderAsync` detects it via `checkCombosOverflow` (JS) and applies one expansion level. Resets on new round/game via `ResetTableExpansionState()`. `_lastComboCount` tracks **total cards across all combos** (`Table.Combinations.Sum(c => c.Cards.Count)`) — not just combo count — so adding cards to existing combos also triggers the overflow re-check.

- **`_combosShrunk = true`**: adds `combos-shrunk` to `.game-container`. Sets `--card-step: 0.33` and overrides `.combo-row .card:not(:first-child)` to `margin-left: -0.67*card-w` — tighter card overlap reduces each combo's width, fitting more combos per row. Ghost spacing also switches to `seqStep = 0.33` so ghost positions remain aligned with real cards (see Ghost card slots above).
- **Vertical overflow fallback**: `.combos-scroll` uses `overflow-y: auto` if combos still wrap to a second row after shrinking.
