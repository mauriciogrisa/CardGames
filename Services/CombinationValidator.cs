using CardGames.Models;

namespace CardGames.Services;

public enum ComboRejection
{
    JokerInTriple,
    TripleRankMismatch,
    TripleDuplicateSuit,
    SeqMixedSuits,
    SeqDuplicateRanks,
    SeqGapTooLarge,
    SeqJokerAtEndNotWinning,
    SeqAdjacentJokersNotWinning,
}

public static class CombinationValidator
{
    public static bool IsValidTriple(IList<Card> cards)
    {
        if (cards.Count != 3) return false;
        if (cards.Any(c => c.IsJoker)) return false; // jokers not allowed in triples

        var rank = cards[0].Rank;
        if (cards.Any(c => c.Rank != rank)) return false;

        var suits = cards.Select(c => c.Suit).ToList();
        if (suits.Count != suits.Distinct().Count()) return false;

        return true;
    }

    // Jokers fill gaps in sorted order; any excess extend at the ends (winning move only).
    // Direction: Ace-high sequences extend at the LOW end (Joker before the lowest card);
    //            all others extend at the HIGH end (Joker after the highest card).
    // Bounds: Ace-low → cannot extend past King (rank 13).
    //         Non-Ace  → cannot extend past Ace  (rank 14).
    //         Ace-high → cannot extend below rank 2 (rank 1 = Ace already in the combo).
    // Two or more jokers side by side (any internal gap ≥ 2) are also only allowed on a winning move.
    public static bool IsValidSequence(IList<Card> cards, bool allowJokerAtEnds = false)
    {
        if (cards.Count < 3) return false;

        var nonJokers = cards.Where(c => !c.IsJoker).ToList();
        int jokerCount = cards.Count - nonJokers.Count;

        if (nonJokers.Count == 0) return allowJokerAtEnds; // all jokers = valid sequence only on winning move

        var suit = nonJokers[0].Suit;
        if (nonJokers.Any(c => c.Suit != suit)) return false;

        var ranks = nonJokers.Select(c => (int)c.Rank).OrderBy(r => r).ToList();

        if (ranks.Count != ranks.Distinct().Count()) return false;

        int span = ranks[^1] - ranks[0] + 1;
        int gaps = span - ranks.Count;

        // Jokers fill internal gaps; extras extend at the ends (only on a winning move).
        if (gaps <= jokerCount && (span == cards.Count || allowJokerAtEnds))
        {
            // Two or more adjacent jokers (any single internal gap ≥ 2) only allowed on winning move.
            if (!allowJokerAtEnds && HasAdjacentJokers(ranks)) return false;
            if (allowJokerAtEnds && span != cards.Count)
            {
                // Extra jokers extend at the HIGH end first; fall back to LOW end if high end exceeds ceiling.
                // Ace-low (rank 1): ceiling is King (13) — rank 14 would be a second Ace.
                // Non-Ace:          ceiling is Ace (14).
                int extra  = jokerCount - gaps;
                int maxTop = ranks[0] == 1 ? 13 : 14;
                if (ranks[^1] + extra > maxTop)
                {
                    // High-end extension exceeds ceiling; try low-end extension (e.g. JKR JKR K = J Q K).
                    if (ranks[0] - extra < 2) return false;
                }
            }
            return true;
        }

        if (ranks.Contains(1))
        {
            var highRanks = ranks.Select(r => r == 1 ? 14 : r).OrderBy(r => r).ToList();
            int highSpan = highRanks[^1] - highRanks[0] + 1;
            int highGaps = highSpan - highRanks.Count;
            if (highGaps <= jokerCount && (highSpan == cards.Count || allowJokerAtEnds))
            {
                if (!allowJokerAtEnds && HasAdjacentJokers(highRanks)) return false;
                if (allowJokerAtEnds && highSpan != cards.Count)
                {
                    // Ace-high: extra jokers extend at the LOW end only (cannot go past Ace at rank 14).
                    // The lowest valid rank in ace-high context is 2 (rank 1 = Ace is already in the combo).
                    int extra = jokerCount - highGaps;
                    if (highRanks[0] - extra < 2) return false;
                }
                return true;
            }
        }

        return false;
    }

    // Returns true if any consecutive pair of non-joker ranks has a gap ≥ 2,
    // meaning two or more jokers would occupy adjacent positions.
    internal static bool HasAdjacentJokers(List<int> sortedRanks)
    {
        for (int i = 0; i + 1 < sortedRanks.Count; i++)
            if (sortedRanks[i + 1] - sortedRanks[i] - 1 >= 2) return true;
        return false;
    }

    /// <summary>Diagnoses why <paramref name="cards"/> fail to form a valid combination.</summary>
    public static ComboRejection GetRejectionReason(IList<Card> cards, bool allowJokerAtEnds)
    {
        var nonJokers = cards.Where(c => !c.IsJoker).ToList();
        int jokerCount = cards.Count - nonJokers.Count;

        // ── All-joker case ───────────────────────────────────────────────────
        if (nonJokers.Count == 0)
            return ComboRejection.SeqJokerAtEndNotWinning; // only valid when allowJokerAtEnds

        // ── Triple-path diagnostics ──────────────────────────────────────────
        // Jokers are not allowed in triples; but first check if this could be a valid sequence.
        if (jokerCount > 0 && nonJokers.Select(c => c.Rank).Distinct().Count() == 1)
        {
            if (IsValidSequence(cards, allowJokerAtEnds: true))
                return ComboRejection.SeqJokerAtEndNotWinning;
            return ComboRejection.JokerInTriple;
        }

        // No jokers, all same rank → it's a triple attempt.
        if (jokerCount == 0 && nonJokers.Select(c => c.Rank).Distinct().Count() == 1)
        {
            var suits = nonJokers.Select(c => c.Suit).ToList();
            if (suits.Count != suits.Distinct().Count())
                return ComboRejection.TripleDuplicateSuit;
            return ComboRejection.TripleRankMismatch; // shouldn't normally fire for same rank, but covers count/size issues
        }

        // ── Sequence-path diagnostics ────────────────────────────────────────
        // Mixed suits → most common new-user mistake.
        var seqSuits = nonJokers.Select(c => c.Suit).Distinct().ToList();
        if (seqSuits.Count > 1)
            return ComboRejection.SeqMixedSuits;

        // Duplicate ranks within the non-joker cards.
        var ranks = nonJokers.Select(c => (int)c.Rank).ToList();
        if (ranks.Count != ranks.Distinct().Count())
            return ComboRejection.SeqDuplicateRanks;

        // Would be valid with allowJokerAtEnds=true → joker-at-ends restriction.
        if (!allowJokerAtEnds && IsValidSequence(cards, allowJokerAtEnds: true))
        {
            var sortedRanks = ranks.OrderBy(r => r).ToList();
            int span  = sortedRanks[^1] - sortedRanks[0] + 1;
            bool extraJokers = jokerCount > (span - sortedRanks.Count);
            if (extraJokers)
                return ComboRejection.SeqJokerAtEndNotWinning;
            // Check ace-high variant too.
            if (ranks.Contains(1))
            {
                var highRanks = ranks.Select(r => r == 1 ? 14 : r).OrderBy(r => r).ToList();
                int highSpan = highRanks[^1] - highRanks[0] + 1;
                bool highExtra = jokerCount > (highSpan - highRanks.Count);
                if (highExtra) return ComboRejection.SeqJokerAtEndNotWinning;
            }
            return ComboRejection.SeqAdjacentJokersNotWinning;
        }

        return ComboRejection.SeqGapTooLarge;
    }

    public static CombinationType? Classify(IList<Card> cards, bool allowJokerAtEnds = false)
    {
        if (IsValidTriple(cards)) return CombinationType.Triple;
        if (IsValidSequence(cards, allowJokerAtEnds)) return CombinationType.Sequence;
        return null;
    }

    public static bool CanAcceptCard(Combination combo, Card card)
    {
        return combo.CanAccept(card);
    }

    public static bool CanAddToSequence(IList<Card> existingCards, Card newCard)
    {
        var testCards = existingCards.Concat(new[] { newCard }).ToList();
        return IsValidSequence(testCards);
    }
}
