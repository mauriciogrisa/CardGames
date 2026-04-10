using CardGames.Services;

namespace CardGames.Models;

public class CpuPlayer : Player
{
    private readonly AiDecisionService _ai;

    public CpuPlayer(AiDecisionService ai, string id, string name)
        : base(id, name)
    {
        _ai = ai;
    }

    // How many consecutive turns this CPU held a complete combo without laying it down.
    // Resets at round start and whenever the CPU lays down. Used to force a lay-down
    // after too many turns of suppression (see AiDecisionService.ExecuteCpuPlayStep).
    public int TurnsHoldingCompleteCombo { get; set; }

    public override void TakeTurn(GameState state)
    {
        _ai.ExecuteCpuTurn(state, this);
    }
}
