using CardGames.Models;

namespace CardGames.Services;

public class CombinationFinder
{
    public List<List<Card>> FindAllTriples(IReadOnlyList<Card> hand)
    {
        var results = new List<List<Card>>();
        var jokers = hand.Where(c => c.IsJoker).ToList();
        var nonJokers = hand.Where(c => !c.IsJoker).ToList();

        var byRank = nonJokers.GroupBy(c => c.Rank);

        foreach (var rankGroup in byRank)
        {
            var cardsOfRank = rankGroup.ToList();
            var distinctSuits = cardsOfRank.Select(c => c.Suit).Distinct().ToList();

            if (distinctSuits.Count >= 3)
            {
                var selectedCards = new List<Card>(3);
                for (int i = 0; i < 3; i++)
                    selectedCards.Add(cardsOfRank.First(c => c.Suit == distinctSuits[i]));
                results.Add(selectedCards);
            }
        }

        return results;
    }

    public List<List<Card>> FindAllSequences(IReadOnlyList<Card> hand, bool allowJokerAtEnds = false)
    {
        var results = new List<List<Card>>();
        var jokers = hand.Where(c => c.IsJoker).ToList();
        var nonJokers = hand.Where(c => !c.IsJoker).ToList();

        var bySuit = nonJokers.GroupBy(c => c.Suit);

        // Each joker can fill one gap in the non-joker subset, so minSubset = 3 - jokers.Count.
        // Floor is 2 for normal play (need 2 anchors to position joker in the middle);
        // floor is 1 only for winning moves where jokers-at-ends are permitted.
        int minSubset = allowJokerAtEnds
            ? Math.Max(1, 3 - jokers.Count)
            : Math.Max(2, 3 - jokers.Count);

        foreach (var suitGroup in bySuit)
        {
            var suitCards = suitGroup.OrderBy(c => (int)c.Rank).ToList();

            for (int start = 0; start < suitCards.Count; start++)
            {
                for (int end = start + (minSubset - 1); end < suitCards.Count; end++)
                {
                    var subset = suitCards.GetRange(start, end - start + 1);
                    var subRanks = subset.Select(c => (int)c.Rank).OrderBy(r => r).ToList();
                    int span = subRanks[^1] - subRanks[0] + 1;
                    int gaps = span - subset.Count;

                    if (gaps < 0 || gaps > jokers.Count) continue;

                    // Generate all C(jokers.Count, gaps) assignments so sequences competing
                    // for jokers get distinct Card references and don't block each other in
                    // the greedy set-cover inside FindBestCombinationSet.
                    foreach (var jokerSet in JokerSubsets(jokers, gaps))
                    {
                        var combo = new List<Card>(subset);
                        combo.AddRange(jokerSet);
                        if (CombinationValidator.IsValidSequence(combo, allowJokerAtEnds))
                            results.Add(combo);
                    }

                    // With allowJokerAtEnds: also try adding extra jokers at the ends
                    if (allowJokerAtEnds)
                    {
                        for (int extra = 1; extra <= jokers.Count - gaps; extra++)
                        {
                            foreach (var jokerSet in JokerSubsets(jokers, gaps + extra))
                            {
                                var combo = new List<Card>(subset);
                                combo.AddRange(jokerSet);
                                if (CombinationValidator.IsValidSequence(combo, true))
                                    results.Add(combo);
                            }
                        }
                    }
                }
            }
        }

        return results;
    }

    // All size-`count` subsets of `jokers` preserving reference identity.
    // Iterative (no recursion) to avoid stack overflow with recursive generators.
    private static List<List<Card>> JokerSubsets(List<Card> jokers, int count)
    {
        var results = new List<List<Card>>();
        if (count <= 0) { if (count == 0) results.Add(new List<Card>()); return results; }
        if (count > jokers.Count) return results;

        // Standard C(n,k) index-walk
        var idx = new int[count];
        for (int i = 0; i < count; i++) idx[i] = i;

        while (true)
        {
            var combo = new List<Card>(count);
            for (int i = 0; i < count; i++) combo.Add(jokers[idx[i]]);
            results.Add(combo);

            // Advance to next combination
            int pos = count - 1;
            while (pos >= 0 && idx[pos] == jokers.Count - count + pos) pos--;
            if (pos < 0) break;
            idx[pos]++;
            for (int i = pos + 1; i < count; i++) idx[i] = idx[pos] + (i - pos);
        }

        return results;
    }

    public List<List<Card>> FindBestCombinationSet(IReadOnlyList<Card> hand, bool allowJokerAtEnds = false)
    {
        var allCombos = FindAllTriples(hand);
        allCombos.AddRange(FindAllSequences(hand, allowJokerAtEnds));

        if (allCombos.Count == 0) return new List<List<Card>>();

        var best = new List<List<Card>>();
        var usedCards = new HashSet<Card>();

        // Primary: maximize cards covered. Secondary: prefer laying down higher-value cards
        // so the remaining hand carries less penalty when the round ends unexpectedly.
        allCombos.Sort((a, b) =>
        {
            int countCmp = b.Count.CompareTo(a.Count);
            if (countCmp != 0) return countCmp;
            return b.Sum(c => c.PointValue).CompareTo(a.Sum(c => c.PointValue));
        });

        foreach (var combo in allCombos)
        {
            if (combo.All(c => !usedCards.Contains(c)))
            {
                best.Add(combo);
                foreach (var c in combo)
                    usedCards.Add(c);
            }
        }

        return best;
    }

    // Exhaustive backtracking search: returns a partition of ALL cards into valid combos,
    // or null if no such partition exists. Used by VeryHard CPU to find winning moves
    // that the greedy FindBestCombinationSet misses.
    public List<List<Card>>? TryPartitionAll(IReadOnlyList<Card> hand, bool allowJokerAtEnds = false)
    {
        if (hand.Count < 3) return null;
        var cards = hand.ToList();
        return PartitionHelper(cards, new bool[cards.Count], allowJokerAtEnds);
    }

    private static List<List<Card>>? PartitionHelper(List<Card> all, bool[] used, bool allowJokerAtEnds)
    {
        // Find first free index to anchor the next combo
        int first = -1;
        for (int i = 0; i < all.Count; i++) if (!used[i]) { first = i; break; }
        if (first == -1) return new List<List<Card>>(); // all cards placed — success

        var free = new List<int>(all.Count);
        for (int i = 0; i < all.Count; i++) if (!used[i]) free.Add(i);
        if (free.Count < 3) return null; // leftover cards can't form a valid combo

        for (int size = 3; size <= free.Count; size++)
        {
            int leftover = free.Count - size;
            if (leftover > 0 && leftover < 3) continue; // remaining couldn't form a combo
            foreach (var indices in IndexSubsets(free, size, mustInclude: first))
            {
                var subset = indices.Select(i => all[i]).ToList();
                if (CombinationValidator.Classify(subset, allowJokerAtEnds) == null) continue;
                foreach (var i in indices) used[i] = true;
                var rest = PartitionHelper(all, used, allowJokerAtEnds);
                if (rest != null) { rest.Insert(0, subset); return rest; }
                foreach (var i in indices) used[i] = false;
            }
        }
        return null;
    }

    // Yields all size-`size` subsets of `src` that include `mustInclude` as the first element.
    // Anchoring on the first free card prunes the search space significantly.
    private static IEnumerable<List<int>> IndexSubsets(List<int> src, int size, int mustInclude)
    {
        // mustInclude must be first element; pick the remaining (size-1) from elements after it
        int pos = src.IndexOf(mustInclude);
        if (pos < 0 || src.Count - pos < size) yield break;
        var rest = src.GetRange(pos + 1, src.Count - pos - 1);
        foreach (var tail in IndexSubsetsUnconstrained(rest, size - 1))
        {
            var r = new List<int>(size) { mustInclude };
            r.AddRange(tail);
            yield return r;
        }
    }

    private static IEnumerable<List<int>> IndexSubsetsUnconstrained(List<int> src, int size)
    {
        if (size == 0) { yield return new List<int>(); yield break; }
        for (int i = 0; i <= src.Count - size; i++)
            foreach (var tail in IndexSubsetsUnconstrained(src.GetRange(i + 1, src.Count - i - 1), size - 1))
            {
                var r = new List<int>(size) { src[i] };
                r.AddRange(tail);
                yield return r;
            }
    }

    public HashSet<Card> FindNearCombinationCards(IReadOnlyList<Card> hand)
    {
        var protectedCards = new HashSet<Card>();

        // Jokers are always protected — they're wildcards valuable in any sequence
        foreach (var joker in hand.Where(c => c.IsJoker))
            protectedCards.Add(joker);

        var nonJokers = hand.Where(c => !c.IsJoker).ToList();

        var byRank = nonJokers.GroupBy(c => c.Rank);
        foreach (var rankGroup in byRank)
        {
            var distinctSuits = rankGroup.Select(c => c.Suit).Distinct().ToList();
            if (distinctSuits.Count >= 2)
            {
                foreach (var card in rankGroup)
                    protectedCards.Add(card);
            }
        }

        var bySuit = nonJokers.GroupBy(c => c.Suit);
        foreach (var suitGroup in bySuit)
        {
            var sorted = suitGroup.OrderBy(c => (int)c.Rank).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                int rankDiff = (int)sorted[i + 1].Rank - (int)sorted[i].Rank;
                if (rankDiff <= 2)
                {
                    protectedCards.Add(sorted[i]);
                    protectedCards.Add(sorted[i + 1]);
                }
            }
        }

        return protectedCards;
    }
}
