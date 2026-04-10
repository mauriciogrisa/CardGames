using CardGames.Models;

namespace CardGames.Services;

public static class ScoringService
{
    public static void ApplyRoundScores(GameState state)
    {
        foreach (var player in state.Players)
        {
            if (player.Id == state.RoundWinnerId)
                continue;

            // Special rule: player at 98 pts with a single Ace takes only 1 pt (goes to 99, not eliminated).
            int penalty = player.Score == 98
                          && player.Hand.Count == 1
                          && player.Hand.Cards[0].Rank == Rank.Ace
                ? 1
                : player.Hand.TotalPoints;

            player.Score += penalty;
        }
    }

    public static Player? GetGameWinner(Player[] players)
    {
        // Anyone who reaches 100+ is eliminated; last one under 100 wins
        var remaining = players.Where(p => p.Score < 100).ToList();
        return remaining.Count == 1 ? remaining[0] : null;
    }
}
