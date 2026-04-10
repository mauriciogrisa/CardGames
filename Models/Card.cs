namespace CardGames.Models;

public class Card
{
    private const int AceJokerPoints = 15;
    private const int HighCardPoints = 10;

    public Card(Rank rank, Suit suit) { Rank = rank; Suit = suit; }
    public Rank Rank { get; }
    public Suit Suit { get; }

    public bool IsJoker => Rank == Rank.Joker;

    public int PointValue => Rank switch
    {
        Rank.Joker => AceJokerPoints,
        Rank.Ace   => AceJokerPoints,
        Rank.Two   => 2,
        Rank.Three => 3,
        Rank.Four  => 4,
        Rank.Five  => 5,
        Rank.Six   => 6,
        Rank.Seven => 7,
        Rank.Eight => 8,
        Rank.Nine  => 9,
        Rank.Ten   => HighCardPoints,
        Rank.Jack  => HighCardPoints,
        Rank.Queen => HighCardPoints,
        Rank.King  => HighCardPoints,
        _ => 0
    };

    public string DisplayName
    {
        get
        {
            if (IsJoker) return "JKR";
            var rankStr = Rank switch
            {
                Rank.Ace => "A",
                Rank.Two => "2",
                Rank.Three => "3",
                Rank.Four => "4",
                Rank.Five => "5",
                Rank.Six => "6",
                Rank.Seven => "7",
                Rank.Eight => "8",
                Rank.Nine => "9",
                Rank.Ten => "10",
                Rank.Jack => "J",
                Rank.Queen => "Q",
                Rank.King => "K",
                _ => "?"
            };
            var suitStr = Suit switch
            {
                Suit.Clubs => "♣",
                Suit.Diamonds => "♦",
                Suit.Hearts => "♥",
                Suit.Spades => "♠",
                _ => "?"
            };
            return rankStr + suitStr;
        }
    }

    public override string ToString() => DisplayName;
}
