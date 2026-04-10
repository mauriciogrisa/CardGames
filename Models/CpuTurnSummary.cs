namespace CardGames.Models;

public class CpuTurnSummary
{
    public bool DrewFromDiscard { get; set; }
    public Card? DrawnCard { get; set; }
    public Card? FirstDiscarded { get; set; }  // set when CPU uses the second-draw discard
    public List<(List<Card> Cards, CombinationType Type)> LaidDown { get; } = new();
    public List<(Card Card, CombinationType ComboType)> AddedToTable { get; } = new();
    public List<Card> SwappedJokers { get; } = new();   // cards placed to take a joker from the table
    public int PositionalSwapCount { get; set; }        // adds that displaced a joker to a new position (joker stays in combo)
    public List<(int ComboIndex, Card Card)> TripleDiscardsPending { get; } = new();
    public Card? Discarded { get; set; }
}
