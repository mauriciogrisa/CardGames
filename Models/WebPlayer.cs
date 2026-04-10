namespace CardGames.Models;

public class WebPlayer : Player
{
    public WebPlayer(string id, string name) : base(id, name) { }
    public override void TakeTurn(GameState state) { }
}
