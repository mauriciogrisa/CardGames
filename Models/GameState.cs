namespace CardGames.Models;

public class GameState
{
    public GameState(Deck deck, TableState table, Player[] players)
    {
        Deck = deck;
        Table = table;
        Players = players;
        CurrentPlayerIndex = 0;
        RoundOver = false;
        RoundWinnerId = null;
        RoundNumber = 1;
    }

    public Deck Deck { get; }
    public TableState Table { get; }
    public Player[] Players { get; }
    public int CurrentPlayerIndex { get; set; }
    public bool RoundOver { get; set; }
    public string? RoundWinnerId { get; set; }
    public int RoundNumber { get; set; }

    public Player CurrentPlayer => Players[CurrentPlayerIndex];

    public void FlipCurrentPlayer()
    {
        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Length;
    }
}
