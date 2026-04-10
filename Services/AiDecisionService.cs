using CardGames.Models;

namespace CardGames.Services;

public class AiDecisionService
{
    private const int UrgencyOpponentHandThreshold   = 4;
    private const int UrgencyScoreThreshold          = 85;
    private const int EliminationScore               = 100;
    private const int JokerPointValue                = 15;
    private const int MaxWinByAddingSearchSize        = 10;
    private const int HighTierMinPoints              = 10;
    private const int HighTierMinPartnersToProtect   = 2;

    private readonly CombinationFinder _finder = new();
    private readonly Random _rng = new();

    public CpuTurnSummary ExecuteCpuTurn(GameState state, Player cpu, bool canDrawSecond = false, int minOpponentHandCount = int.MaxValue)
    {
        var summary = ExecuteCpuDrawStep(state, cpu, canDrawSecond, minOpponentHandCount);
        ExecuteCpuPlayStep(state, cpu, summary, minOpponentHandCount);
        if (cpu.Hand.Count > 0)
            ExecuteCpuDiscardStep(state, cpu, summary, minOpponentHandCount: minOpponentHandCount);
        return summary;
    }

    public CpuTurnSummary ExecuteCpuDrawStep(GameState state, Player cpu, bool canDrawSecond, int minOpponentHandCount = int.MaxValue)
    {
        var summary = new CpuTurnSummary();
        var handBefore = cpu.Hand.Cards.ToList();
        bool takeDiscard = DecideDrawSourceHard(state, cpu);
        TurnService.ExecuteDraw(state, takeDiscard);
        summary.DrewFromDiscard = takeDiscard;
        summary.DrawnCard = cpu.Hand.Cards.FirstOrDefault(c => !handBefore.Contains(c));

        if (canDrawSecond && !takeDiscard)
        {
            var firstCard = cpu.Hand.Cards[^1];
            bool useSecondDraw = !IsFirstDrawCardProtected(firstCard, cpu.Hand.Cards);
            // A high-tier card (J/Q/K/A) requires strong sequence support to justify
            // keeping it: either a joker in hand (can complete any 3-card sequence) or
            // a full 3-card sequence fragment already present. A single tight-seq partner
            // is insufficient — without a 3rd card, the 10-pt liability accumulates every
            // lost round while waiting for one specific draw.
            // A joker in hand doesn't justify keeping an isolated high-tier card —
            // HasThreeCardSeqFragment already protects cards in a real 3-card fragment.
            if (!useSecondDraw && !firstCard.IsJoker
                && firstCard.Rank is Rank.Jack or Rank.Queen or Rank.King or Rank.Ace
                && !HasThreeCardSeqFragment(firstCard, cpu.Hand.Cards))
                useSecondDraw = true;
            if (useSecondDraw)
                useSecondDraw = !firstCard.IsJoker &&
                    !state.Table.Combinations.Any(c => c.CanReplaceJoker(firstCard));
            if (useSecondDraw)
            {
                TurnService.ExecuteDiscard(state, firstCard);
                summary.FirstDiscarded = firstCard;
                var handBefore2 = cpu.Hand.Cards.ToList();
                TurnService.ExecuteDraw(state, false);
                summary.DrawnCard = cpu.Hand.Cards.FirstOrDefault(c => !handBefore2.Contains(c));
            }
        }
        return summary;
    }

    public void ExecuteCpuPlayStep(GameState state, Player cpu, CpuTurnSummary summary, int minOpponentHandCount = int.MaxValue)
    {
        // Try joker swaps first so the obtained joker can be used in lay-down this turn.
        TryJokerSwaps(state, cpu, summary);

        var (combosToLay, isWinningLay) = DetermineLayDownPlan(cpu);

        // If a partial lay-down still leaves the player facing elimination AND
        // none of the remaining cards connect to any combo on the table (new or existing),
        // skip it — nothing to gain. But if remaining cards could extend a laid combo in a
        // future turn, still lay down (e.g. 4♠5♠6♠ laid, 2♠8♠ remaining can extend later).
        // First tries a smarter path: add cards to the table, then lay down the rest.
        // Exception: never skip when the player drew from the discard pile — they are committed
        // to using that card and holding back only inflates their penalty if they lose.
        bool skipLayDown = false;
        if (!isWinningLay && cpu.DiscardDrawnCard == null)
        {
            if (IsFacingElimination(cpu, combosToLay))
            {
                if (TryWinByAddingFirst(state, cpu, summary))
                    return; // winning path executed — hand empty, round over

                var laidSet = combosToLay.SelectMany(c => c).ToHashSet(ReferenceEqualityComparer.Instance);
                var remaining = cpu.Hand.Cards.Where(c => !laidSet.Contains(c)).ToList();
                if (!RemainingCardsConnectToCombos(remaining, combosToLay, state.Table.Combinations))
                    skipLayDown = true;
            }
        }

        if (skipLayDown && IsUrgent(minOpponentHandCount, cpu))
            skipLayDown = false;

        // A swapped joker creates a discard obligation: the joker cannot be discarded and the
        // player cannot discard anything else until the joker is used in a combo. If the planned
        // lay-down uses the joker, skipping it would leave the player with no legal discard at all.
        if (skipLayDown && cpu.SwappedJoker != null
            && combosToLay.Any(c => c.Any(x => ReferenceEquals(x, cpu.SwappedJoker))))
            skipLayDown = false;

        // Never suppress a multi-combo burst — having 2+ complete combos ready is a strong
        // position; laying them simultaneously removes more cards than a single combo and
        // denies opponents the extra turns they would get if the CPU held back.
        if (skipLayDown && combosToLay.Count >= 2)
            skipLayDown = false;

        // Lay-down suppression cap: if the CPU has been sitting on a complete combo for
        // 2+ consecutive turns, force the lay-down regardless of remainder quality.
        // Waiting indefinitely inflates the penalty when the round ends unexpectedly.
        if (skipLayDown && combosToLay.Count > 0 && cpu is CpuPlayer cpuWithCounter
            && cpuWithCounter.TurnsHoldingCompleteCombo >= 2)
        {
            skipLayDown = false;
            cpuWithCounter.TurnsHoldingCompleteCombo = 0;
        }

        if (!skipLayDown)
        {
            // When the CPU drew from the discard pile, lay that card's combo first so it
            // occupies the lowest table index (first row) — matches the animation priority
            // that highlights the drawn card arriving in its combo before the others.
            if (cpu.DiscardDrawnCard != null && combosToLay.Count > 1)
            {
                var dfCard = cpu.DiscardDrawnCard;
                int dfIdx = combosToLay.FindIndex(c => c.Any(card => ReferenceEquals(card, dfCard)));
                if (dfIdx > 0)
                {
                    var dfCombo = combosToLay[dfIdx];
                    combosToLay.RemoveAt(dfIdx);
                    combosToLay.Insert(0, dfCombo);
                }
            }

            foreach (var combo in combosToLay)
            {
                var available = combo.Where(c => cpu.Hand.Cards.Contains(c)).ToList();
                if (available.Count == combo.Count)
                {
                    var type = CombinationValidator.Classify(available, allowJokerAtEnds: isWinningLay);
                    if (type != null)
                    {
                        var laid = TurnService.ExecuteLayDown(state, available, allowJokerAtEnds: isWinningLay);
                        summary.LaidDown.Add((laid.Cards.ToList(), type.Value));
                    }
                }
            }
        }

        // Track how many consecutive turns this CPU held a complete combo without laying down.
        if (cpu is CpuPlayer cpuPlayer)
        {
            if (summary.LaidDown.Count > 0)
                cpuPlayer.TurnsHoldingCompleteCombo = 0;
            else if (skipLayDown && combosToLay.Count > 0)
                cpuPlayer.TurnsHoldingCompleteCombo++;
        }

        bool urgent = IsUrgent(minOpponentHandCount, cpu);
        DrainSingleCardAdds(state, cpu, summary, urgent);
        if (TryWinByMultiCardTableAdd(state, cpu, summary))
            DrainSingleCardAdds(state, cpu, summary, urgent);
    }

    private void DrainSingleCardAdds(GameState state, Player cpu, CpuTurnSummary summary, bool urgent)
    {
        bool addedCard = true;
        while (addedCard)
        {
            addedCard = false;
            var cardsToAdd = DecideCardsToAddToTable(state, cpu, urgent);
            foreach (var (comboIdx, card) in cardsToAdd)
            {
                if (!cpu.Hand.Cards.Contains(card)) continue;

                bool wouldWin = cpu.Hand.Count <= 2;
                if (!wouldWin)
                {
                    var handAfter = cpu.Hand.Cards.Where(c => !ReferenceEquals(c, card));
                    if (!HasLegalDiscard(state, handAfter)) break;
                }

                var combo = state.Table.Combinations[comboIdx];
                var comboType = combo.Type;
                bool isDuplicate = comboType == CombinationType.Triple && !card.IsJoker
                    && combo.Cards.Any(c => c.Suit == card.Suit);
                bool isPositionalSwap = combo.CanReplaceJoker(card);
                TurnService.ExecuteAddToTable(state, comboIdx, card, wouldWin);
                summary.AddedToTable.Add((card, comboType));
                if (isPositionalSwap) summary.PositionalSwapCount++;
                if (isDuplicate)
                    summary.TripleDiscardsPending.Add((comboIdx, card));
                addedCard = true;
                break;
            }
        }
    }

    // Tries to win by adding 2+ hand cards atomically to a single table sequence.
    // Handles cases where individual CanAccept would fail (e.g. gap filled by joker)
    // but the combined final state is valid via CanAcceptAll.
    private bool TryWinByMultiCardTableAdd(GameState state, Player cpu, CpuTurnSummary summary)
    {
        var hand = cpu.Hand.Cards.ToList();
        if (hand.Count < 2) return false;

        var combos = state.Table.Combinations;
        int n = Math.Min(hand.Count, MaxWinByAddingSearchSize);

        for (int mask = 1; mask < (1 << n); mask++)
        {
            var subset = new List<Card>();
            for (int i = 0; i < n; i++)
                if ((mask & (1 << i)) != 0) subset.Add(hand[i]);

            // Require at least 2 cards and must leave at least 1 card to discard
            if (subset.Count < 2 || subset.Count >= hand.Count) continue;

            bool isWinningMove = hand.Count <= subset.Count + 1;
            var remaining = hand
                .Where(c => !subset.Any(s => ReferenceEquals(s, c)))
                .ToList();

            if (remaining.Count == 1 && remaining[0].IsJoker) continue;
            if (remaining.Count > 1 && !HasLegalDiscard(state, remaining)) continue;

            for (int ci = 0; ci < combos.Count; ci++)
            {
                if (!combos[ci].CanAcceptAll(subset, isWinningMove)) continue;
                TurnService.ExecuteAddAllToTable(state, ci, subset, isWinningMove);
                foreach (var card in subset)
                    summary.AddedToTable.Add((card, combos[ci].Type));
                return true;
            }
        }
        return false;
    }

    // Determines the optimal set of combinations to lay down and whether it constitutes a winning play.
    // Tries in order: greedy → joker-at-ends → exhaustive partition → near-win exhaustive search.
    private (List<List<Card>> combos, bool isWinning) DetermineLayDownPlan(Player cpu)
    {
        var combosToLay = _finder.FindBestCombinationSet(cpu.Hand.Cards);
        bool isWinning  = combosToLay.Sum(c => c.Count) == cpu.Hand.Count;

        if (!isWinning)
        {
            var winCombos  = _finder.FindBestCombinationSet(cpu.Hand.Cards, allowJokerAtEnds: true);
            int winCovered = winCombos.Sum(c => c.Count);
            if (winCovered == cpu.Hand.Count)
                return (winCombos, true);

            List<List<Card>>? nearWinCombos = winCovered == cpu.Hand.Count - 1 ? winCombos : null;

            var partition = _finder.TryPartitionAll(cpu.Hand.Cards, allowJokerAtEnds: false)
                         ?? _finder.TryPartitionAll(cpu.Hand.Cards, allowJokerAtEnds: true);
            if (partition != null)
                return (partition, true);

            if (nearWinCombos == null)
            {
                for (int i = 0; i < cpu.Hand.Cards.Count; i++)
                {
                    var candidate = cpu.Hand.Cards[i];
                    if (candidate.IsJoker) continue;
                    var withoutCard = cpu.Hand.Cards.Where((_, idx) => idx != i).ToList();
                    var p = _finder.TryPartitionAll(withoutCard, allowJokerAtEnds: true);
                    if (p != null) { nearWinCombos = p; break; }
                }
            }

            if (nearWinCombos != null)
                return (nearWinCombos, true);
        }

        return (combosToLay, isWinning);
    }

    // Returns true if, after laying down combosToLay, the remaining hand points would
    // still eliminate the player (score + remaining >= 100).
    private static bool IsFacingElimination(Player cpu, IReadOnlyList<List<Card>> combosToLay)
    {
        var laid = combosToLay.SelectMany(c => c).ToHashSet(ReferenceEqualityComparer.Instance);
        int remaining = cpu.Hand.Cards.Where(c => !laid.Contains(c)).Sum(c => c.PointValue);
        return cpu.Score + remaining >= 100;
    }

    // Returns true if any remaining card has a plausible connection to a combo (newly-laid
    // or already on the table): same suit within 2 ranks of a sequence end, or same rank as
    // a triple. A connection means laying down is still worthwhile for future extension.
    private static bool RemainingCardsConnectToCombos(
        IReadOnlyList<Card> remaining,
        IReadOnlyList<List<Card>> newCombos,
        IReadOnlyList<Combination> tableCombos)
    {
        foreach (var card in remaining)
        {
            // Check newly-laid combos
            foreach (var combo in newCombos)
            {
                if (CardConnectsToRawCombo(card, combo)) return true;
            }
            // Check existing table combos
            foreach (var combo in tableCombos)
            {
                if (CardConnectsToTableCombo(card, combo)) return true;
            }
        }
        return false;
    }

    private static bool CardConnectsToNonJokers(Card card, List<Card> nonJokers, bool isTriple)
    {
        if (isTriple)
            return card.Rank == nonJokers[0].Rank;

        var suit = nonJokers[0].Suit;
        if (card.Suit != suit) return false;
        int minR = nonJokers.Min(c => (int)c.Rank);
        int maxR = nonJokers.Max(c => (int)c.Rank);
        int r = (int)card.Rank;
        return r >= minR - 2 && r <= maxR + 2 && !nonJokers.Any(c => (int)c.Rank == r);
    }

    private static bool CardConnectsToRawCombo(Card card, List<Card> combo)
    {
        var nonJokers = combo.Where(c => !c.IsJoker).ToList();
        if (nonJokers.Count == 0) return false;
        bool isTriple = nonJokers.Select(c => c.Rank).Distinct().Count() == 1;
        if (!isTriple && !nonJokers.All(c => c.Suit == nonJokers[0].Suit)) return false;
        return CardConnectsToNonJokers(card, nonJokers, isTriple);
    }

    private static bool CardConnectsToTableCombo(Card card, Combination combo)
    {
        var nonJokers = combo.Cards.Where(c => !c.IsJoker).ToList();
        if (nonJokers.Count == 0) return false;
        return CardConnectsToNonJokers(card, nonJokers, combo.Type == CombinationType.Triple);
    }

    // VeryHard: exhaustively tries subsets of cards that can be added to table combos,
    // checking if the remainder of the hand can then be fully laid down (winning move).
    // Returns true and executes the winning sequence if found.
    internal bool TryWinByAddingFirst(GameState state, Player cpu, CpuTurnSummary summary)
    {
        var hand = cpu.Hand.Cards.ToList();
        var table = state.Table.Combinations;

        // Cards in hand that can be added to at least one table combo on a winning move.
        var addable = hand
            .Where(c => Enumerable.Range(0, table.Count).Any(ci => table[ci].CanAccept(c, isWinningMove: true)))
            .ToList();

        if (addable.Count == 0) return false;

        int n = Math.Min(addable.Count, MaxWinByAddingSearchSize);
        for (int mask = 1; mask < (1 << n); mask++)
        {
            var toAdd = new List<Card>();
            for (int i = 0; i < n; i++)
                if ((mask & (1 << i)) != 0) toAdd.Add(addable[i]);

            var toAddSet = toAdd.ToHashSet(ReferenceEqualityComparer.Instance);
            var simHand = hand.Where(c => !toAddSet.Contains(c)).ToList();

            // Check if the remaining cards can all be laid down
            var remaining = _finder.FindBestCombinationSet(simHand, allowJokerAtEnds: true);
            if (remaining.Sum(c => c.Count) != simHand.Count) continue;

            // Winning path found — execute: add to table first, then lay down the rest
            foreach (var card in toAdd)
            {
                if (!cpu.Hand.Cards.Contains(card)) continue;
                for (int ci = 0; ci < table.Count; ci++)
                {
                    if (table[ci].CanAccept(card, isWinningMove: true))
                    {
                        bool isDuplicate = table[ci].Type == CombinationType.Triple && !card.IsJoker
                            && table[ci].Cards.Any(c => c.Suit == card.Suit);
                        TurnService.ExecuteAddToTable(state, ci, card, isWinningMove: true);
                        summary.AddedToTable.Add((card, table[ci].Type));
                        if (isDuplicate) summary.TripleDiscardsPending.Add((ci, card));
                        break;
                    }
                }
            }
            foreach (var combo in remaining)
            {
                var available = combo.Where(c => cpu.Hand.Cards.Contains(c)).ToList();
                if (available.Count == combo.Count)
                {
                    var type = CombinationValidator.Classify(available, allowJokerAtEnds: true);
                    if (type != null)
                    {
                        var laid = TurnService.ExecuteLayDown(state, available, allowJokerAtEnds: true);
                        summary.LaidDown.Add((laid.Cards.ToList(), type.Value));
                    }
                }
            }
            return true;
        }

        return false;
    }

    // VeryHard: like FindNearCombinationCards but drops protection for groups where all
    // completing cards have already been publicly seen (discards + table) and no jokers remain.
    internal HashSet<Card> FindViableNearCombinationCards(IReadOnlyList<Card> hand, IReadOnlyList<Card> seenPublic)
    {
        var result = new HashSet<Card>(ReferenceEqualityComparer.Instance);
        var nonJokers = hand.Where(c => !c.IsJoker).ToList();
        int unseenJokers = 4 - seenPublic.Count(c => c.IsJoker) - hand.Count(c => c.IsJoker);

        // Triple candidates: same rank, 2+ distinct suits
        var byRank = nonJokers.GroupBy(c => c.Rank);
        foreach (var rankGroup in byRank)
        {
            var cards = rankGroup.ToList();
            var usedSuits = cards.Select(c => c.Suit).Distinct().ToList();
            if (usedSuits.Count < 2) continue;

            // Viable if any other suit still has unseen copies (jokers don't help triples)
            bool viable = Enum.GetValues<Suit>().Any(s =>
                !usedSuits.Contains(s) && UnseenCount(rankGroup.Key, s, seenPublic, hand) > 0);
            if (viable)
                foreach (var c in cards) result.Add(c);
        }

        // Sequence candidates: adjacent same-suit pairs (rankDiff <= 2)
        var bySuit = nonJokers.GroupBy(c => c.Suit);
        foreach (var suitGroup in bySuit)
        {
            var sorted = suitGroup.OrderBy(c => (int)c.Rank).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                int r1 = (int)sorted[i].Rank;
                int r2 = (int)sorted[i + 1].Rank;
                if (r2 - r1 > 2) continue;

                var suit = sorted[i].Suit;
                bool viable = unseenJokers > 0; // joker can fill gap or extend

                if (!viable)
                {
                    // Check if any connecting/extending rank (same suit) is still unseen
                    for (int r = r1 - 1; r <= r2 + 1 && !viable; r++)
                    {
                        if (r < 1 || r > 13) continue;   // Ace=1, King=13
                        if (r >= r1 && r <= r2) continue; // already in pair
                        viable = UnseenCount((Rank)r, suit, seenPublic, hand) > 0;
                    }
                }

                if (viable)
                {
                    result.Add(sorted[i]);
                    result.Add(sorted[i + 1]);
                }
            }
        }

        return result;
    }

    // How many copies of (rank, suit) are neither in own hand nor publicly seen.
    // 2 copies per rank/suit exist in the full deck (two decks).
    private static int UnseenCount(Rank rank, Suit suit, IReadOnlyList<Card> seenPublic, IReadOnlyList<Card> ownHand)
    {
        int seen = seenPublic.Count(c => c.Rank == rank && c.Suit == suit);
        int own  = ownHand.Count(c => c.Rank == rank && c.Suit == suit);
        return Math.Max(0, 2 - seen - own);
    }

    public void ExecuteCpuDiscardStep(GameState state, Player cpu, CpuTurnSummary summary,
        IReadOnlyList<Card>? seenPublic = null, IReadOnlyList<Card>? opponentDiscards = null, int minOpponentHandCount = int.MaxValue,
        IReadOnlyList<Card>? opponentDfdCards = null)
    {
        var discard = DecideDiscard(state, cpu, seenPublic, opponentDiscards, minOpponentHandCount, opponentDfdCards);
        TurnService.ExecuteDiscard(state, discard);
        summary.Discarded = discard;
    }

    private static bool CanReplaceTableJoker(GameState state, Card card) =>
        state.Table.Combinations.Any(c => c.CanReplaceJoker(card));

    // Returns true if the hand contains at least one card that can legally be discarded.
    private static bool HasLegalDiscard(GameState state, IEnumerable<Card> hand) =>
        hand.Any(c => !c.IsJoker && !CanReplaceTableJoker(state, c));

    // Only take from discard if the card will actually be used in a new combo from hand,
    // AND the combo set improves beyond what the hand can already achieve without it.
    public bool DecideDrawSource(GameState state, Player cpu)
    {
        var discardTop = state.Deck.DiscardTop;
        if (discardTop == null) return false;

        var hypotheticalHand = cpu.Hand.Cards.Concat(new[] { discardTop }).ToList();
        var combos = _finder.FindBestCombinationSet(hypotheticalHand);
        if (!combos.Any(c => c.Contains(discardTop, ReferenceEqualityComparer.Instance)))
            return false;

        // Don't draw from discard if the same combo count is achievable without it
        // (i.e. the hand already contains a duplicate of the discard card that covers the combo)
        var combosWithout = _finder.FindBestCombinationSet(cpu.Hand.Cards);
        int cardsCoveredWith = combos.Sum(c => c.Count);
        int cardsCoveredWithout = combosWithout.Sum(c => c.Count);
        return cardsCoveredWith > cardsCoveredWithout;
    }

    // Hard/VeryHard: also take from discard if the card can be added to an existing table combo.
    // When urgent, also draw if the card forms a near-combination with hand cards —
    // reducing hand count quickly matters more than waiting for a complete combo.
    public bool DecideDrawSourceHard(GameState state, Player cpu)
    {
        if (DecideDrawSource(state, cpu)) return true;

        var discardTop = state.Deck.DiscardTop;
        if (discardTop == null) return false;

        return state.Table.Combinations.Any(c => c.CanAccept(discardTop, isWinningMove: false));
    }

    public List<List<Card>> DecideCombinationsToLayDown(GameState state, Player cpu)
    {
        return _finder.FindBestCombinationSet(cpu.Hand.Cards);
    }

    public List<(int comboIndex, Card card)> DecideCardsToAddToTable(GameState state, Player cpu, bool isUrgent = false)
    {
        var result = new List<(int, Card)>();
        var combos = state.Table.Combinations;
        bool isWinningMove = cpu.Hand.Count <= 2;

        bool handHasJoker = cpu.Hand.Cards.Any(c => c.IsJoker);
        bool anySequenceOnTable = combos.Any(c => c.Type == CombinationType.Sequence);

        for (int i = 0; i < combos.Count; i++)
        {
            foreach (var card in cpu.Hand.Cards)
            {
                if (combos[i].CanAccept(card, isWinningMove))
                {
                    // Don't add a non-joker to a triple when the hand holds a joker and no
                    // sequence exists on the table. Keeping the card preserves the winning path:
                    // add card to triple + add joker to a future sequence = win with 0 cards.
                    // Exception: when urgent (opponent near win), shed cards aggressively — the
                    // "wait for a sequence" strategy is too slow when the game may end this turn.
                    // Exception: burn cards (duplicate suit already in the triple) are discarded
                    // either way — burning only changes WHERE they land on the discard pile
                    // (behind the top card), so the joker-pairing strategy is unaffected.
                    bool isBurn = combos[i].Type == CombinationType.Triple && !card.IsJoker
                        && combos[i].Cards.Any(c => c.Suit == card.Suit);
                    if (combos[i].Type == CombinationType.Triple && !card.IsJoker
                        && handHasJoker && !anySequenceOnTable && !isWinningMove && !isUrgent
                        && !isBurn)
                        continue;

                    result.Add((i, card));
                }
            }
        }

        return result;
    }

    public Card DecideDiscard(GameState state, Player cpu,
        IReadOnlyList<Card>? seenPublic = null, IReadOnlyList<Card>? opponentDiscards = null, int minOpponentHandCount = int.MaxValue,
        IReadOnlyList<Card>? opponentDfdCards = null)
    {
        var hand = cpu.Hand.Cards.OrderByDescending(c => c.PointValue).ToList();
        var swapEligible = new HashSet<Card>(
            hand.Where(c => CanReplaceTableJoker(state, c)),
            ReferenceEqualityComparer.Instance);

        // Urgency mode: opponent is nearly done or CPU is near elimination — discard aggressively,
        // ignoring anti-feed and near-combo protection. Dump the highest-point dead-weight card
        // first to minimise penalty if we lose this round.
        if (IsUrgent(minOpponentHandCount, cpu))
        {
            var urgentCandidates = hand.Where(c => !c.IsJoker && !swapEligible.Contains(c)).ToList();
            if (urgentCandidates.Count > 0) return urgentCandidates[0];
        }

        var protectedCards = seenPublic != null
            ? FindViableNearCombinationCards(cpu.Hand.Cards, seenPublic)
            : _finder.FindNearCombinationCards(cpu.Hand.Cards);

        // High-tier cards (J/Q/K/A) require at least 2 combo partners to retain their protection.
        // A single partner is insufficient justification for a 10+ point liability across lost rounds.
        var weaklyProtectedHighTier = protectedCards
            .Where(c => !c.IsJoker && c.PointValue >= HighTierMinPoints
                        && CountComboPartners(c, cpu.Hand.Cards) < HighTierMinPartnersToProtect)
            .ToList();
        foreach (var card in weaklyProtectedHighTier)
            protectedCards.Remove(card);

        // Jokers and cards that can replace a table joker can never be discarded
        var discardCandidates = hand
            .Where(c => !c.IsJoker && !protectedCards.Contains(c) && !swapEligible.Contains(c))
            .ToList();

        discardCandidates = ApplyAntiFeedFilters(state, discardCandidates, opponentDiscards, opponentDfdCards);

        if (opponentDiscards != null && opponentDiscards.Count >= 2 && discardCandidates.Count > 1)
        {
            // Only accept the filtered result if it doesn't drop to a less-valuable card than the
            // natural top candidate. The filter is a tiebreaker among equally-valuable cards, not
            // a reason to hold a higher-value card in order to discard a lower-value one.
            var filtered = ApplyOpponentDiscardFilter(discardCandidates, opponentDiscards);
            if (filtered[0].PointValue >= discardCandidates[0].PointValue)
                discardCandidates = filtered;
        }

        if (discardCandidates.Count > 0)
            return discardCandidates[0];

        // Fall back: all remaining candidates are protected near-combo cards.
        // Prefer sacrificing near-triple-only cards over near-sequence cards —
        // a near-sequence + joker draw = immediate win; a near-triple + joker = 15-pt liability.
        // Near-triples whose completing cards are all seen are already unprotected above.
        var nonJokers = hand
            .Where(c => !c.IsJoker && !swapEligible.Contains(c))
            .ToList();
        if (nonJokers.Count > 0)
        {
            var nearSeq = FindNearSequenceCards(cpu.Hand.Cards);
            var tripleOnly = nonJokers
                .Where(c => !nearSeq.Contains(c, ReferenceEqualityComparer.Instance))
                .ToList();
            if (tripleOnly.Count > 0) return tripleOnly[0];
            return nonJokers[0];
        }

        // Last resort: any non-joker (jokers must never be discarded)
        var anyNonJoker = hand.Where(c => !c.IsJoker).ToList();
        if (anyNonJoker.Count > 0) return anyNonJoker[0];

        // Hand is all jokers — shouldn't happen (CPU should have won), but avoid crash
        return hand[0];
    }

    // Removes candidates adjacent to the discard top (same suit, within threshold ranks)
    // and candidates that would directly extend a table combo — either gift a combo to the next player.
    // Also checks all opponent discards in the round as hot zones (not just the current top).
    private static List<Card> ApplyAntiFeedFilters(GameState state, List<Card> candidates,
        IReadOnlyList<Card>? opponentDiscards = null, IReadOnlyList<Card>? opponentDfdCards = null)
    {
        if (candidates.Count <= 1)
            return candidates;

        int threshold = 1;

        var discardTop = state.Deck.DiscardTop;
        if (discardTop != null && !discardTop.IsJoker)
        {
            int topRank = (int)discardTop.Rank;
            var safe = candidates
                .Where(c => c.Suit != discardTop.Suit || Math.Abs((int)c.Rank - topRank) > threshold)
                .ToList();
            if (safe.Count > 0)
                candidates = safe;
        }

        // Block cards rank-adjacent to any card an opponent drew from the discard pile.
        // A DFD draw is confirmed to be used in a combo — adjacent same-suit cards directly extend
        // the opponent's sequence and are therefore higher-risk than regular discard-pattern signals.
        if (opponentDfdCards != null && opponentDfdCards.Count > 0 && candidates.Count > 1)
        {
            var safe = candidates
                .Where(c => !c.IsJoker && !opponentDfdCards
                    .Where(dfd => !dfd.IsJoker && dfd.Suit == c.Suit)
                    .Any(dfd => Math.Abs((int)dfd.Rank - (int)c.Rank) <= threshold))
                .ToList();
            if (safe.Count > 0)
                candidates = safe;
        }

        // Also treat every opponent discard in the round as a hot zone.
        // A player who discarded card X in suit S likely held other S-suit cards nearby;
        // discarding into that neighbourhood risks completing their sequence.
        if (opponentDiscards != null && candidates.Count > 1)
        {
            var safe = candidates
                .Where(c => !c.IsJoker && !opponentDiscards
                    .Where(od => !od.IsJoker && od.Suit == c.Suit)
                    .Any(od => Math.Abs((int)od.Rank - (int)c.Rank) <= threshold))
                .ToList();
            if (safe.Count > 0)
                candidates = safe;
        }

        if (state.Table.Combinations.Count > 0 && candidates.Count > 1)
        {
            var safeFromTable = candidates
                .Where(c => !state.Table.Combinations.Any(combo => combo.CanAccept(c, isWinningMove: false)))
                .ToList();
            if (safeFromTable.Count > 0)
                candidates = safeFromTable;
        }

        return candidates;
    }

    // VeryHard: prefer discarding ranks/suits that opponents have already discarded,
    // indicating disinterest. Requires ≥2 opponent discards for enough signal.
    private static List<Card> ApplyOpponentDiscardFilter(List<Card> candidates, IReadOnlyList<Card> opponentDiscards)
    {
        var discardedRanks = opponentDiscards.Where(c => !c.IsJoker).Select(c => c.Rank).ToHashSet();
        var discardedSuits = opponentDiscards.Where(c => !c.IsJoker).Select(c => c.Suit).ToHashSet();
        var rankSafe = candidates.Where(c => discardedRanks.Contains(c.Rank)).ToList();
        if (rankSafe.Count > 0)
        {
            var rankAndSuitSafe = rankSafe.Where(c => discardedSuits.Contains(c.Suit)).ToList();
            return rankAndSuitSafe.Count > 0 ? rankAndSuitSafe : rankSafe;
        }
        var suitSafe = candidates.Where(c => discardedSuits.Contains(c.Suit)).ToList();
        return suitSafe.Count > 0 ? suitSafe : candidates;
    }

    // Cards that are part of a near-sequence (same-suit pair with rankDiff ≤ 2).
    // Used to identify cards worth keeping over near-triple-only cards when a forced
    // discard among near-combo cards is required.
    private static HashSet<Card> FindNearSequenceCards(IReadOnlyList<Card> hand)
    {
        var result = new HashSet<Card>(ReferenceEqualityComparer.Instance);
        foreach (var suitGroup in hand.Where(c => !c.IsJoker).GroupBy(c => c.Suit))
        {
            var sorted = suitGroup.OrderBy(c => (int)c.Rank).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if ((int)sorted[i + 1].Rank - (int)sorted[i].Rank <= 2)
                {
                    result.Add(sorted[i]);
                    result.Add(sorted[i + 1]);
                }
            }
        }
        return result;
    }

    // Returns true when the first drawn card pairs tightly with another card in hand,
    // meaning discarding it for the second-draw privilege would break a near-combo.
    // Uses tight criteria (rankDiff == 1 for sequences) to avoid over-protecting.
    // High-tier cards (J/Q/K/A) require at least 2 combo partners to be considered protected —
    // a single partner is not enough justification for a 10+ point liability.
    private static bool IsFirstDrawCardProtected(Card firstCard, IReadOnlyList<Card> hand)
    {
        bool isNearTriple = hand.Any(c => !ReferenceEquals(c, firstCard)
            && !c.IsJoker && c.Rank == firstCard.Rank && c.Suit != firstCard.Suit);
        bool isTightNearSeq = !firstCard.IsJoker && hand.Any(c => !ReferenceEquals(c, firstCard)
            && !c.IsJoker && c.Suit == firstCard.Suit
            && Math.Abs((int)c.Rank - (int)firstCard.Rank) == 1);

        if (!isNearTriple && !isTightNearSeq) return false;
        if (firstCard.IsJoker || firstCard.PointValue < HighTierMinPoints) return true;

        int partnerCount = CountComboPartners(firstCard, hand);
        return partnerCount >= HighTierMinPartnersToProtect;
    }

    // Counts how many cards in hand could plausibly form a combo with the given card:
    // same rank (different suit) counts as a triple partner;
    // same suit within ±2 ranks counts as a sequence partner.
    private static int CountComboPartners(Card card, IReadOnlyList<Card> hand)
    {
        int count = 0;
        foreach (var c in hand)
        {
            if (ReferenceEquals(c, card) || c.IsJoker) continue;
            bool isTriplePartner = c.Rank == card.Rank && c.Suit != card.Suit;
            bool isSeqPartner    = c.Suit == card.Suit && Math.Abs((int)c.Rank - (int)card.Rank) <= 2;
            if (isTriplePartner || isSeqPartner) count++;
        }
        return count;
    }

    // True when the card belongs to a 3-card consecutive sequence fragment already in hand
    // (two other same-suit cards exist that form three consecutive ranks with it).
    private static bool HasThreeCardSeqFragment(Card card, IReadOnlyList<Card> hand)
    {
        int r = (int)card.Rank;
        var samesuit = hand
            .Where(c => !ReferenceEquals(c, card) && !c.IsJoker && c.Suit == card.Suit)
            .Select(c => (int)c.Rank)
            .ToHashSet();
        return (samesuit.Contains(r - 2) && samesuit.Contains(r - 1)) ||
               (samesuit.Contains(r - 1) && samesuit.Contains(r + 1)) ||
               (samesuit.Contains(r + 1) && samesuit.Contains(r + 2));
    }

    // Urgency: true when any opponent is close to winning, the CPU is near elimination,
    // or the CPU holds jokers whose penalty alone would cause elimination (joker trap).
    // In urgent mode, protection filters are bypassed to dump high-point dead weight fast.
    internal static bool IsUrgent(int minOpponentHandCount, Player cpu)
    {
        if (minOpponentHandCount <= UrgencyOpponentHandThreshold || cpu.Score >= UrgencyScoreThreshold) return true;
        int jokerPenalty = cpu.Hand.Cards.Count(c => c.IsJoker) * JokerPointValue;
        return jokerPenalty > 0 && cpu.Score + jokerPenalty >= EliminationScore;
    }

    // Score for a lay-down plan: higher is better.
    // Primary axis: cards removed (×1000 weight). Secondary: remaining hand penalty (lower = better).
    private static int ScorePlan(IReadOnlyList<Card> hand, IEnumerable<List<Card>> combos)
    {
        var laid = combos.SelectMany(c => c).ToHashSet(ReferenceEqualityComparer.Instance);
        return laid.Count * 1000 - hand.Where(c => !laid.Contains(c)).Sum(c => c.PointValue);
    }

    // Swap a card for a table joker only when:
    // 1. The card being placed is not needed for current combos, AND
    // 2. The joker received would actually be used in a combination.
    private void TryJokerSwaps(GameState state, Player cpu, CpuTurnSummary summary)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var hand = cpu.Hand.Cards.ToList();
            var bestCombos = _finder.FindBestCombinationSet(hand);
            bool found = false;

            for (int ci = 0; ci < state.Table.Combinations.Count && !found; ci++)
            {
                var combo = state.Table.Combinations[ci];
                foreach (var card in hand.Where(c => !c.IsJoker))
                {
                    if (!combo.CanReplaceJoker(card)) continue;

                    // Card must not be part of current best combos
                    bool usedInCombo = bestCombos.Any(c => c.Any(x => ReferenceEquals(x, card)));
                    if (usedInCombo) continue;

                    // Peek: which joker would we get?
                    var joker = combo.PeekSwapJoker(card);
                    if (joker == null) continue;

                    // Simulate: hand minus card, plus joker — would the joker be used?
                    var simHand = hand.Where(c => !ReferenceEquals(c, card)).Append(joker).ToList();
                    var simCombosNormal  = _finder.FindBestCombinationSet(simHand, allowJokerAtEnds: false);
                    bool jokerUsedNormal = simCombosNormal.Any(c => c.Any(x => ReferenceEquals(x, joker)));

                    // Full winning lay-down: all sim-hand cards covered.
                    var simCombosWinning  = _finder.FindBestCombinationSet(simHand, allowJokerAtEnds: true);
                    bool jokerUsedWinning = simCombosWinning.SelectMany(c => c).Count() == simHand.Count
                                        && simCombosWinning.Any(c => c.Any(x => ReferenceEquals(x, joker)));

                    // Near-win: lay N-1 cards (joker used in combo), discard the remaining non-joker.
                    bool jokerUsedNearWin = false;
                    if (!jokerUsedNormal && !jokerUsedWinning)
                    {
                        int covered = simCombosWinning.SelectMany(c => c).Count();
                        if (covered == simHand.Count - 1
                            && simCombosWinning.Any(c => c.Any(x => ReferenceEquals(x, joker))))
                        {
                            var coveredSet = new HashSet<Card>(
                                simCombosWinning.SelectMany(c => c), ReferenceEqualityComparer.Instance);
                            var uncovered = simHand.Where(c => !coveredSet.Contains(c)).ToList();
                            jokerUsedNearWin = uncovered.Count == 1 && !uncovered[0].IsJoker;
                        }

                        if (!jokerUsedNearWin)
                        {
                            foreach (var candidate in simHand.Where(c => !c.IsJoker))
                            {
                                var withoutCandidate = simHand.Where(c => !ReferenceEquals(c, candidate)).ToList();
                                var partition = _finder.TryPartitionAll(withoutCandidate, allowJokerAtEnds: true);
                                if (partition != null && partition.Any(c => c.Any(x => ReferenceEquals(x, joker))))
                                {
                                    jokerUsedNearWin = true;
                                    break;
                                }
                            }
                        }
                    }

                    // Guard: require the swap to improve or maintain the plan score, not just
                    // "joker ends up in some combo". Without this check, the greedy can pick a
                    // joker-sequence that displaces a larger non-joker combo, reducing total
                    // coverage or leaving a higher-penalty remainder in the hand.
                    bool swapImprovesScore = jokerUsedNormal &&
                        ScorePlan(simHand, simCombosNormal) > ScorePlan(hand, bestCombos);

                    if (!swapImprovesScore && !jokerUsedWinning && !jokerUsedNearWin
                        && !WinsByAddingToTable(state, ci, card, simHand, simCombosWinning)) continue;

                    TurnService.ExecuteSwapJoker(state, ci, card);
                    summary.SwappedJokers.Add(card);
                    summary.AddedToTable.Add((card, combo.Type));
                    found = true;
                    break;
                }
            }
            if (!found) break;
        }
    }

    // Returns true when ALL simHand cards can be placed as a winning move by combining
    // new combos (simCombosWinning) with additions to existing table sequences/triples.
    // This catches the case where the post-swap hand consists only of jokers (or small
    // leftovers) that cannot form new combos by themselves but can extend table combos.
    internal bool WinsByAddingToTable(
        GameState state, int swappedComboIndex, Card swappedCard,
        List<Card> simHand, List<List<Card>> simCombosWinning)
    {
        // Cards already covered by new combos from hand
        var coveredSet = new HashSet<Card>(
            simCombosWinning.SelectMany(c => c), ReferenceEqualityComparer.Instance);
        var uncovered = simHand.Where(c => !coveredSet.Contains(c)).ToList();
        if (uncovered.Count == 0) return true;

        // Simulate the table after the swap: the joker in combo[swappedComboIndex] is
        // replaced by swappedCard, so the table no longer has that joker at that slot.
        var simTable = state.Table.Combinations.Select((combo, i) =>
        {
            var cards = combo.Cards.ToList();
            if (i == swappedComboIndex)
            {
                var j = cards.FirstOrDefault(c => c.IsJoker);
                if (j != null) { cards.Remove(j); cards.Add(swappedCard); }
            }
            return (Combo: combo, Cards: cards);
        }).ToList();

        // Greedily place each uncovered card onto any table combo (isWinningMove = true).
        foreach (var card in uncovered)
        {
            bool placed = false;
            for (int i = 0; i < simTable.Count && !placed; i++)
            {
                var (origCombo, simCards) = simTable[i];
                if (!CanAddToSimCombo(origCombo, simCards, card)) continue;
                simTable[i] = (origCombo, simCards.Concat(new[] { card }).ToList());
                placed = true;
            }
            if (!placed) return false;
        }
        return true;
    }

    // Checks whether `card` can be added to a simulated combo (represented as a mutable
    // card list derived from `original`).  Uses the real CanAccept when the list is
    // still at its original size; otherwise replicates the validation manually so we
    // don't need to mutate the actual Combination object.
    internal static bool CanAddToSimCombo(Combination original, List<Card> simCards, Card card)
    {
        if (original.Type == CombinationType.Triple)
        {
            // If unextended, delegate to the real method (preserves _estrangeiraSuit logic).
            if (simCards.Count == original.Cards.Count)
                return original.CanAccept(card, isWinningMove: false);
            // Extended triple: replicate core checks (estrangeiraSuit edge case omitted).
            if (card.IsJoker || simCards.Count >= Combination.MaxTripleSize) return false;
            var nonJokers = simCards.Where(c => !c.IsJoker).ToList();
            return nonJokers.Count > 0
                && nonJokers[0].Rank == card.Rank
                && simCards.Count(c => c.Suit == card.Suit) < 2;
        }
        // Sequence
        var nonJokersSeq = simCards.Where(c => !c.IsJoker).ToList();
        if (nonJokersSeq.Count == 0) return false;
        if (!card.IsJoker && card.Suit != nonJokersSeq[0].Suit) return false;
        return CombinationValidator.IsValidSequence(
            simCards.Concat(new[] { card }).ToList(), allowJokerAtEnds: true);
    }
}
