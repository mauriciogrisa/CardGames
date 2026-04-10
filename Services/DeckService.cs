using CardGames.Models;

namespace CardGames.Services;

public static class DeckService
{
    public static Deck BuildFullDeck(Random rng)
    {
        var cards = new List<Card>();

        // 2 standard 52-card decks
        for (int d = 0; d < 2; d++)
        {
            foreach (Suit suit in Enum.GetValues<Suit>())
            {
                foreach (Rank rank in Enum.GetValues<Rank>())
                {
                    if (rank == Rank.Joker) continue;
                    cards.Add(new Card(rank, suit));
                }
            }
        }

        // 4 Jokers (wildcards)
        for (int i = 0; i < 4; i++)
            cards.Add(new Card(Rank.Joker, Suit.Clubs));

        // Shuffle
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return new Deck(cards, rng);
    }

    public static void DealCards(Deck deck, Player[] players, int cardsEach = 9)
    {
        foreach (var player in players)
            player.Hand.Clear();

        for (int i = 0; i < cardsEach; i++)
        {
            foreach (var player in players)
                player.Hand.Add(deck.Draw());
        }
    }
}
