namespace CardGames.Models;

public class Deck
{
    private readonly Random _rng;
    private List<Card> _drawPile;
    private List<Card> _discardPile;

    public Deck(List<Card> cards, Random rng)
    {
        _rng = rng;
        _drawPile = cards;
        _discardPile = new List<Card>();
    }

    public int DrawPileCount => _drawPile.Count;
    public int DiscardPileCount => _discardPile.Count;
    public Card? DiscardTop => _discardPile.Count > 0 ? _discardPile[^1] : null;
    public IReadOnlyList<Card> DrawPileCards => _drawPile;

    // Returns up to n cards from the top of the discard pile (top card last)
    public IReadOnlyList<Card> DiscardPeek(int n)
    {
        int count = Math.Min(n, _discardPile.Count);
        return _discardPile.GetRange(_discardPile.Count - count, count);
    }

    // Return a card to the bottom of the draw pile (used when a discard-pile draw is cancelled)
    public void ReturnToDeck(Card card) => _drawPile.Insert(0, card);

    // Place a card on top of the draw pile so it will be drawn next (for testing only).
    internal void AddToDrawTop(Card card) => _drawPile.Add(card);

    public Card Draw()
    {
        if (_drawPile.Count == 0)
            Reshuffle();

        var card = _drawPile[^1];
        _drawPile.RemoveAt(_drawPile.Count - 1);
        return card;
    }

    public Card TakeFromDiscard()
    {
        if (_discardPile.Count == 0)
            throw new InvalidOperationException("Discard pile is empty.");
        var card = _discardPile[^1];
        _discardPile.RemoveAt(_discardPile.Count - 1);
        return card;
    }

    public void AddToDiscard(Card card)
    {
        _discardPile.Add(card);
    }

    private void Reshuffle()
    {
        if (_discardPile.Count == 0)
            throw new InvalidOperationException("Both piles are empty.");

        var top = _discardPile[^1];
        _discardPile.RemoveAt(_discardPile.Count - 1);
        _drawPile = _discardPile;
        _discardPile = new List<Card> { top };
        Shuffle();
    }

    private void Shuffle()
    {
        for (int i = _drawPile.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_drawPile[i], _drawPile[j]) = (_drawPile[j], _drawPile[i]);
        }
    }
}
