using CardGames.Models;

namespace CardGames.Services;

public enum AppLanguage { English, Portuguese }

public class LanguageService
{
    public AppLanguage Language { get; private set; } = AppLanguage.English;

    public void Set(AppLanguage lang) => Language = lang;

    private bool Pt => Language == AppLanguage.Portuguese;

    // ── Welcome / Game Over ───────────────────────────────────────────────
    public string YouName      => Pt ? "Você"         : "You";
    public string StartGame    => Pt ? "Iniciar Jogo" : "Start Game";
    public string NewGame      => Pt ? "Novo Jogo"    : "New Game";
    public string Simulate     => Pt ? "Simular"      : "Simulate";
    public string GameOver     => Pt ? "Fim de Jogo"  : "Game Over";
    public string Subtitle     => Pt
        ? "Jogo de cartas para 2 a 8 jogadores · Quem chegar a 100 pontos explode · O último vence"
        : "A card game for 2 to 8 players · First to 100 points explodes · Last one standing wins";
    public string AiLabel        => Pt ? "IA"            : "AI";
    public string CpuCountLabel  => Pt ? "Jogadores IA"  : "AI Players";
    public string YourNameLabel  => Pt ? "Seu nome"      : "Your name";
    public string YourNamePlaceholder => Pt ? "Você"     : "You";
    public string GameWins(string name) => Pt ? $"{name} ganhou o jogo!" : $"{name} wins the game!";
    public string YouWinGame  => Pt ? "Você ganhou o jogo! 🏆" : "You win the game! 🏆";
    public string YouLoseGame(string name) => Pt ? $"Você perdeu! {name} ganhou o jogo." : $"You lost! {name} wins the game.";
    public string Points      => Pt ? "pontos" : "points";
    public string YouScoreLabel(int n)  => Pt ? $"Você: {n}/100"  : $"You: {n}/100";

    // ── Header ────────────────────────────────────────────────────────────
    public string Round        => Pt ? "Rodada"     : "Round";
    public string YouPts(int n)              => n >= 100 ? (Pt ? "Você: fora"      : "You: out")        : (Pt ? $"Você: {n}"    : $"You: {n}");
    public string CpuPts(string name, int n) => n >= 100 ? (Pt ? $"{name}: fora"  : $"{name}: out")    : $"{name}: {n}";
    public string ScoreDisplay(int n)        => n >= 100 ? (Pt ? "fora" : "out") : n.ToString();
    public string FirstPlayer(string name, bool isMainPlayer) => isMainPlayer
        ? (Pt ? "Primeiro: Você"  : "First: You")
        : (Pt ? $"Primeiro: {name}" : $"First: {name}");

    // ── Sections ──────────────────────────────────────────────────────────
    public string CpuHand(string name, int n) => name;
    public string YourHand(int n) => Pt ? "Sua mão" : "Your Hand";
    public string TableLabel   => Pt ? "Mesa"                    : "Table";
    public string EmptyTable   => Pt ? "Sem combinações na mesa" : "No combinations on the table yet";

    // ── Combo labels ──────────────────────────────────────────────────────
    public string Yours        => Pt ? "Sua" : "Yours";
    public string TypeName(CombinationType t) => t == CombinationType.Sequence
        ? (Pt ? "Sequência" : "Sequence")
        : (Pt ? "Trinca"    : "Triple");

    // ── Piles ─────────────────────────────────────────────────────────────
    public string Deck         => Pt ? "Baralho"  : "Deck";
    public string DiscardPile  => Pt ? "Descarte" : "Discard";
    public string Empty        => Pt ? "Pilha de Descarte" : "Discard Pile";

    // ── Hints & action buttons ────────────────────────────────────────────
    public string HintDraw     => Pt ? "Clique em uma pilha para comprar uma carta" : "Click a pile to draw a card";
    public string HintCpu(string name) => Pt ? $"{name} jogando..." : $"{name} is playing...";
    public string NextRound    => Pt ? "Próxima Rodada"      : "Next Round";
    public string ReturnCard   => Pt ? "Devolver Carta"      : "Return Card";
    public string LayDown(int n) => Pt ? $"Baixar ({n})"    : $"Lay Down ({n})";
    public string DiscardOne      => Pt ? "Descartar (1)"   : "Discard (1)";

    // ── Round-over display ────────────────────────────────────────────────
    public string YouLost(int n)   => Pt ? $"Penalidade Você: +{n} pts"  : $"Your penalty: +{n} pts";
    public string CpuLost(string name, int n)   => Pt ? $"Penalidade {name}: +{n} pts"   : $"{name} penalty: +{n} pts";
    public string ScoresYou(int n) => Pt ? $"Pontuações → Você: {n}/100" : $"Scores → You: {n}/100";
    public string ScoresCpu(string name, int n) => $"{name}: {n}/100";

    // ── Game messages (set inside WebGameService) ─────────────────────────
    public string MsgYouFirst        => Pt ? "Você começa — compre uma carta."  : "You go first — draw a card.";
    public string MsgCpuFirst(string name) => Pt ? $"{name} começa — jogando..." : $"{name} goes first — playing...";
    public string MsgDrewCanSecond   => Pt
        ? "Você comprou do baralho. Descarte para comprar uma segunda carta, ou jogue sua mão."
        : "Drew from deck. Discard it to draw a second card, or play your hand.";
    public string MsgDrewNormal      => Pt
        ? "Você comprou do baralho. Selecione cartas para baixar, adicionar a combinações, ou descartar."
        : "Drew from deck. Select cards to lay down, add to combos, or discard.";
    public string MsgTookDiscard     => Pt
        ? "Você pegou do descarte. Deve usar esta carta em uma combinação."
        : "Took from discard. You must use this card in a combination.";
    public string MsgReturnedCard    => Pt
        ? "Carta devolvida ao baralho. Compre do baralho."
        : "Card returned to the deck. Draw from the deck.";
    public string MsgFirstDiscarded  => Pt
        ? "Primeira carta descartada. Agora compre a segunda carta do baralho."
        : "First card discarded. Now draw your second card from the deck.";
    public string MsgLaidDown(int n) => Pt ? $"Você baixou {n} cartas."          : $"Laid down {n} cards.";
    public string MsgAddedToCombo    => Pt ? "Carta adicionada à combinação."    : "Added card to combination.";
    public string MsgYourTurn        => Pt ? "Sua vez — compre uma carta."       : "Your turn — draw a card.";
    public string MsgCpuDrewDeck(string name)    => Pt ? $"{name} comprou do baralho."    : $"{name} drew from the deck.";
    public string MsgCpuDrewDiscard(string name) => Pt ? $"{name} comprou do descarte."   : $"{name} drew from the discard pile.";
    public string MsgCpuSecondDraw(string name)  => Pt ? $"{name} usou o segundo comprar." : $"{name} used second draw.";
    public string MsgCpuLaidDown(string name, int n) => Pt
        ? $"{name} baixou {n} carta{(n == 1 ? "" : "s")}!"
        : $"{name} laid down {n} card{(n == 1 ? "" : "s")}!";
    public string MsgCpuAddedToTable(string name) => Pt ? $"{name} adicionou à mesa."     : $"{name} added to a combo.";
    public string MsgCpuSwappedJoker(string name) => Pt ? $"{name} trocou um curinga."    : $"{name} swapped a joker.";
    public string MsgRoundWinner(string winner, string nextFirst) => Pt
        ? $"{winner} ganhou a rodada! {nextFirst} começa na próxima rodada."
        : $"{winner} won the round! {nextFirst} goes first next round.";
    public string MsgRoundOver(string nextFirst) => Pt
        ? $"Rodada terminada! {nextFirst} começa na próxima rodada."
        : $"Round over! {nextFirst} goes first next round.";
    public string MsgRoundOverFinal => Pt ? "Rodada terminada!" : "Round over!";
    public string MsgGameWinner(string name) => Pt ? $"{name} ganhou o jogo!" : $"{name} wins the game!";

    // ── CPU turn log ──────────────────────────────────────────────────────
    public string CpuLastPlay(string name) => Pt ? $"Última jogada de {name}:" : $"{name}'s last play:";
    public string DrewFromDeckLabel   => Pt ? "Comprou do baralho"      : "Drew from deck";
    public string DrewFromDiscardLabel => Pt ? "Comprou do descarte"    : "Drew from discard";
    public string LaidDownLabel       => Pt ? "Baixou"                  : "Laid down";
    public string AddedToComboLabel   => Pt ? "Adicionou à combinação"  : "Added to combo";
    public string DiscardedLabel      => Pt ? "Descartou"               : "Discarded";

    // ── Simulation ────────────────────────────────────────────────────────
    public string SkipSimBtn => Pt ? "Pular simulação" : "Skip simulation";

    // ── About modal ───────────────────────────────────────────────────────
    public string AboutBtn        => Pt ? "Sobre"    : "About";
    public string AboutTitle      => Pt ? "Sobre o Pontinho" : "About Pontinho";
    public string AboutCreatedBy  => Pt ? "Criado por" : "Created by";
    public string AboutSourceCode => Pt ? "Código-fonte" : "Source code";
    public string AboutLicenseTitle => Pt ? "Licença" : "License";
    public string AboutLicenseBody => Pt
        ? "Este software é distribuído sob a Licença MIT. Você é livre para copiar, modificar e distribuir este software, desde que os créditos ao autor sejam mantidos."
        : "This software is distributed under the MIT License. You are free to copy, modify and distribute it, provided that credit is given to the original author.";
    public string AboutClose      => Pt ? "Fechar"   : "Close";

    // ── Quit confirmation ─────────────────────────────────────────────────
    public string QuitBtn          => Pt ? "Sair"      : "Quit";
    public string QuitConfirmTitle => Pt ? "Sair do jogo?" : "Quit game?";
    public string QuitConfirmMsg   => Pt ? "Seu progresso será perdido." : "Your progress will be lost.";
    public string QuitConfirmYes   => Pt ? "Sair"      : "Quit";
    public string QuitConfirmNo    => Pt ? "Cancelar"  : "Cancel";

    // ── Rules modal ───────────────────────────────────────────────────────
    public string RulesBtn   => Pt ? "Regras"      : "Rules";
    public string RulesTitle => Pt ? "Como Jogar"  : "How to Play";
    public string RulesClose => Pt ? "Fechar"      : "Close";

    public string RulesDeckTitle   => Pt ? "O Baralho"         : "The Deck";
    public string RulesDeckBody    => Pt
        ? "2 baralhos completos + 4 Curingas = 108 cartas. São distribuídas 9 cartas a cada jogador no início de cada rodada."
        : "2 full decks + 4 Jokers = 108 cards. Each player is dealt 9 cards at the start of each round.";

    public string RulesCombosTitle  => Pt ? "Combinações Válidas"  : "Valid Combinations";
    public string RulesCombosBody   => Pt
        ? "Trinca: 3 ou mais cartas do mesmo valor em naipes diferentes (máx. 6, um por naipe).\nSequência: 3 ou mais cartas do mesmo naipe em ordem consecutiva.\nVocê pode baixar várias combinações de uma vez e adicionar cartas avulsas a combinações já na mesa.\nQueimar: adicionar uma carta de naipe repetido a uma Trinca. A carta queimada fica visível durante o turno mas vai para o descarte ao final."
        : "Triple: 3 or more cards of the same rank in different suits (max 6, one per suit).\nSequence: 3 or more cards of the same suit in consecutive order.\nYou may lay down multiple combos at once and add individual cards to existing table combos.\nBurn (Queimar): add a duplicate-suit card to a Triple. The burned card stays visible during your turn but is moved to the discard pile at turn end.";

    public string RulesJokersTitle  => Pt ? "Curingas"  : "Jokers";
    public string RulesJokersBody   => Pt
        ? "Curingas apenas em sequências. Não podem ficar nas pontas nem dois Curingas seguidos — exceto na jogada vencedora. Sequências totalmente formadas por Curingas são válidas na jogada vencedora. Curingas não podem ser descartados.\nTroca: coloque uma carta na sequência para recuperar o Curinga que ela representa. O Curinga obtido deve ser usado em uma combinação — não pode ser descartado.\nAtenção: se você tentar descartar uma carta que pode substituir um Curinga, o descarte é bloqueado e você perde o direito de troca neste turno — a carta poderá ser descartada normalmente depois.\nDevolução: coloque um Curinga da sua mão de volta a uma sequência, recuperando a carta que ele cobria."
        : "Wildcards in sequences only. Cannot be at either end or two side by side — except on the winning move. All-joker sequences are valid on the winning move. Jokers cannot be discarded.\nSwap: place a card onto a table sequence to claim the joker it covers. The swapped joker must be used in a combo — it cannot be discarded.\nNote: if you try to discard a card that could replace a joker, the discard is blocked and you forfeit the swap for that card this turn — you may then discard it freely.\nReturn: place a joker from your hand back onto a sequence, taking back the card it was covering.";

    public string RulesDrawTitle    => Pt ? "Comprar Cartas"  : "Drawing Cards";
    public string RulesDrawBody     => Pt
        ? "O primeiro jogador de cada rodada pode comprar do baralho e descartar essa carta para comprar uma segunda em seu lugar.\nSe você comprar do descarte, deve usar essa carta em uma combinação ou devolvê-la (devolver restaura as opções de compra).\nAtenção: com apenas 1 carta na mão, não é permitido comprar do descarte uma carta que poderia ser adicionada diretamente a uma combinação já na mesa — você não pode vencer sem jogar sua carta restante."
        : "The first player of each round may draw from the deck and discard that card to draw a second one instead.\nIf you draw from the discard pile, you must use that card in a combination or return it (returning restores your draw options).\nNote: with only 1 card in hand, you cannot draw a discard card that could be added directly to an existing table combo — you cannot win without playing your remaining hand card.";

    public string RulesWinTitle     => Pt ? "Vencer a Rodada"  : "Winning a Round";
    public string RulesWinBody      => Pt
        ? "Jogue todas as suas cartas baixando combinações ou adicionando cartas à mesa — a rodada termina imediatamente.\nVocê também pode vencer descartando sua última carta (a rodada termina quando sua mão fica vazia)."
        : "Play all your cards by laying down combos or adding cards to the table — the round ends immediately.\nYou can also win by discarding your last card (the round ends as soon as your hand is empty).";

    public string RulesPointsTitle  => Pt ? "Pontuação"  : "Points";
    public string RulesPointsBody   => Pt
        ? "Cartas na mão valem: Curinga / Ás = 15 pts · 10, J, Q, K = 10 pts · Demais = valor nominal.\nQuem atingir 100 pontos explode (é eliminado do jogo). O último jogador restante vence.\nRegra de clemência do Ás: se você estiver com exatamente 98 pontos e terminar a rodada com apenas um Ás na mão, recebe 1 ponto em vez de 15 — e não explode."
        : "Cards left in hand score: Joker / Ace = 15 pts · 10, J, Q, K = 10 pts · Others = face value.\nReach 100 points and you explode — you're out. Last player standing wins the game.\nAce mercy rule: if you are at exactly 98 points and end a round with a single Ace in hand, you receive 1 point instead of 15 — and you don't explode.";

    // ── Add-to-combo feedback ─────────────────────────────────────────────
    public string AddToBadge           => Pt ? "clique para adicionar"  : "click to add";
    public string SwapJokerBadge       => Pt ? "trocar curinga"         : "swap joker";
    public string ReturnJokerBadge     => Pt ? "devolver curinga"       : "return joker";
    public string ReturnCardBadge      => Pt ? "devolver carta"         : "return card";
    public string TotalLabel           => Pt ? "Total"                  : "Total";
    public string ErrJokerInTriple     => Pt ? "Curingas não podem ser adicionados a uma Trinca."     : "Jokers cannot be added to a Triple.";
    public string ErrWrongRankTriple   => Pt ? "O valor não corresponde à Trinca."                   : "Card rank doesn't match this Triple.";
    public string ErrTripleFull        => Pt ? "Esta Trinca já está completa (máx. 6)."              : "This Triple is already full (max. 6).";
    public string ErrDuplicateSuit     => Pt ? "Este naipe já aparece duas vezes nesta Trinca."      : "That suit already appears twice in this Triple.";
    public string ErrExcludedSuit      => Pt ? "Este naipe não é permitido nesta Trinca."            : "That suit is not allowed in this Triple.";
    public string ErrWrongSuitSequence => Pt ? "O naipe não corresponde a esta Sequência."           : "Card suit doesn't match this Sequence.";
    public string ErrCardNotAdjacent   => Pt ? "A carta não é adjacente a esta Sequência."           : "Card isn't adjacent to this Sequence.";
    public string ErrJokerNoGap        => Pt ? "Não há espaço livre para um Curinga nesta Sequência." : "No open gap for a Joker in this Sequence.";
    public string ErrAdjacentJokers    => Pt ? "Dois Curingas seguidos só são permitidos na jogada vencedora." : "Two jokers side by side are only allowed on the winning move.";
    public string ErrCannotSwapJoker   => Pt ? "Esta carta não substitui nenhum Curinga nesta Sequência."      : "This card cannot replace any Joker in this Sequence.";
    public string ErrMustSwapJoker     => Pt ? "Esta carta pode substituir um Curinga na mesa — não é possível descartá-la." : "This card can replace a Joker on the table — it cannot be discarded.";
    public string ErrSwapLockedThisTurn => Pt ? "Você tentou descartar esta carta — a troca só é possível na próxima rodada." : "You tried to discard this card — the swap is only available next turn.";
    public string MsgSwappedJoker      => Pt ? "Você trocou uma carta por um Curinga!"                        : "You swapped your card for a Joker!";

    // ── Error messages ────────────────────────────────────────────────────
    public string ErrMustDrawDeck    => Pt ? "Você deve comprar do baralho."                                      : "You must draw from the deck.";
    public string ErrCannotDrawDiscardToWin => Pt
        ? "Com 1 carta na mão, você não pode pegar um descarte que pode ser adicionado diretamente à mesa."
        : "With 1 card left, you can't take a discard card that can go directly to the table.";
    public string ErrDiscardEmpty    => Pt ? "A pilha de descarte está vazia."                                    : "Discard pile is empty.";
    public string ErrNeed3Cards      => Pt ? "Selecione pelo menos 3 cartas para baixar."                         : "Select at least 3 cards to lay down.";
    public string ErrInvalidCombo    => Pt ? "As cartas selecionadas não formam uma combinação válida."           : "Selected cards don't form a valid combination.";
    public string ErrCantAddToCombo  => Pt ? "Essas cartas não podem ser adicionadas a essa combinação."          : "Those cards can't be added to that combination.";
    public string ErrSelect1ForCombo => Pt ? "Selecione exatamente 1 carta para adicionar a uma combinação."     : "Select exactly 1 card to add to a combination.";
    public string ErrMustUseDiscard  => Pt
        ? "Você deve usar a carta que pegou do descarte em uma combinação, ou devolvê-la."
        : "You must use the card you took from the discard pile in a combination, or return it.";
    public string ErrSelect1Discard  => Pt ? "Selecione exatamente 1 carta para descartar." : "Select exactly 1 card to discard.";
    public string ErrCannotDiscardJoker => Pt ? "Não é possível descartar um Curinga." : "You cannot discard a Joker.";
    public string ErrMustUseSwappedJoker => Pt ? "Você deve usar o Curinga que pegou da mesa em uma combinação antes de descartar." : "You must use the Joker you took from the table in a combination before discarding.";

    public string[] CpuNicknames => Pt
        ? new[] { "Beto", "Duda", "Gabi", "Kaká", "Manu", "Neto", "Rafa", "Titi", "Vini", "Bia", "Cadu", "Edu", "Gui", "Nico", "Pipo" }
        : new[] { "Ace", "Bolt", "Dash", "Rex", "Zara", "Fox", "Jay", "Max", "Rio", "Kai", "Leo", "Nova", "Rook", "Sage", "Cruz" };

    // ── Item 1: Combo rejection reasons ───────────────────────────────────
    public string ComboRejectionMsg(ComboRejection r) => r switch
    {
        ComboRejection.JokerInTriple              => Pt ? "Curingas não podem ser usados em Trincas."                            : "Jokers cannot be used in a Triple.",
        ComboRejection.TripleRankMismatch         => Pt ? "Uma Trinca exige 3 ou mais cartas do mesmo valor."                    : "A Triple requires 3 or more cards of the same rank.",
        ComboRejection.TripleDuplicateSuit        => Pt ? "Uma Trinca não pode ter dois ou mais cartas do mesmo naipe."          : "A Triple cannot have two or more cards of the same suit.",
        ComboRejection.SeqMixedSuits              => Pt ? "Todas as cartas de uma Sequência devem ser do mesmo naipe."           : "All cards in a Sequence must be the same suit.",
        ComboRejection.SeqDuplicateRanks          => Pt ? "Uma Sequência não pode ter dois ou mais cartas do mesmo valor."       : "A Sequence cannot have duplicate ranks.",
        ComboRejection.SeqJokerAtEndNotWinning    => Pt ? "Curinga na ponta só é permitido na jogada vencedora."                 : "Joker at either end is only allowed on the winning move.",
        ComboRejection.SeqAdjacentJokersNotWinning => Pt ? "Dois Curingas seguidos só são permitidos na jogada vencedora."       : "Two jokers side by side are only allowed on the winning move.",
        _                                         => Pt ? "As cartas selecionadas não formam uma combinação válida."             : "Selected cards don't form a valid combination.",
    };

    // ── Item 2: CPU win explanation ────────────────────────────────────────
    /// <summary>Returns a human-readable note explaining a complex CPU winning move, or null for simple wins.</summary>
    public string? MsgCpuWinNote(string name, CpuTurnSummary summary)
    {
        bool hasSwap   = summary.SwappedJokers.Count > 0;
        bool hasLaid   = summary.LaidDown.Count > 0;
        bool allJoker  = hasLaid && summary.LaidDown.Any(l => l.Cards.All(c => c.IsJoker));
        bool jokerEnd  = hasLaid && !allJoker && summary.LaidDown.Any(l =>
            l.Type == CombinationType.Sequence && l.Cards.Count >= 1 &&
            (l.Cards[0].IsJoker || l.Cards[^1].IsJoker));
        bool multiCombo = hasLaid && summary.LaidDown.Count >= 2;

        if (!hasSwap && !allJoker && !jokerEnd && !multiCombo) return null;

        if (allJoker && hasSwap)
            return Pt
                ? $"{name} trocou cartas por Curingas da mesa e baixou uma sequência de Curingas para vencer!"
                : $"{name} swapped cards for table Jokers and laid down an all-Joker sequence to win!";
        if (allJoker)
            return Pt
                ? $"{name} baixou uma sequência formada apenas por Curingas para vencer!"
                : $"{name} laid down an all-Joker sequence to win!";
        if (hasSwap && jokerEnd)
            return Pt
                ? $"{name} trocou uma carta por um Curinga e usou o Curinga na ponta de uma sequência para vencer!"
                : $"{name} swapped a card for a Joker and used the Joker at the end of a sequence to win!";
        if (hasSwap)
            return Pt
                ? $"{name} trocou uma carta por um Curinga da mesa para completar a jogada vencedora!"
                : $"{name} swapped a card for a table Joker to complete the winning move!";
        if (jokerEnd)
            return Pt
                ? $"{name} usou um Curinga na ponta de uma sequência — permitido apenas na jogada vencedora!"
                : $"{name} used a Joker at the end of a sequence — only allowed on the winning move!";
        if (multiCombo)
            return Pt
                ? $"{name} baixou {summary.LaidDown.Count} combinações de uma vez para vencer!"
                : $"{name} laid down {summary.LaidDown.Count} combos at once to win!";

        return null;
    }

    // ── Item 4: Second-draw privilege hint ────────────────────────────────
    public string SecondDrawHint => Pt ? "↓ Descartar = 2ª compra" : "↓ Discard = draw again";
    // ── Round-end toast ───────────────────────────────────────────────────
    public string ToastRoundWin  => Pt ? "Você ganhou a rodada! 👏" : "You won the round! 👏";
    public string ToastRoundLose(string winner) => Pt ? $"{winner} ganhou a rodada! 🃏" : $"{winner} won the round! 🃏";

    // ── Last-card warning ─────────────────────────────────────────────────
    public string WarnJokerSwap(string name) => Pt
        ? $"🃏 {name} trocou um curinga!"
        : $"🃏 {name} swapped a joker!";

    public string WarnLastCard(IReadOnlyList<string> names)
    {
        var joined = names.Count == 1 ? names[0]
            : string.Join(Pt ? " e " : " and ", names);
        return Pt
            ? $"⚠ {joined} {(names.Count == 1 ? "tem" : "têm")} apenas 1 carta!"
            : $"⚠ {joined} {(names.Count == 1 ? "has" : "have")} 1 card left!";
    }
}
