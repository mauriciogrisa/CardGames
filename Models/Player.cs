namespace CardGames.Models;

public abstract class Player
{
    protected Player(string id, string name)
    {
        Id = id;
        Name = name;
        Score = 0;
        Hand = new Hand();
    }

    public string Id { get; }
    public string Name { get; }
    public int Score { get; set; }
    public Hand Hand { get; }

    // ── Per-turn constraint tracking ─────────────────────────────────────────
    // Set by TurnService; cleared by TurnService when the card is used in a combo.

    // Joker obtained from a table sequence via swap — must be played into a combo before discarding.
    public Card? SwappedJoker { get; set; }

    // Card drawn from the discard pile — must be used in a combo before discarding.
    public Card? DiscardDrawnCard { get; set; }

    // Clears per-turn constraints for any of the given cards that were just played.
    public void ClearConstraintsIfUsed(IEnumerable<Card> playedCards)
    {
        if (DiscardDrawnCard != null &&
            playedCards.Contains(DiscardDrawnCard, ReferenceEqualityComparer.Instance))
            DiscardDrawnCard = null;

        if (SwappedJoker != null &&
            playedCards.Contains(SwappedJoker, ReferenceEqualityComparer.Instance))
            SwappedJoker = null;
    }

    // Called at the start of each round to wipe any leftover state.
    public void ResetTurnConstraints()
    {
        SwappedJoker = null;
        DiscardDrawnCard = null;
    }

    public abstract void TakeTurn(GameState state);
}
