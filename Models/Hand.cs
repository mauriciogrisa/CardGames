namespace CardGames.Models;

public class Hand
{
    private readonly List<Card> _cards = new();

    public IReadOnlyList<Card> Cards => _cards;
    public int Count => _cards.Count;
    public int TotalPoints => _cards.Sum(c => c.PointValue);

    public void Add(Card card) => _cards.Add(card);

    public int IndexOf(Card card) => _cards.IndexOf(card);

    public void Insert(int index, Card card) => _cards.Insert(Math.Clamp(index, 0, _cards.Count), card);

    public bool Remove(Card card) => _cards.Remove(card);

    public void Clear() => _cards.Clear();

    // insertBefore = position to insert at (0 = before first, Count = after last)
    public void MoveCard(int fromIndex, int insertBefore)
    {
        if (fromIndex < 0 || fromIndex >= _cards.Count) return;
        insertBefore = Math.Clamp(insertBefore, 0, _cards.Count);
        // no-op: card is already at that slot
        if (insertBefore == fromIndex || insertBefore == fromIndex + 1) return;
        var card = _cards[fromIndex];
        _cards.RemoveAt(fromIndex);
        // after removal, shift target if needed
        int insertAt = insertBefore > fromIndex ? insertBefore - 1 : insertBefore;
        _cards.Insert(insertAt, card);
    }
}
