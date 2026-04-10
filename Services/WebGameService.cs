using CardGames.Models;

namespace CardGames.Services;

public enum GamePhase { Welcome, AwaitingDraw, AwaitingAction, CpuTurn, RoundOver, GameOver }

public class WebGameService
{
    private readonly AiDecisionService _ai;
    private readonly LanguageService _lang;
    private readonly GameLogger.GameLogSession _log;
    private readonly Random _rng = new();
    private readonly CombinationFinder _finder = new();

    private CpuPlayer? _pendingCpu;
    private CpuTurnSummary? _pendingCpuSummary;
    private bool _cpuStep2Done;

    private readonly List<Card> _roundDiscards   = new(); // VeryHard AI card-tracking: all round discards
    private readonly List<Card> _opponentDiscards = new(); // VeryHard AI: infers what opponents collect
    private readonly List<Card> _opponentDfdCards = new(); // VeryHard AI: cards opponents drew from discard (confirmed in combo)

    private string _firstPlayerId = "main";       // whose turn starts each round; rotates after each round
    private HashSet<string> _needFirstTurn = new(StringComparer.Ordinal); // players with first-draw privilege remaining
    private Card? _firstDrawnCard = null;          // discarding this specific card (by ref) grants second draw

    private bool _mustDrawFromDeck = false;
    private int _turnNumber = 0; // increments on each draw; reset each round
    private bool _discardDrawnOnFirstTurn = false;
    // Cards the player attempted to discard this turn — swap is locked out until next turn.
    private readonly HashSet<Card> _forfeitedSwaps = new(ReferenceEqualityComparer.Instance);
    // Combo index the current-turn joker came from (null = joker not obtained via swap this turn).
    public int? SwappedJokerComboIndex { get; private set; }

    // Duplicate-suit cards added to triples this turn; removed from triple and buried under discard at turn end
    private readonly List<(int ComboIndex, Card Card)> _tripleDiscardsPending = new();

    public IReadOnlyList<(int ComboIndex, Card Card)> TripleDiscardsPending => _tripleDiscardsPending;

    // CPU equivalent: duplicate-suit cards the active CPU added to a triple this turn.
    // Available after ExecuteCpuStep2Play(), cleared by ExecuteCpuStep3Discard().
    public IReadOnlyList<(int ComboIndex, Card Card)> CpuTripleDiscardsPending =>
        _pendingCpuSummary != null ? _pendingCpuSummary.TripleDiscardsPending : Array.Empty<(int ComboIndex, Card Card)>();

    public GameState? State { get; private set; }
    public GamePhase Phase { get; private set; } = GamePhase.Welcome;
    public WebPlayer MainPlayer { get; private set; }
    public IReadOnlyList<CpuPlayer> Cpus { get; private set; } = Array.Empty<CpuPlayer>();
    // Snapshot of active CPUs at the start of the current round — stable until next round begins.
    public IReadOnlyList<CpuPlayer> RoundCpus { get; private set; } = Array.Empty<CpuPlayer>();
    // In simulation mode the player slot is replaced by a CPU, so no +1.
    // During RoundOver we keep the normal layout so the player's section stays visible
    // until the next round starts — avoids a jarring layout shift on elimination.
    public int ActiveLayoutCount => IsSimulationMode && Phase != GamePhase.RoundOver
        ? (RoundCpus.Count > 0 ? RoundCpus.Count : Cpus.Count)
        : (RoundCpus.Count > 0 ? RoundCpus.Count + 1 : Cpus.Count + 1);
    public HashSet<Card> SelectedCards { get; } = new HashSet<Card>(ReferenceEqualityComparer.Instance);
    public string? Message { get; private set; }
    public void SetMessage(string? msg) => Message = msg;
    public string? ErrorMessage { get; private set; }
    public Dictionary<string, int> LastDeductions { get; private set; } = new();
    public record RoundRecord(int Round, Dictionary<string, int> Penalties, Dictionary<string, int> TotalsAfter, string FirstPlayerId);
    public List<RoundRecord> RoundHistory { get; private set; } = new();
    public Player? GameWinner { get; private set; }
    public int RoundNumber { get; private set; } = 1;
    public CpuTurnSummary? LastCpuTurn { get; private set; }
    public string? LastCpuTurnName { get; private set; }
    public string? LastPlayedPlayerId { get; private set; }
    public Dictionary<string, CpuTurnSummary> LastTurnByPlayer { get; private set; } = new(StringComparer.Ordinal);
    private CpuTurnSummary _mainPlayerTurnSummary = new();

    public int CpuCount { get; set; } = 1;
    public string PlayerName { get; set; } = "";

    public bool HasUnusedDiscardCard => MainPlayer.DiscardDrawnCard != null;
    public Card? DiscardDrawnCard => MainPlayer.DiscardDrawnCard;
    public bool HasSecondDrawPrivilege => Phase == GamePhase.AwaitingAction && _firstDrawnCard != null;
    public bool IsSimulationMode => MainPlayer.Score >= 100
        && Phase != GamePhase.Welcome
        && Phase != GamePhase.GameOver;

    /// <summary>
    /// True when playing <paramref name="cardCount"/> cards is a winning move —
    /// i.e. at most one card will remain in hand to discard afterwards.
    /// Single-card add: IsWinningMove(1)  →  Hand.Count &lt;= 2
    /// Multi-card add/lay-down: IsWinningMove(cards.Count)  →  Hand.Count &lt;= cards.Count + 1
    /// </summary>
    public bool IsWinningMove(int cardCount) => MainPlayer.Hand.Count <= cardCount + 1;
    // Non-null during RoundOver if the game will end when the user advances.
    public Player? PendingGameWinner => Phase == GamePhase.RoundOver ? ScoringService.GetGameWinner(AllPlayers) : null;
    public bool MustDrawFromDeck => _mustDrawFromDeck;
    public bool IsMainPlayerFirst => _firstPlayerId == MainPlayer.Id;
    public string FirstPlayerId => _firstPlayerId;
    public string FirstPlayerName => AllPlayers.FirstOrDefault(p => p.Id == _firstPlayerId)?.Name ?? "";

    public Player[] AllPlayers => new Player[] { MainPlayer }.Concat(Cpus).ToArray();
    private Player[] ActivePlayers => AllPlayers.Where(p => p.Score < 100).ToArray();

    public WebGameService(LanguageService lang, GameLogger log, AiDecisionService ai)
    {
        _lang = lang;
        _log  = log.OpenSession();
        _ai   = ai;
        MainPlayer = new WebPlayer("main", _lang.YouName);
        Cpus = Array.Empty<CpuPlayer>();
    }

    public void GoToWelcome()
    {
        Message = null;
        ErrorMessage = null;
        Phase = GamePhase.Welcome;
    }

    /// <summary>
    /// Returns the cards of a hand grouped by combo membership so that
    /// combo partners appear adjacent (useful for simulation-mode face-up display).
    /// Cards belonging to a found combination come first, grouped and sorted within
    /// the combo (sequences by ascending rank, triples by suit).
    /// Leftover cards follow, sorted by suit then rank.
    /// </summary>
    public IReadOnlyList<Card> OrderHandForDisplay(IReadOnlyList<Card> hand)
    {
        var combos = _finder.FindBestCombinationSet(hand);

        var result = new List<Card>(hand.Count);
        var used = new HashSet<Card>(ReferenceEqualityComparer.Instance);

        foreach (var combo in combos)
        {
            result.AddRange(SortComboForDisplay(combo));
            foreach (var c in combo) used.Add(c);
        }

        result.AddRange(hand
            .Where(c => !used.Contains(c))
            .OrderBy(c => c.IsJoker ? 99 : (int)c.Suit)
            .ThenBy(c => c.IsJoker ? 0 : (int)c.Rank));

        return result;
    }

    private static IEnumerable<Card> SortComboForDisplay(IReadOnlyList<Card> combo)
    {
        var nonJokers = combo.Where(c => !c.IsJoker).ToList();
        bool isTriple = nonJokers.Count > 1
            && nonJokers.Select(c => c.Rank).Distinct().Count() == 1;

        return isTriple
            ? combo.OrderBy(c => c.IsJoker ? 99 : (int)c.Suit)
            : combo.OrderBy(c => c.IsJoker ? 99 : (int)c.Rank);
    }

    public void StartNewGame()
    {
        var humanName = NormalizePlayerName(PlayerName);
        MainPlayer = new WebPlayer("main", humanName);
        Cpus = CreateCpuPlayers(humanName, CpuCount);
        RoundNumber = 1;
        _firstPlayerId = DetermineFirstPlayerForNewGame(AllPlayers);
        GameWinner = null;
        LastDeductions = new();
        RoundHistory = new();
        _log.Log("");
        _log.LogSeparator($"NEW GAME  |  Session: {_log.SessionId}  |  Players: {AllPlayers.Length}  |  Difficulty: VeryHard");
        StartNewRound();
    }

    private string NormalizePlayerName(string input)
    {
        var trimmed = input.Trim();
        return trimmed.Length > 0 ? trimmed[..Math.Min(trimmed.Length, 8)] : _lang.YouName;
    }

    private IReadOnlyList<CpuPlayer> CreateCpuPlayers(string humanName, int count)
    {
        var available = _lang.CpuNicknames
            .Where(n => !n.Equals(humanName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => _rng.Next())
            .ToArray();
        var cpus = new List<CpuPlayer>();
        for (int i = 0; i < count; i++)
            cpus.Add(new CpuPlayer(_ai, $"cpu{i + 1}", available[i]));
        return cpus;
    }

    private string DetermineFirstPlayerForNewGame(Player[] allPlayers)
    {
        // Previous game winner goes last; the player right after the winner goes first.
        // Falls back to random if no previous winner or winner's ID no longer exists (CpuCount changed).
        string? prevWinnerId = GameWinner?.Id;
        int winnerIdx = prevWinnerId != null ? Array.FindIndex(allPlayers, p => p.Id == prevWinnerId) : -1;
        return winnerIdx >= 0
            ? allPlayers[(winnerIdx + 1) % allPlayers.Length].Id
            : allPlayers[_rng.Next(allPlayers.Length)].Id;
    }

    public void StartNewRound()
    {
        SelectedCards.Clear();
        LastCpuTurn = null;
        LastCpuTurnName = null;
        LastTurnByPlayer = new Dictionary<string, CpuTurnSummary>(StringComparer.Ordinal);
        _mainPlayerTurnSummary = new CpuTurnSummary();
        _firstDrawnCard = null;
        _discardDrawnOnFirstTurn = false;
        _turnNumber = 0;
        _mustDrawFromDeck = false;
        _forfeitedSwaps.Clear();
        SwappedJokerComboIndex = null;
        _roundDiscards.Clear();
        _opponentDfdCards.Clear();
        if (_opponentDiscards.Count > 15)
            _opponentDiscards.RemoveRange(0, _opponentDiscards.Count - 15);
        // Keep up to 15 recent entries to seed cross-round pattern detection

        var activePlayers = ActivePlayers;
        // If _firstPlayerId was just eliminated, pick next active player
        if (!activePlayers.Any(p => p.Id == _firstPlayerId))
            _firstPlayerId = activePlayers[0].Id;

        RoundCpus = Cpus.Where(c => c.Score < 100).ToArray();

        // Clear hands of eliminated players so their UI shows 0 cards
        foreach (var p in AllPlayers.Where(p => p.Score >= 100))
            p.Hand.Clear();

        var rng = new Random();
        var deck = DeckService.BuildFullDeck(rng);
        DeckService.DealCards(deck, activePlayers);
        var table = new TableState();
        State = new GameState(deck, table, activePlayers);
        State.RoundNumber = RoundNumber;
        foreach (var p in activePlayers)
        {
            p.ResetTurnConstraints();
            if (p is CpuPlayer cpu) cpu.TurnsHoldingCompleteCombo = 0;
        }
        int firstIdx = Array.FindIndex(activePlayers, p => p.Id == _firstPlayerId);
        State.CurrentPlayerIndex = firstIdx;

        _needFirstTurn = new HashSet<string>(StringComparer.Ordinal) { _firstPlayerId };
        ErrorMessage = null;

        var firstPlayer = activePlayers[firstIdx];
        _log.LogSeparator($"ROUND {RoundNumber}  |  First: {firstPlayer.Name}");
        foreach (var p in activePlayers)
        {
            var tag = p is CpuPlayer ? " (CPU)" : "";
            _log.Log($"  {p.Name + tag,-14}: {GameLogger.Hand(p.Hand.Cards)}");
        }
        _log.Log($"  DISCARD   : {(State.Deck.DiscardTop != null ? GameLogger.C(State.Deck.DiscardTop) : "empty")}");

        if (firstPlayer == MainPlayer)
        {
            Phase = GamePhase.AwaitingDraw;
            Message = _lang.MsgYouFirst;
        }
        else
        {
            Phase = GamePhase.CpuTurn;
            Message = _lang.MsgCpuFirst(firstPlayer.Name);
        }
    }

    public void DrawFromDeck()
    {
        if (Phase != GamePhase.AwaitingDraw) return;
        var handBefore = MainPlayer.Hand.Cards.ToList();
        TurnService.ExecuteDraw(State!, false);
        var drawn = MainPlayer.Hand.Cards.FirstOrDefault(c => !handBefore.Contains(c));
        bool isSecondDraw = _mustDrawFromDeck;
        if (!isSecondDraw) _turnNumber++;
        string deckDrawSuffix = isSecondDraw ? " (second draw)" : "";
        _log.Log($"[T{_turnNumber}] {MainPlayer.Name} drew from deck: {(drawn != null ? GameLogger.C(drawn) : "?")}{deckDrawSuffix}");
        _mustDrawFromDeck = false;
        _forfeitedSwaps.Clear();
        SwappedJokerComboIndex = null;
        _tripleDiscardsPending.Clear();
        _mainPlayerTurnSummary = new CpuTurnSummary { DrewFromDiscard = false, DrawnCard = drawn };
        LastTurnByPlayer[MainPlayer.Id] = _mainPlayerTurnSummary;
        Phase = GamePhase.AwaitingAction;

        bool isFirstTurn = _needFirstTurn.Remove(MainPlayer.Id);
        if (isFirstTurn)
        {
            _firstDrawnCard = drawn;
            Message = _lang.MsgDrewCanSecond;
        }
        else
        {
            Message = _lang.MsgDrewNormal;
        }
        ErrorMessage = null;
    }

    public void DrawFromDiscard()
    {
        if (Phase != GamePhase.AwaitingDraw) return;
        if (_mustDrawFromDeck) { ErrorMessage = _lang.ErrMustDrawDeck; return; }
        if (State!.Deck.DiscardTop == null) { ErrorMessage = _lang.ErrDiscardEmpty; return; }

        var card = State.Deck.DiscardTop;

        // New rule: if you have 1 card in hand, you can't draw a discard card that can be
        // added to an existing table combo — that would let you win without playing your hand card.
        if (MainPlayer.Hand.Count == 1 &&
            State.Table.Combinations.Any(c => c.CanAccept(card, isWinningMove: true)))
        {
            ErrorMessage = _lang.ErrCannotDrawDiscardToWin;
            return;
        }

        TurnService.ExecuteDraw(State!, true);
        _opponentDfdCards.Add(card);
        _discardDrawnOnFirstTurn = _needFirstTurn.Contains(MainPlayer.Id);
        _needFirstTurn.Remove(MainPlayer.Id);
        _turnNumber++;
        _log.Log($"[T{_turnNumber}] {MainPlayer.Name} drew from discard: {GameLogger.C(card)}");
        _firstDrawnCard = null;
        _forfeitedSwaps.Clear();
        SwappedJokerComboIndex = null;
        _tripleDiscardsPending.Clear();
        _mainPlayerTurnSummary = new CpuTurnSummary { DrewFromDiscard = true, DrawnCard = card };
        LastTurnByPlayer[MainPlayer.Id] = _mainPlayerTurnSummary;
        Phase = GamePhase.AwaitingAction;
        SelectedCards.Clear();
        SelectedCards.Add(card);
        Message = _lang.MsgTookDiscard;
        ErrorMessage = null;
    }

    public void ReturnDiscardCard()
    {
        if (MainPlayer.DiscardDrawnCard == null || Phase != GamePhase.AwaitingAction) return;
        _log.Log($"  {MainPlayer.Name} returned {GameLogger.C(MainPlayer.DiscardDrawnCard)} to deck");
        MainPlayer.Hand.Remove(MainPlayer.DiscardDrawnCard);
        State!.Deck.AddToDiscard(MainPlayer.DiscardDrawnCard);
        _roundDiscards.Add(MainPlayer.DiscardDrawnCard);
        if (_discardDrawnOnFirstTurn) _needFirstTurn.Add(MainPlayer.Id);
        _discardDrawnOnFirstTurn = false;
        MainPlayer.DiscardDrawnCard = null;
        _mustDrawFromDeck = false;
        SelectedCards.Clear();
        Phase = GamePhase.AwaitingDraw;
        Message = _lang.MsgReturnedCard;
        ErrorMessage = null;
    }

    public void ToggleCard(Card card)
    {
        if (Phase != GamePhase.AwaitingAction) return;
        if (!SelectedCards.Add(card))
            SelectedCards.Remove(card);
        ErrorMessage = null;
    }

    public void TryLayDown()
    {
        if (Phase != GamePhase.AwaitingAction) return;
        var cards = SelectedCards.ToList();
        if (cards.Count < 3) { ErrorMessage = _lang.ErrNeed3Cards; return; }
        bool isWinningMove = IsWinningMove(cards.Count);
        var type = CombinationValidator.Classify(cards, allowJokerAtEnds: isWinningMove);

        if (type != null)
        {
            var laid = TurnService.ExecuteLayDown(State!, cards, allowJokerAtEnds: isWinningMove);
            _log.Log($"  {MainPlayer.Name} laid down: {GameLogger.Combo(laid.Cards, type.Value)}");
            if (IsSequenceWithJokerAtEnd(laid) && MainPlayer.Hand.Count > 0)
                _log.Log($"[EDGE CASE] {MainPlayer.Name} laid sequence with joker at end (non-winning): {GameLogger.Combo(laid.Cards, type.Value)}");
            _mainPlayerTurnSummary.LaidDown.Add((cards, type.Value));
            CompleteLayDown(cards);
            return;
        }

        // Single-combo classify failed — try triple lay-down with duplicate-suit burn cards.
        // e.g. Q♣ Q♦ Q♠ Q♣ Q♠ → triple Q♣Q♦Q♠ + Q♣Q♠ burned at turn end.
        var tripleWithBurns = TryClassifyTripleWithBurns(cards);
        if (tripleWithBurns.HasValue)
        {
            var (baseCards, burnCards) = tripleWithBurns.Value;
            var laid = TurnService.ExecuteLayDown(State!, baseCards, allowJokerAtEnds: false);
            int comboIndex = State!.Table.Combinations.Count - 1;
            foreach (var burn in burnCards)
            {
                TurnService.ExecuteAddToTable(State!, comboIndex, burn, isWinningMove);
                _tripleDiscardsPending.Add((comboIndex, burn));
            }
            _log.Log($"  {MainPlayer.Name} laid down: {GameLogger.Combo(laid.Cards, CombinationType.Triple)}");
            _mainPlayerTurnSummary.LaidDown.Add((baseCards, CombinationType.Triple));
            CompleteLayDown(cards);
            return;
        }

        // Try splitting selected cards into 2+ valid combinations.
        var partition = TryPartition(cards, allowJokerAtEnds: isWinningMove);
        if (partition != null)
        {
            foreach (var (comboCards, comboType) in partition)
            {
                var laid = TurnService.ExecuteLayDown(State!, comboCards, allowJokerAtEnds: isWinningMove);
                _log.Log($"  {MainPlayer.Name} laid down: {GameLogger.Combo(laid.Cards, comboType)}");
                if (IsSequenceWithJokerAtEnd(laid) && MainPlayer.Hand.Count > 0)
                    _log.Log($"[EDGE CASE] {MainPlayer.Name} laid sequence with joker at end (non-winning): {GameLogger.Combo(laid.Cards, comboType)}");
                _mainPlayerTurnSummary.LaidDown.Add((comboCards, comboType));
            }
            CompleteLayDown(cards);
            return;
        }

        ErrorMessage = _lang.ComboRejectionMsg(CombinationValidator.GetRejectionReason(cards, isWinningMove));
        _log.Log($"  [ERR] invalid combo attempt: {GameLogger.Hand(cards)}");
    }

    private void CompleteLayDown(List<Card> cards)
    {
        SelectedCards.Clear();
        _firstDrawnCard = null;
        Message = _lang.MsgLaidDown(cards.Count);
        ErrorMessage = null;
        CheckMainPlayerHandEmpty();
    }

    private static List<(List<Card>, CombinationType)>? TryPartition(List<Card> cards, bool allowJokerAtEnds)
    {
        if (cards.Count < 6) return null;
        return PartitionHelper(cards, new bool[cards.Count], allowJokerAtEnds);
    }

    private static List<(List<Card>, CombinationType)>? PartitionHelper(
        List<Card> all, bool[] used, bool allowJokerAtEnds)
    {
        var free = Enumerable.Range(0, all.Count).Where(i => !used[i]).ToList();
        if (free.Count == 0) return new();
        if (free.Count < 3) return null;

        for (int size = 3; size <= free.Count; size++)
        {
            int leftover = free.Count - size;
            if (leftover > 0 && leftover < 3) continue;
            foreach (var indices in IndexSubsets(free, size))
            {
                var subset = indices.Select(i => all[i]).ToList();
                var type = CombinationValidator.Classify(subset, allowJokerAtEnds);
                if (type == null) continue;
                foreach (var i in indices) used[i] = true;
                var rest = PartitionHelper(all, used, allowJokerAtEnds);
                if (rest != null) { rest.Insert(0, (subset, type.Value)); return rest; }
                foreach (var i in indices) used[i] = false;
            }
        }
        return null;
    }

    // Returns base cards (one per distinct suit) + burn cards (duplicate suits) when the
    // selection is a valid triple base (exactly 3 distinct suits, same rank, 4–6 total, ≤2 per suit).
    private static (List<Card> baseCards, List<Card> burnCards)? TryClassifyTripleWithBurns(List<Card> cards)
    {
        if (cards.Count < 4 || cards.Count > 6) return null;
        if (cards.Any(c => c.IsJoker)) return null;
        var rank = cards[0].Rank;
        if (cards.Any(c => c.Rank != rank)) return null;
        var bySuit = cards.GroupBy(c => c.Suit).ToDictionary(g => g.Key, g => g.ToList());
        if (bySuit.Count != 3) return null;                             // must have exactly 3 distinct suits
        if (bySuit.Values.Any(g => g.Count > 2)) return null;          // max 2 per suit
        var baseCards = bySuit.Values.Select(g => g[0]).ToList();
        var burnCards = bySuit.Values.Where(g => g.Count > 1).Select(g => g[1]).ToList();
        return (baseCards, burnCards);
    }

    private static IEnumerable<List<int>> IndexSubsets(List<int> src, int size)
    {
        if (size == 0) { yield return new List<int>(); yield break; }
        for (int i = 0; i <= src.Count - size; i++)
            foreach (var tail in IndexSubsets(src.GetRange(i + 1, src.Count - i - 1), size - 1))
            {
                var r = new List<int>(size) { src[i] };
                r.AddRange(tail);
                yield return r;
            }
    }

    public void TryAddToComboByDrop(Card card, int comboIndex)
    {
        if (Phase != GamePhase.AwaitingAction) return;
        var combo = State!.Table.Combinations[comboIndex];
        bool isWinningMove = IsWinningMove(1);

        // Try normal add first; fall back to joker swap only if add is not possible.
        // This ensures that on a winning move the player wins instead of taking a joker.
        if (combo.CanAccept(card, isWinningMove))
        {
            bool isDuplicate = combo.Type == CombinationType.Triple && !card.IsJoker
                && combo.Cards.Any(c => c.Suit == card.Suit);
            TurnService.ExecuteAddToTable(State!, comboIndex, card, isWinningMove);
            if (isDuplicate) _tripleDiscardsPending.Add((comboIndex, card));
            _log.Log($"  {MainPlayer.Name} added {GameLogger.C(card)} to {combo.Type} combo");
            _mainPlayerTurnSummary.AddedToTable.Add((card, combo.Type));
            SelectedCards.Remove(card);
            _firstDrawnCard = null;
            Message = _lang.MsgAddedToCombo;
            ErrorMessage = null;
            CheckMainPlayerHandEmpty();
            return;
        }

        // Try joker swap if the card can replace a joker and swap was not locked this turn
        if (combo.CanReplaceJoker(card) && !_forfeitedSwaps.Contains(card))
        {
            var joker = TurnService.ExecuteSwapJoker(State!, comboIndex, card);
            _log.Log($"  {MainPlayer.Name} swapped {GameLogger.C(card)} for joker in {combo.Type} combo");
            _mainPlayerTurnSummary.AddedToTable.Add((card, combo.Type));
            SelectedCards.Clear();
            SelectedCards.Add(joker);
            _firstDrawnCard = null;
            SwappedJokerComboIndex = comboIndex;
            Message = _lang.MsgSwappedJoker;
            ErrorMessage = null;
            return;
        }

        ErrorMessage = DescribeAddRejection(combo, card, isWinningMove);
    }

    public void TryAddToCombo(int comboIndex)
    {
        if (Phase != GamePhase.AwaitingAction) return;
        if (SelectedCards.Count == 0) { ErrorMessage = _lang.ErrSelect1ForCombo; return; }
        var combo = State!.Table.Combinations[comboIndex];

        if (SelectedCards.Count == 1)
        {
            if (!TryAddSingleCardToCombo(combo, comboIndex)) return;
        }
        else
        {
            if (!TryAddMultipleCardsToCombo(combo, comboIndex)) return;
        }

        _firstDrawnCard = null;
        Message = _lang.MsgAddedToCombo;
        ErrorMessage = null;
        CheckMainPlayerHandEmpty();
    }

    private bool TryAddSingleCardToCombo(Combination combo, int comboIndex)
    {
        var card = SelectedCards.First();
        bool isWinningMove = IsWinningMove(1);
        if (!combo.CanAccept(card, isWinningMove)) { ErrorMessage = DescribeAddRejection(combo, card, isWinningMove); return false; }
        bool isDuplicate = combo.Type == CombinationType.Triple && !card.IsJoker
            && combo.Cards.Any(c => c.Suit == card.Suit);
        TurnService.ExecuteAddToTable(State!, comboIndex, card, isWinningMove);
        if (isDuplicate) _tripleDiscardsPending.Add((comboIndex, card));
        _log.Log($"  {MainPlayer.Name} added {GameLogger.C(card)} to {combo.Type} combo");
        _mainPlayerTurnSummary.AddedToTable.Add((card, combo.Type));
        SelectedCards.Clear();
        return true;
    }

    private bool TryAddMultipleCardsToCombo(Combination combo, int comboIndex)
    {
        var cards = SelectedCards.ToList();
        bool isWinningMove = IsWinningMove(cards.Count);
        if (!combo.CanAcceptAll(cards, isWinningMove)) { ErrorMessage = _lang.ErrCantAddToCombo; return false; }
        if (combo.Type == CombinationType.Triple)
        {
            var suitCounts = combo.Cards.GroupBy(c => c.Suit).ToDictionary(g => g.Key, g => g.Count());
            foreach (var c in cards)
            {
                if (!c.IsJoker && suitCounts.GetValueOrDefault(c.Suit) >= 1)
                    _tripleDiscardsPending.Add((comboIndex, c));
                suitCounts[c.Suit] = suitCounts.GetValueOrDefault(c.Suit) + 1;
            }
        }
        combo.AddCards(cards, isWinningMove);
        foreach (var c in cards) { MainPlayer.Hand.Remove(c); SelectedCards.Remove(c); }
        MainPlayer.ClearConstraintsIfUsed(cards);
        _log.Log($"  {MainPlayer.Name} added {GameLogger.Hand(cards)} to {combo.Type} combo");
        foreach (var c in cards) _mainPlayerTurnSummary.AddedToTable.Add((c, combo.Type));
        return true;
    }

    public void TrySwapJoker(int comboIndex)
    {
        if (Phase != GamePhase.AwaitingAction) return;
        if (SelectedCards.Count != 1) { ErrorMessage = _lang.ErrSelect1ForCombo; return; }
        var card = SelectedCards.First();
        var combo = State!.Table.Combinations[comboIndex];
        if (!combo.CanReplaceJoker(card)) { ErrorMessage = _lang.ErrCannotSwapJoker; return; }
        if (_forfeitedSwaps.Contains(card)) { ErrorMessage = _lang.ErrSwapLockedThisTurn; return; }
        var joker = TurnService.ExecuteSwapJoker(State!, comboIndex, card);
        _log.Log($"  {MainPlayer.Name} swapped {GameLogger.C(card)} for joker in {combo.Type} combo");
        _mainPlayerTurnSummary.AddedToTable.Add((card, combo.Type));
        SelectedCards.Clear();
        SelectedCards.Add(joker);
        _firstDrawnCard = null;
        SwappedJokerComboIndex = comboIndex;
        Message = _lang.MsgSwappedJoker;
        ErrorMessage = null;
    }

    public void TryReturnJokerToCombo(int comboIndex)
    {
        if (Phase != GamePhase.AwaitingAction) return;
        if (SelectedCards.Count != 1) { ErrorMessage = _lang.ErrSelect1ForCombo; return; }
        var joker = SelectedCards.First();
        if (SwappedJokerComboIndex.HasValue && SwappedJokerComboIndex != comboIndex) return;
        if (!joker.IsJoker || !MainPlayer.Hand.Cards.Contains(joker)) return;
        var combo = State!.Table.Combinations[comboIndex];
        if (!combo.CanReturnJoker(joker)) { ErrorMessage = _lang.ErrCannotSwapJoker; return; }

        var displaced = combo.ReturnJoker(joker);
        MainPlayer.Hand.Remove(joker);
        MainPlayer.Hand.Add(displaced);
        if (ReferenceEquals(MainPlayer.SwappedJoker, joker)) MainPlayer.SwappedJoker = null;
        MainPlayer.ClearConstraintsIfUsed(new[] { displaced });
        _log.Log($"  {MainPlayer.Name} returned joker to {combo.Type} combo, got back {GameLogger.C(displaced)}");
        SelectedCards.Clear();
        SelectedCards.Add(displaced);
        _firstDrawnCard = null;
        Message = _lang.MsgSwappedJoker;
        ErrorMessage = null;
    }

    public void SelectOnly(Card card)
    {
        SelectedCards.Clear();
        SelectedCards.Add(card);
    }

    public void ReorderHand(int fromIndex, int toIndex)
    {
        MainPlayer.Hand.MoveCard(fromIndex, toIndex);
    }

    public void TryDiscard()
    {
        if (Phase != GamePhase.AwaitingAction) return;
        if (SelectedCards.Count != 1) { ErrorMessage = _lang.ErrSelect1Discard; return; }

        if (MainPlayer.DiscardDrawnCard != null)
        {
            ErrorMessage = _lang.ErrMustUseDiscard;
            return;
        }

        if (MainPlayer.SwappedJoker != null && MainPlayer.Hand.Cards.Contains(MainPlayer.SwappedJoker))
        {
            ErrorMessage = _lang.ErrMustUseSwappedJoker;
            return;
        }

        var card = SelectedCards.First();

        if (card.IsJoker) { ErrorMessage = _lang.ErrCannotDiscardJoker; return; }

        // Skip the swap obligation if: (a) this is the winning discard (last card — forcing a swap
        // would leave the player holding an undiscardable joker), or (b) the swap was already forfeited
        // this turn (player tried once, was blocked; second attempt must be allowed per the rules).
        bool isWinningDiscard = MainPlayer.Hand.Count == 1;
        bool swapForfeited = _forfeitedSwaps.Contains(card);
        if (!isWinningDiscard && !swapForfeited
            && State!.Table.Combinations.Any(c => c.CanReplaceJoker(card)))
        {
            _forfeitedSwaps.Add(card); // swap locked out for the rest of this turn
            ErrorMessage = _lang.ErrMustSwapJoker;
            return;
        }

        if (_firstDrawnCard != null && ReferenceEquals(card, _firstDrawnCard))
        {
            TurnService.ExecuteDiscard(State!, card, swapForfeited);
            _roundDiscards.Add(card);
            _opponentDiscards.Add(card);
            _log.Log($"  {MainPlayer.Name} discarded first card: {GameLogger.C(card)}  (drawing second)");
            SelectedCards.Clear();
            _firstDrawnCard = null;
            _mustDrawFromDeck = true;
            Phase = GamePhase.AwaitingDraw;
            Message = _lang.MsgFirstDiscarded;
            ErrorMessage = null;
            return;
        }

        _firstDrawnCard = null;
        FlushTripleDiscards();
        TurnService.ExecuteDiscard(State!, card, swapForfeited);
        _roundDiscards.Add(card);
        _opponentDiscards.Add(card);
        _log.Log($"  {MainPlayer.Name} discarded: {GameLogger.C(card)}  (h:{MainPlayer.Hand.Count})");
        _mainPlayerTurnSummary.Discarded = card;
        LastTurnByPlayer[MainPlayer.Id] = _mainPlayerTurnSummary;
        LastPlayedPlayerId = MainPlayer.Id;
        SelectedCards.Clear();
        ErrorMessage = null;

        if (State!.RoundOver)
        {
            HandleRoundOver();
        }
        else
        {
            State!.FlipCurrentPlayer();
            AdvanceToNextPlayerPhase();
        }
    }

    public void ExecuteCpuTurn()
    {
        ExecuteCpuStep1Draw();
        ExecuteCpuStep2Play();
        ExecuteCpuStep3Discard();
    }

    // Fast-forward all remaining CPU turns and rounds to completion without any UI delays.
    // Handles mid-turn state: if Step1 was already called (_pendingCpu set), resumes from Step2.
    // Safe to call whenever Phase is CpuTurn or RoundOver.
    public void RunToGameOver()
    {
        const int maxIterations = 10_000;
        for (int i = 0; i < maxIterations; i++)
        {
            if (Phase == GamePhase.CpuTurn)
            {
                if (_pendingCpu == null) ExecuteCpuStep1Draw();
                if (!_cpuStep2Done) ExecuteCpuStep2Play();
                ExecuteCpuStep3Discard();
            }
            else if (Phase == GamePhase.RoundOver)
            {
                AdvanceRound();
            }
            else
            {
                break;
            }
        }
    }

    public void ExecuteCpuStep1Draw()
    {
        if (Phase != GamePhase.CpuTurn) return;
        var cpu = State!.CurrentPlayer as CpuPlayer;
        if (cpu == null) return;
        _pendingCpu = cpu;
        bool firstTurn = _needFirstTurn.Remove(cpu.Id);
        _pendingCpuSummary = _ai.ExecuteCpuDrawStep(State!, cpu, firstTurn);
        if (_pendingCpuSummary.DrewFromDiscard && _pendingCpuSummary.DrawnCard != null)
            _opponentDfdCards.Add(_pendingCpuSummary.DrawnCard);
        LastCpuTurn = _pendingCpuSummary;
        LastCpuTurnName = cpu.Name;
        LastTurnByPlayer[cpu.Id] = _pendingCpuSummary;
        LastPlayedPlayerId = cpu.Id;
        _turnNumber++;
        bool didSecondDraw = _pendingCpuSummary.FirstDiscarded != null;
        string source = _pendingCpuSummary.DrewFromDiscard ? "discard" : "deck";
        string firstLogCard = didSecondDraw
            ? GameLogger.C(_pendingCpuSummary.FirstDiscarded!)
            : (_pendingCpuSummary.DrawnCard != null ? GameLogger.C(_pendingCpuSummary.DrawnCard) : "?");
        _log.Log($"[T{_turnNumber}] {cpu.Name} drew from {source}: {firstLogCard}");
        if (didSecondDraw)
        {
            _log.Log($"  {cpu.Name} discarded first card: {GameLogger.C(_pendingCpuSummary.FirstDiscarded!)}  (drawing second)");
            string secondCard = _pendingCpuSummary.DrawnCard != null ? GameLogger.C(_pendingCpuSummary.DrawnCard) : "?";
            _log.Log($"[T{_turnNumber}] {cpu.Name} drew from deck: {secondCard}  (second draw)");
        }
    }

    public void ExecuteCpuStep2Play()
    {
        if (_pendingCpu == null || _pendingCpuSummary == null) return;
        _cpuStep2Done = true;
        _ai.ExecuteCpuPlayStep(State!, _pendingCpu, _pendingCpuSummary, MinOpponentHandCount(_pendingCpu));
        foreach (var (cards, type) in _pendingCpuSummary.LaidDown)
        {
            _log.Log($"{_pendingCpu.Name} laid down: {GameLogger.Combo(cards, type)}");
            if (type == CombinationType.Sequence && cards.Count > 0 && (cards[0].IsJoker || cards[^1].IsJoker) && _pendingCpu.Hand.Count > 0)
                _log.Log($"[EDGE CASE] {_pendingCpu.Name} laid sequence with joker at end (non-winning): {GameLogger.Combo(cards, type)}");
        }
        foreach (var card in _pendingCpuSummary.SwappedJokers)
            _log.Log($"{_pendingCpu.Name} swapped {GameLogger.C(card)} for joker in Sequence combo");
        foreach (var (card, comboType) in _pendingCpuSummary.AddedToTable)
            if (!_pendingCpuSummary.SwappedJokers.Contains(card))
                _log.Log($"{_pendingCpu.Name} added {GameLogger.C(card)} to {comboType} combo");

        if (_pendingCpu.Hand.Count == 0)
        {
            State!.RoundOver = true;
            State.RoundWinnerId = _pendingCpu.Id;
            _log.Log($"  {_pendingCpu.Name} played all cards — round won");
            LastTurnByPlayer[_pendingCpu.Id] = _pendingCpuSummary;
            LastPlayedPlayerId = _pendingCpu.Id;
            _pendingCpu = null;
            _pendingCpuSummary = null;
            _cpuStep2Done = false;
            HandleRoundOver();
        }
    }

    public void ExecuteCpuStep3Discard()
    {
        if (Phase == GamePhase.RoundOver || Phase == GamePhase.GameOver)
        {
            _pendingCpu = null;
            _pendingCpuSummary = null;
            _cpuStep2Done = false;
            return;
        }
        if (_pendingCpu == null || _pendingCpuSummary == null) return;
        foreach (var (ci, card) in _pendingCpuSummary.TripleDiscardsPending)
            if (ci < State!.Table.Combinations.Count)
            {
                State.Table.Combinations[ci].RemoveCard(card);
                State.Deck.AddToDiscard(card);
                _roundDiscards.Add(card);
            }
        if (_pendingCpuSummary.FirstDiscarded != null)
            _roundDiscards.Add(_pendingCpuSummary.FirstDiscarded);

        var seenPublic = BuildSeenPublicCards();
        bool hasUnusedSwapJoker   = _pendingCpu.SwappedJoker != null;
        bool hasUnusedDiscardCard = _pendingCpu.DiscardDrawnCard != null;
        bool noLegalDiscard = HasNoLegalDiscard(_pendingCpu);
        if (noLegalDiscard && !hasUnusedSwapJoker && !hasUnusedDiscardCard)
        {
            var handDesc = string.Join("  ", _pendingCpu.Hand.Cards.Select(c => GameLogger.C(c)));
            _log.Log($"[EDGE CASE] {_pendingCpu.Name} has no legal discard — hand: {handDesc}");
        }
        else if (hasUnusedDiscardCard)
        {
            var dfdCard = _pendingCpu.DiscardDrawnCard!;
            _log.Log($"[EDGE CASE] {_pendingCpu.Name} drew from discard but could not use {GameLogger.C(dfdCard)} — returning to discard pile and drawing from deck");
            _pendingCpu.Hand.Remove(dfdCard);
            State!.Deck.AddToDiscard(dfdCard);
            _pendingCpu.DiscardDrawnCard = null;
            var replacement = State.Deck.Draw();
            _pendingCpu.Hand.Add(replacement);
            hasUnusedDiscardCard = false;
            _ai.ExecuteCpuDiscardStep(State, _pendingCpu, _pendingCpuSummary, seenPublic, _opponentDiscards, MinOpponentHandCount(_pendingCpu), _opponentDfdCards);
            if (_pendingCpuSummary.Discarded != null)
            {
                _roundDiscards.Add(_pendingCpuSummary.Discarded);
                _opponentDiscards.Add(_pendingCpuSummary.Discarded);
            }
        }
        else if (hasUnusedSwapJoker || noLegalDiscard)
        {
            _log.Log($"{_pendingCpu.Name} skipping discard (constraint or no legal card)");
        }
        else
        {
            _ai.ExecuteCpuDiscardStep(State!, _pendingCpu, _pendingCpuSummary, seenPublic, _opponentDiscards, MinOpponentHandCount(_pendingCpu), _opponentDfdCards);
            if (_pendingCpuSummary.Discarded != null)
            {
                _roundDiscards.Add(_pendingCpuSummary.Discarded);
                _opponentDiscards.Add(_pendingCpuSummary.Discarded);
            }
        }
        if (_pendingCpuSummary.Discarded != null)
            _log.Log($"  {_pendingCpu.Name} discarded: {GameLogger.C(_pendingCpuSummary.Discarded)}  (h:{_pendingCpu.Hand.Count})");
        LastTurnByPlayer[_pendingCpu.Id] = _pendingCpuSummary;
        LastPlayedPlayerId = _pendingCpu.Id;
        // Reset per-turn constraints so they never carry over to the next turn.
        // (The player's constraints are always resolved within a single turn by the UI guards;
        //  for CPU, AI edge-cases can leave them set — clearing here prevents an infinite block.)
        _pendingCpu.ResetTurnConstraints();
        _pendingCpu = null;
        _pendingCpuSummary = null;
        _cpuStep2Done = false;

        if (State!.RoundOver)
            HandleRoundOver();
        else
        {
            State!.FlipCurrentPlayer();
            AdvanceToNextPlayerPhase();
        }
    }

    public void AdvanceRound()
    {
        if (Phase != GamePhase.RoundOver) return;
        var allPlayers = AllPlayers;
        var winner = ScoringService.GetGameWinner(allPlayers);
        if (winner != null)
        {
            GameWinner = winner;
            Phase = GamePhase.GameOver;
            Message = null;
        }
        else
        {
            _firstPlayerId = FindNextActivePlayerId(_firstPlayerId, allPlayers);
            RoundNumber++;
            StartNewRound();
        }
    }

    // Finds the next active player in AllPlayers order, wrapping around from currentFirstId.
    // Uses AllPlayers (not just active) so that eliminated players don't cause position skips.
    private string FindNextActivePlayerId(string currentFirstId, Player[] allPlayers)
    {
        var activeIds = ActivePlayers.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        int curIdx = Array.FindIndex(allPlayers, p => p.Id == currentFirstId);
        for (int offset = 1; offset <= allPlayers.Length; offset++)
        {
            string candidateId = allPlayers[(curIdx + offset) % allPlayers.Length].Id;
            if (activeIds.Contains(candidateId)) return candidateId;
        }
        return currentFirstId; // fallback: should never occur when active players exist
    }

    // Minimum hand count among all active players except the given CPU (used for urgency trigger).
    private int MinOpponentHandCount(Player? excludeCpu) =>
        State!.Players
            .Where(p => excludeCpu == null || !ReferenceEquals(p, excludeCpu))
            .Select(p => p.Hand.Count)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

    // Sets Phase/Message after the current player has finished and FlipCurrentPlayer() was called.
    private void AdvanceToNextPlayerPhase()
    {
        if (State!.CurrentPlayer == MainPlayer)
        {
            Phase = GamePhase.AwaitingDraw;
            Message = _lang.MsgYourTurn;
        }
        else
        {
            Phase = GamePhase.CpuTurn;
        }
    }

    private string DescribeAddRejection(Combination combo, Card card, bool isWinningMove)
    {
        if (combo.Type == CombinationType.Triple)
        {
            if (card.IsJoker) return _lang.ErrJokerInTriple;
            var tripleRank = combo.Cards.First(c => !c.IsJoker).Rank;
            if (card.Rank != tripleRank) return _lang.ErrWrongRankTriple;
            if (combo.Cards.Count >= Combination.MaxTripleSize) return _lang.ErrTripleFull;
            if (combo.Cards.Count(c => c.Suit == card.Suit) >= 2) return _lang.ErrDuplicateSuit;
            return _lang.ErrExcludedSuit;
        }
        else
        {
            var nonJokers = combo.Cards.Where(c => !c.IsJoker).ToList();
            if (!card.IsJoker && nonJokers.Count > 0 && card.Suit != nonJokers[0].Suit)
                return _lang.ErrWrongSuitSequence;
            if (card.IsJoker)
            {
                var testRanks = combo.Cards.Concat(new[] { card })
                    .Where(c => !c.IsJoker).Select(c => (int)c.Rank).OrderBy(r => r).ToList();
                for (int i = 0; i + 1 < testRanks.Count; i++)
                    if (testRanks[i + 1] - testRanks[i] - 1 >= 2) return _lang.ErrAdjacentJokers;
                return _lang.ErrJokerNoGap;
            }
            return _lang.ErrCardNotAdjacent;
        }
    }

    private void CheckMainPlayerHandEmpty()
    {
        if (Phase != GamePhase.AwaitingAction || MainPlayer.Hand.Count != 0) return;
        _log.Log($"  {MainPlayer.Name} played all cards — round won");
        LastTurnByPlayer[MainPlayer.Id] = _mainPlayerTurnSummary;
        LastPlayedPlayerId = MainPlayer.Id;
        State!.RoundOver = true;
        State.RoundWinnerId = MainPlayer.Id;
        HandleRoundOver();
    }

    private void FlushTripleDiscards()
    {
        foreach (var (ci, card) in _tripleDiscardsPending)
            if (ci < State!.Table.Combinations.Count)
            {
                State.Table.Combinations[ci].RemoveCard(card);
                State.Deck.AddToDiscard(card);
                _roundDiscards.Add(card);
            }
        _tripleDiscardsPending.Clear();
    }

    private List<Card> BuildSeenPublicCards() =>
        _roundDiscards.Concat(State!.Table.Combinations.SelectMany(c => c.Cards)).ToList();

    private static bool IsSequenceWithJokerAtEnd(Combination combo) =>
        combo.Type == CombinationType.Sequence && combo.Cards.Count > 0 &&
        (combo.Cards[0].IsJoker || combo.Cards[^1].IsJoker);

    private bool HasNoLegalDiscard(Player cpu)
    {
        // When exactly 1 card remains, TurnService.ExecuteDiscard bypasses the swap obligation
        // (its guard is player.Hand.Count > 1). The only hard block at that point is a joker.
        if (cpu.Hand.Count == 1)
            return cpu.Hand.Cards.First().IsJoker;
        return !cpu.Hand.Cards.Any(c => !c.IsJoker && !State!.Table.Combinations.Any(combo => combo.CanReplaceJoker(c)));
    }

    private void HandleRoundOver()
    {
        var deductions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var player in State!.Players)
            deductions[player.Id] = player.Id == State.RoundWinnerId ? 0 : player.Hand.TotalPoints;
        LastDeductions = deductions;

        var roundWinner = State!.Players.FirstOrDefault(p => p.Id == State.RoundWinnerId);
        _log.Log($"ROUND {RoundNumber} OVER  |  Winner: {roundWinner?.Name ?? "none"}");
        foreach (var p in State.Players)
            _log.Log($"  {p.Name}: hand={GameLogger.Hand(p.Hand.Cards)}  penalty=+{deductions[p.Id]}");

        ScoringService.ApplyRoundScores(State!);
        _log.Log("  Scores: " + string.Join("  ", AllPlayers.Select(p => $"{p.Name} {p.Score}/100")));
        var totals = AllPlayers.ToDictionary(p => p.Id, p => p.Score, StringComparer.Ordinal);
        RoundHistory.Add(new RoundRecord(RoundNumber, deductions, totals, _firstPlayerId));

        var nextActivePlayers = AllPlayers.Where(p => p.Score < 100).ToArray();
        bool isLastRound = nextActivePlayers.Length <= 1;
        string nextFirst = isLastRound
            ? ""
            : AllPlayers.First(p => p.Id == FindNextActivePlayerId(_firstPlayerId, AllPlayers)).Name;

        var gameWinner = isLastRound ? ScoringService.GetGameWinner(AllPlayers) : null;

        if (gameWinner != null)
        {
            _log.Log("");
            _log.LogSeparator("GAME OVER  |  Winner: " + gameWinner.Name + "  |  " +
                string.Join("  ", AllPlayers.Select(p => $"{p.Name} {p.Score}/100")));
        }

        Phase = GamePhase.RoundOver;
        Message = gameWinner != null
            ? null  // game-end toast in Game.razor handles this
            : isLastRound
                ? _lang.MsgRoundOverFinal
                : roundWinner != null
                    ? _lang.MsgRoundWinner(roundWinner.Name, nextFirst)
                    : _lang.MsgRoundOver(nextFirst);
        ErrorMessage = null;
    }
}
