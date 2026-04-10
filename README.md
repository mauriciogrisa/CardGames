# Card Games

A browser-based implementation of **Pontinho** (Brazilian Rummy variant) for 1 human player against 1–7 CPU opponents (2–8 players total). Built with Blazor Server on .NET 10. All game logic runs server-side; the browser updates via SignalR.

## Features

- 1 vs 1–7 CPU opponents
- CPU difficulty: VeryHard (the only active difficulty — lower levels were removed)
- Bilingual UI: English and Brazilian Portuguese (auto-detected from browser)
- Full drag-and-drop support for hand reordering, playing to the table, and discarding
- CPU "thinking" animation with combo reveal sequence
- Last-card warning toast with sound
- Second-draw privilege indicator on the discard pile
- Score history panel with round-by-round breakdown
- Persistent settings (player name, difficulty, CPU count) via `localStorage`

## Game Rules

- **Deck**: 2 full decks + 4 Jokers = 108 cards; 9 cards dealt per active player
- **Valid combinations**:
  - **Triple**: 3–6 cards of the same rank, all different suits
  - **Sequence**: 3+ cards of the same suit in ascending order
- **Jokers** act as wildcards in sequences, but cannot be at either end — except on the winning move
- **Joker swap**: a player may replace a joker on the table with the card it represents; the joker enters the hand
- **First-turn second draw**: the player who goes first may draw from the deck, discard that exact card, and draw again
- **Scoring**: cards remaining in hand count as penalty points (Joker/Ace = 15, 10/J/Q/K = 10, others = face value). Reaching 100 points eliminates a player
- **Winning a round**: play all cards (lay down or add to combos) — no discard needed
- **Winning the game**: last player standing after all others are eliminated
- **New game first player**: the winner of the previous game goes **last** in the next game's first round (the player immediately after the winner in turn order goes first)

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Chromium-based browser (for E2E tests: Playwright installs Chromium automatically)

## Running

```bash
# Run the app (auto-opens browser at http://localhost:5000)
dotnet run --project CardGames

# Build only
dotnet build CardGames

# If the build fails with a file-lock error, kill the running process first:
taskkill /IM CardGames.exe /F     # Windows
```

## Testing

The `CardGames.Tests/` project contains end-to-end tests using **NUnit + Playwright (Chromium)**. The app must be running before executing tests.

```bash
# Start the app first (in a separate terminal)
dotnet run --project CardGames --urls http://localhost:5000

# Run all 30 E2E tests
dotnet test CardGames.Tests

# Run a specific test
dotnet test CardGames.Tests --filter "TestName"
```

Tests cover: hand drag-and-drop reorder, phase transitions, draw mechanics, joker swap/return, discard rules, multi-combo lay-down, CPU turns, and game-over flow.

## Project Structure

```
CardGames/
├── App.razor                  # HTML shell (links app.css, app.js)
├── Program.cs                 # Service registration, middleware
├── Components/Pages/
│   └── Game.razor             # Single-page UI + all Blazor event handlers
├── Models/
│   ├── Card.cs                # Card class (reference equality — two decks have duplicate cards)
│   ├── Combination.cs         # A combo on the table; enforces CanAccept/CanReplaceJoker rules
│   ├── Deck.cs                # Draw pile + discard pile
│   ├── GameState.cs           # Snapshot of the current round state
│   ├── Hand.cs                # Ordered list of cards + TotalPoints
│   ├── Player.cs              # Base player (constraints: SwappedJoker, DiscardDrawnCard)
│   ├── WebPlayer.cs           # Human player (extends Player)
│   └── CpuPlayer.cs           # CPU player (extends Player, holds difficulty ref)
└── Services/
    ├── WebGameService.cs      # Scoped. Central state machine + all human action methods
    ├── AiDecisionService.cs   # CPU decision logic (draw, play, discard) per difficulty
    ├── CombinationValidator.cs# Pure static validation: IsValidTriple, IsValidSequence, GetRejectionReason
    ├── CombinationFinder.cs   # FindBestCombinationSet, TryPartitionAll (exhaustive backtracking)
    ├── TurnService.cs         # Atomic mutations: draw, lay down, add to table, discard
    ├── ScoringService.cs      # ApplyRoundScores, GetGameWinner, Ace mercy rule
    ├── DeckService.cs         # Deck creation and shuffling
    ├── LanguageService.cs     # Scoped. All UI strings in EN and PT-BR
    └── GameLogger.cs          # Singleton. Writes timestamped events to bin/Debug/logs/game_log.txt

CardGames.Tests/
├── HandDragTests.cs                   # E2E: drag-and-drop reorder and drop-zone tests
├── BurnAnimationTests.cs              # E2E: triple duplicate-suit card burn animation
├── DiscardPileTests.cs                # E2E: discard pile interaction tests
├── Phase5VerificationTests.cs         # E2E: full game-flow integration tests
├── AiDecisionServiceTests.cs          # Integration: CPU decision logic
├── AiDecisionServiceInternalTests.cs  # Unit: internal CPU methods
├── WebGameServiceTests.cs             # Integration: game state machine
├── CombinationFinderTests.cs          # Unit: combo finder / partitioner
├── CombinationTests.cs                # Unit: Combination model
├── CombinationValidatorTests.cs       # Unit: validator rules
├── ModelTests.cs                      # Unit: Card, Hand, Deck models
└── TurnServiceTests.cs                # Unit: atomic turn mutations

wwwroot/
├── app.css                    # All styling (CSS grid layout, card rendering, animations)
└── app.js                     # Drag-and-drop JS, hand-reorder DotNet bridge, sound, localStorage
```

## Architecture Notes

- **Blazor Server**: all game state lives in a scoped `WebGameService` on the server. The UI is a single Razor component (`Game.razor`) that calls service methods and calls `StateHasChanged()` to re-render.
- **`Card` uses reference equality** — two decks produce physically distinct cards of the same rank/suit. `SelectedCards` (a `HashSet`) always uses `ReferenceEqualityComparer.Instance`.
- **Hand drag-and-drop** bypasses Blazor's `@ondrop` event delegation (which does not fire for synthetic events). Instead, `app.js` registers a capture-phase `document.addEventListener('drop')` that calls `HandleHandCardDrop` via `invokeMethodAsync` directly on the `Game` component's `DotNetObjectReference`.
- **`allowJokerAtEnds` and `isWinningMove`** must be threaded through every layer: `WebGameService` → `CombinationValidator` → `TurnService` → `Combination.AddCard`. Missing either flag silently rejects valid winning moves.
- **CPU turns are async in the UI**: `RunCpuTurns()` loops while `Phase == CpuTurn`, inserting `StateHasChanged()` + delay between iterations for the "thinking" animation. The service layer is synchronous.
- **Game log**: written to `bin/Debug/net10.0/logs/game_log.txt`. `[EDGE CASE]` entries flag unusual CPU situations for debugging.

## CPU Behaviour

All games use a single fixed difficulty (VeryHard). The `Difficulty` enum and all lower-difficulty branches have been removed.

| Dimension | Behaviour |
|---|---|
| Draw from discard | If the card forms a new hand combo or extends an existing table combo |
| Second-draw (first-to-act) | Redraw if drawn card is unprotected (no near-combo partner) and is a high-tier isolated card |
| Add to table | Yes — after lay-down, greedily adds cards to existing combos |
| Discard strategy | Near-combo protected + anti-feed (threshold ±1 of discard top) + opponent-discard pattern filter |

## Static Asset Versioning

After modifying `app.js` or `app.css`, increment the query-string version in `App.razor` to bust the browser cache:

```html
<link rel="stylesheet" href="app.css?v=123" />
<script src="app.js?v=10"></script>
```
