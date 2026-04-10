namespace CardGames.Models;

public class TableState
{
    private readonly List<Combination> _combinations = new();

    public IReadOnlyList<Combination> Combinations => _combinations;

    public void AddCombination(Combination combination)
    {
        _combinations.Add(combination);
    }

    public void AddCardToCombination(int index, Card card, bool isWinningMove = false)
    {
        if (index < 0 || index >= _combinations.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _combinations[index].AddCard(card, isWinningMove);
    }

    public void AddAllCardsToCombination(int index, IReadOnlyList<Card> cards, bool isWinningMove = false)
    {
        if (index < 0 || index >= _combinations.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _combinations[index].AddCards(cards, isWinningMove);
    }

    // Reorders _combinations[startIdx..] to match newOrder (which lists the original indices).
    // Used by the animation layer to ensure new combos appear on screen in phase order.
    public void ReorderNewCombinations(int startIdx, IReadOnlyList<int> newOrder)
    {
        var reordered = newOrder.Select(i => _combinations[i]).ToList();
        for (int i = 0; i < reordered.Count; i++)
            _combinations[startIdx + i] = reordered[i];
    }

    public void Clear() => _combinations.Clear();
}
