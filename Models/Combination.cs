using CardGames.Services;

namespace CardGames.Models;

public class Combination
{
    internal const int MaxTripleSize    = 6;
    internal const int MaxSequenceSize  = 13; // A through K — no rank can appear twice
    private const int MaxCardsPerSuit = 2;

    private readonly List<Card> _cards;
    // The suit that may NEVER be added to this triple (the 4th / "estrangeira" suit)
    private readonly Suit? _estrangeiraSuit;

    public Combination(List<Card> cards, CombinationType type, string ownerId)
    {
        _cards = new List<Card>(cards);
        Type = type;
        OwnerId = ownerId;

        if (type == CombinationType.Triple)
        {
            var usedSuits = cards.Select(c => c.Suit).ToHashSet();
            _estrangeiraSuit = Enum.GetValues<Suit>().Cast<Suit?>()
                .FirstOrDefault(s => !usedSuits.Contains(s!.Value));
        }

        Sort();
    }

    public IReadOnlyList<Card> Cards => _cards;
    public CombinationType Type { get; }
    public string OwnerId { get; }
    public bool IsWinningLaydown { get; set; }
    public bool IsWinningAddition { get; set; }

    public bool CanAccept(Card card, bool isWinningMove = false)
    {
        if (Type == CombinationType.Triple)
            return CanAcceptTriple(card);
        return CanAcceptSequence(card, isWinningMove);
    }

    public void AddCard(Card card, bool isWinningMove = false)
    {
        if (!CanAccept(card, isWinningMove))
            throw new InvalidOperationException($"Card {card} cannot be added to this combination.");
        _cards.Add(card);
        Sort();
    }

    public void RemoveCard(Card card)
    {
        _cards.Remove(card);
        Sort();
    }

    // ── Sorting ─────────────────────────────────────────────────────────────

    private void Sort()
    {
        if (Type == CombinationType.Sequence)
            SortAsSequence();
        else
            SortAsTriple();
    }

    private static bool IsRed(Card c) => c.Suit is Suit.Hearts or Suit.Diamonds;

    private void SortAsTriple()
    {
        // Separate the first occurrence of each suit (originals) from extras (duplicates added
        // during a turn). Duplicates must always appear at the end so the burn animation can
        // target them by index regardless of how many were added.
        var seen = new HashSet<Suit>();
        var originals  = new List<Card>();
        var duplicates = new List<Card>();
        foreach (var card in _cards)
        {
            if (seen.Add(card.Suit)) originals.Add(card);
            else                     duplicates.Add(card);
        }

        InterleaveByColour(originals);

        foreach (var dup in duplicates)
            _cards.Add(dup);
    }

    private void InterleaveByColour(List<Card> originals)
    {
        var reds   = originals.Where(c =>  IsRed(c)).OrderBy(c => (int)c.Suit).ToList();
        var blacks = originals.Where(c => !IsRed(c)).OrderBy(c => (int)c.Suit).ToList();

        var majority = reds.Count >= blacks.Count ? reds : blacks;
        var minority = reds.Count >= blacks.Count ? blacks : reds;

        _cards.Clear();
        int mi = 0;
        for (int i = 0; i < majority.Count; i++)
        {
            _cards.Add(majority[i]);
            if (mi < minority.Count)
                _cards.Add(minority[mi++]);
        }
        while (mi < minority.Count)
            _cards.Add(minority[mi++]);
    }

    private void SortAsSequence()
    {
        var nonWild = _cards.Where(c => !c.IsJoker).ToList();
        var wilds   = _cards.Where(c =>  c.IsJoker).ToList();
        if (nonWild.Count == 0) return;

        // Ace-high only if treating Ace as rank 14 is actually gapless within available jokers.
        bool aceHigh = false;
        if (nonWild.Any(c => c.Rank == Rank.Ace) && nonWild.Any(c => c.Rank is Rank.Queen or Rank.King))
        {
            var highRanks = nonWild.Select(c => c.Rank == Rank.Ace ? 14 : (int)c.Rank).OrderBy(r => r).ToList();
            int highSpan = highRanks[^1] - highRanks[0] + 1;
            int highGaps = highSpan - highRanks.Count;
            aceHigh = highGaps <= wilds.Count;
        }

        int EffRank(Card c) => c.Rank == Rank.Ace && aceHigh ? 14 : (int)c.Rank;
        nonWild.Sort((a, b) => EffRank(a).CompareTo(EffRank(b)));

        // Count internal gaps and extra (end-extending) jokers
        int internalGaps = 0;
        for (int i = 1; i < nonWild.Count; i++)
            internalGaps += EffRank(nonWild[i]) - EffRank(nonWild[i - 1]) - 1;
        int extraJokers = Math.Max(0, wilds.Count - internalGaps);

        // Distribute extra jokers: high end up to rank 14 (Ace), remainder at low end.
        // Ace-high sequences cannot extend past Ace, so all extras go low.
        int highJokers = aceHigh ? 0 : Math.Min(extraJokers, Math.Max(0, 14 - EffRank(nonWild[^1])));
        int lowJokers  = extraJokers - highJokers;

        _cards.Clear();
        int wi = 0;

        // Pre-pend low-end jokers
        for (int i = 0; i < lowJokers && wi < wilds.Count; i++, wi++)
            _cards.Add(wilds[wi]);

        // Non-wild cards with internal joker fills
        for (int i = 0; i < nonWild.Count; i++)
        {
            if (i > 0)
            {
                int gap = EffRank(nonWild[i]) - EffRank(nonWild[i - 1]) - 1;
                for (int g = 0; g < gap && wi < wilds.Count; g++, wi++)
                    _cards.Add(wilds[wi]);
            }
            _cards.Add(nonWild[i]);
        }

        // Append high-end jokers
        while (wi < wilds.Count)
            _cards.Add(wilds[wi++]);
    }

    // ── Multi-card add ───────────────────────────────────────────────────────

    // Returns true if all cards can be added to this sequence at once.
    public bool CanAcceptAll(IReadOnlyList<Card> cards, bool isWinningMove = false)
    {
        if (Type != CombinationType.Sequence) return false;
        var test = _cards.Concat(cards).ToList();
        return CombinationValidator.IsValidSequence(test, isWinningMove);
    }

    // Validates the combined result, then adds all cards at once.
    public void AddCards(IReadOnlyList<Card> cards, bool isWinningMove = false)
    {
        if (!CanAcceptAll(cards, isWinningMove))
            throw new InvalidOperationException("Cards cannot be added to this combination.");
        foreach (var c in cards) _cards.Add(c);
        Sort();
    }

    // ── Joker-swap ───────────────────────────────────────────────────────────

    // Returns true if 'card' can replace one of the jokers in this sequence.
    public bool CanReplaceJoker(Card card)
    {
        if (Type != CombinationType.Sequence) return false;
        if (card.IsJoker) return false;
        foreach (var joker in _cards.Where(c => c.IsJoker).ToList())
        {
            var test = _cards.Where(c => !ReferenceEquals(c, joker)).Concat(new[] { card }).ToList();
            if (CombinationValidator.IsValidSequence(test, allowJokerAtEnds: true))
                return true;
        }
        return false;
    }

    // Returns the joker that would be displaced by 'replacement', without mutating.
    public Card? PeekSwapJoker(Card replacement)
    {
        foreach (var joker in _cards.Where(c => c.IsJoker))
        {
            var test = _cards.Where(c => !ReferenceEquals(c, joker)).Concat(new[] { replacement }).ToList();
            if (CombinationValidator.IsValidSequence(test, allowJokerAtEnds: true))
                return joker;
        }
        return null;
    }

    // Removes a joker that 'replacement' can stand in for, adds 'replacement', returns the joker.
    public Card SwapJoker(Card replacement)
    {
        foreach (var joker in _cards.Where(c => c.IsJoker).ToList())
        {
            var test = _cards.Where(c => !ReferenceEquals(c, joker)).Concat(new[] { replacement }).ToList();
            if (CombinationValidator.IsValidSequence(test, allowJokerAtEnds: true))
            {
                _cards.Remove(joker);
                _cards.Add(replacement);
                Sort();
                return joker;
            }
        }
        throw new InvalidOperationException("No joker can be replaced by this card.");
    }

    // Returns true if 'joker' (in hand) can replace a non-joker in this sequence.
    public bool CanReturnJoker(Card joker)
    {
        if (Type != CombinationType.Sequence) return false;
        if (!joker.IsJoker) return false;
        foreach (var nonJoker in _cards.Where(c => !c.IsJoker).ToList())
        {
            var test = _cards.Where(c => !ReferenceEquals(c, nonJoker)).Concat(new[] { joker }).ToList();
            if (CombinationValidator.IsValidSequence(test, allowJokerAtEnds: false))
                return true;
        }
        return false;
    }

    // Replaces the best-fit non-joker with 'joker'; returns the displaced card.
    public Card ReturnJoker(Card joker)
    {
        foreach (var nonJoker in _cards.Where(c => !c.IsJoker).ToList())
        {
            var test = _cards.Where(c => !ReferenceEquals(c, nonJoker)).Concat(new[] { joker }).ToList();
            if (CombinationValidator.IsValidSequence(test, allowJokerAtEnds: false))
            {
                _cards.Remove(nonJoker);
                _cards.Add(joker);
                Sort();
                return nonJoker;
            }
        }
        throw new InvalidOperationException("Joker cannot be returned to this sequence.");
    }

    // ── Acceptance checks ────────────────────────────────────────────────────

    private bool CanAcceptTriple(Card card)
    {
        if (card.IsJoker) return false; // jokers not allowed in triples

        var rank = _cards[0].Rank;
        if (card.Rank != rank) return false;
        if (card.Suit == _estrangeiraSuit) return false;

        int suitCount = _cards.Count(c => c.Suit == card.Suit);
        if (suitCount >= MaxCardsPerSuit) return false;

        if (_cards.Count >= MaxTripleSize) return false;

        return true;
    }

    private bool CanAcceptSequence(Card card, bool isWinningMove = false)
    {
        var nonJokers = _cards.Where(c => !c.IsJoker).ToList();
        if (nonJokers.Count == 0) return false;

        if (!card.IsJoker && card.Suit != nonJokers[0].Suit) return false;

        var testCards = _cards.Concat(new[] { card }).ToList();
        return CombinationValidator.IsValidSequence(testCards, isWinningMove);
    }

    public override string ToString()
    {
        var cardStr = string.Join("  ", _cards.Select(c => c.DisplayName));
        return $"{cardStr}  ({Type})";
    }
}
