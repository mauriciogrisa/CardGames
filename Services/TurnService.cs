using CardGames.Models;

namespace CardGames.Services;

public static class TurnService
{
    public static Card ExecuteDraw(GameState state, bool fromDiscard)
    {
        Card card;
        if (fromDiscard)
            card = state.Deck.TakeFromDiscard();
        else
            card = state.Deck.Draw();

        state.CurrentPlayer.Hand.Add(card);
        if (fromDiscard)
            state.CurrentPlayer.DiscardDrawnCard = card;
        return card;
    }

    public static Combination ExecuteLayDown(GameState state, List<Card> cards, bool allowJokerAtEnds = false)
    {
        var player = state.CurrentPlayer;
        var type = CombinationValidator.Classify(cards, allowJokerAtEnds)
            ?? throw new InvalidOperationException("Invalid combination.");

        foreach (var card in cards)
            player.Hand.Remove(card);

        player.ClearConstraintsIfUsed(cards);

        var combo = new Combination(cards, type, player.Id) { IsWinningLaydown = player.Hand.Count <= 1 };
        state.Table.AddCombination(combo);
        return combo;
    }

    public static void ExecuteAddToTable(GameState state, int comboIndex, Card card, bool isWinningMove = false)
    {
        var player = state.CurrentPlayer;
        state.Table.AddCardToCombination(comboIndex, card, isWinningMove);
        player.Hand.Remove(card);
        player.ClearConstraintsIfUsed(new[] { card });
        if (player.Hand.Count <= 1)
            state.Table.Combinations[comboIndex].IsWinningAddition = true;
    }

    // Atomically adds multiple cards to a single combination (validated as a whole by CanAcceptAll).
    public static void ExecuteAddAllToTable(GameState state, int comboIndex, IReadOnlyList<Card> cards, bool isWinningMove = false)
    {
        var player = state.CurrentPlayer;
        state.Table.AddAllCardsToCombination(comboIndex, cards, isWinningMove);
        foreach (var card in cards)
        {
            player.Hand.Remove(card);
            player.ClearConstraintsIfUsed(new[] { card });
        }
        if (player.Hand.Count <= 1)
            state.Table.Combinations[comboIndex].IsWinningAddition = true;
    }

    // Places 'replacement' on a table sequence in exchange for the joker it covers; returns the joker.
    public static Card ExecuteSwapJoker(GameState state, int comboIndex, Card replacement)
    {
        var player = state.CurrentPlayer;
        var joker = state.Table.Combinations[comboIndex].SwapJoker(replacement);
        int pos = player.Hand.IndexOf(replacement);
        player.Hand.Remove(replacement);
        player.Hand.Insert(pos, joker);
        player.ClearConstraintsIfUsed(new[] { replacement });
        player.SwappedJoker = joker;
        return joker;
    }

    public static void ExecuteDiscard(GameState state, Card card, bool swapForfeited = false)
    {
        var player = state.CurrentPlayer;

        // ── Hard constraints — apply to every player without exception ────────
        if (card.IsJoker)
            throw new InvalidOperationException("Jokers cannot be discarded.");

        // Winning discard (last card in hand) bypasses the swap obligation — forcing a swap
        // when there are no other cards would leave the player holding an undiscardable joker.
        // swapForfeited=true means the caller already warned the player once (first attempt was
        // blocked and the card added to _forfeitedSwaps); the second attempt is allowed per rules.
        if (!swapForfeited && player.Hand.Count > 1 && state.Table.Combinations.Any(c => c.CanReplaceJoker(card)))
            throw new InvalidOperationException($"Card {card} must be used to swap a table joker before discarding.");

        if (player.SwappedJoker != null && player.Hand.Cards.Contains(player.SwappedJoker))
            throw new InvalidOperationException("The swapped joker must be used in a combo before discarding.");

        if (player.DiscardDrawnCard != null)
            throw new InvalidOperationException("The card drawn from the discard pile must be used in a combo before discarding.");
        // ─────────────────────────────────────────────────────────────────────

        player.Hand.Remove(card);
        state.Deck.AddToDiscard(card);

        if (player.Hand.Count == 0)
        {
            state.RoundOver = true;
            state.RoundWinnerId = player.Id;
        }
    }
}
