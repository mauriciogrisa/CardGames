"""
Burn opportunity analysis for session 3F003DA3, Round 2.

A "burn opportunity" is when:
  1. A player has a Triple on the table (e.g. 3♣ 3♦ 3♠)
  2. The card on top of the discard pile BEFORE that player's draw is the 4th suit
     of that same rank (e.g. 3♥)
  3. The player drew from the deck instead of the discard pile

The script:
  - Extracts all lay-down events in Round 2 to find every triple laid
  - Builds the ordered discard pile sequence (card on top at each moment)
  - For each player who laid a triple, lists every subsequent turn they took,
    showing the discard top before their draw and flagging burn matches
  - Notes when the burn card is drawn by anyone (table owner or other player)
"""

import re
import sys
import io

# Force UTF-8 output on Windows
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

LOG_PATH = r"C:\Users\mgrisa\Documents\test\CardGames\bin\Debug\net10.0\logs\game_log.txt"
SESSION_ID = "3F003DA3"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

SUIT_SYMBOLS = {"♣", "♦", "♥", "♠"}

def parse_card(token: str) -> tuple[str, str] | None:
    """Return (rank, suit) or None if token is not a card."""
    token = token.strip().rstrip(".,;")
    if not token:
        return None
    for suit in SUIT_SYMBOLS:
        if suit in token:
            rank = token.replace(suit, "").strip()
            if rank:
                return (rank, suit)
    return None

def card_str(rank: str, suit: str) -> str:
    return f"{rank}{suit}"

def cards_str(cards: list[tuple[str, str]]) -> str:
    return "  ".join(card_str(r, s) for r, s in cards)

# ---------------------------------------------------------------------------
# Step 1 — locate session 3F003DA3 Round 2 lines
# ---------------------------------------------------------------------------

prefix = f"[{SESSION_ID}]"

with open(LOG_PATH, encoding="utf-8") as f:
    all_lines = f.readlines()

# Find Round 2 start and end within the session
round2_start = None
round2_end = None
in_session = False

for i, line in enumerate(all_lines):
    if prefix not in line:
        continue
    in_session = True
    if "ROUND 2  |  First:" in line and round2_start is None:
        round2_start = i
    if "ROUND 2 OVER" in line and round2_start is not None and round2_end is None:
        round2_end = i
        break

if round2_start is None:
    sys.exit(f"ERROR: Could not find Round 2 for session {SESSION_ID}")

r2_lines = [ln.rstrip("\n") for ln in all_lines[round2_start : round2_end + 1]]

print(f"Session {SESSION_ID} — Round 2 spans log lines {round2_start+1}–{round2_end+1}")
print(f"  ({len(r2_lines)} lines total)\n")

# ---------------------------------------------------------------------------
# Step 2 — parse every Round-2 event
# ---------------------------------------------------------------------------

# Pattern: [timestamp] [SESSION] event text
EVENT_RE = re.compile(
    r"\[[\d:.]+\] \[" + re.escape(SESSION_ID) + r"\] (.*)"
)
TURN_DRAW_RE = re.compile(
    r"\[T(\d+)\] (\S+(?:\s+\(CPU\))?(?:\s+\w+)?)\s+drew from (deck|discard): (.+)"
)
# Simpler player-name extraction: first word after [Tn]
TURN_START_RE = re.compile(r"\[T(\d+)\] (.+?) drew from (deck|discard): (.+?)(?:\s+\(second draw\))?$")
LAYDOWN_RE = re.compile(r"(.+?) laid down: (.+?) \[(\w+)\]$")
DISCARD_RE = re.compile(r"(.+?) discarded(?:\s+first card)?: (\S+)")
ADD_COMBO_RE = re.compile(r"(.+?) added (.+?) to (Triple|Sequence) combo")
SWAP_RE = re.compile(r"(.+?) swapped (.+?) for joker in Sequence combo")

# We'll track events as structured dicts
events = []

for raw in r2_lines:
    m = EVENT_RE.match(raw)
    if not m:
        continue
    text = m.group(1).strip()

    # Draw event (new turn)
    td = TURN_START_RE.match(text)
    if td:
        turn_num = int(td.group(1))
        player = td.group(2).strip()
        source = td.group(3)
        drawn_card_text = td.group(4).strip()
        drawn = parse_card(drawn_card_text)
        events.append({
            "type": "draw",
            "turn": turn_num,
            "player": player,
            "source": source,
            "card": drawn,
        })
        continue

    # Lay-down event
    ld = LAYDOWN_RE.match(text)
    if ld:
        player = ld.group(1).strip()
        cards_text = ld.group(2).strip()
        combo_type = ld.group(3)
        card_tokens = re.split(r"\s{2,}", cards_text)
        cards = [c for c in (parse_card(t) for t in card_tokens) if c]
        events.append({
            "type": "laydown",
            "player": player,
            "combo_type": combo_type,
            "cards": cards,
        })
        continue

    # Discard event
    ds = DISCARD_RE.match(text)
    if ds:
        player = ds.group(1).strip()
        card_text = ds.group(2).strip()
        card = parse_card(card_text)
        is_first = "first card" in text
        events.append({
            "type": "discard",
            "player": player,
            "card": card,
            "is_first": is_first,
        })
        continue

    # Add-to-combo
    ac = ADD_COMBO_RE.match(text)
    if ac:
        events.append({
            "type": "add_combo",
            "player": ac.group(1).strip(),
            "card_text": ac.group(2).strip(),
            "combo_type": ac.group(3),
        })
        continue

# ---------------------------------------------------------------------------
# Step 3 — find all triples laid in Round 2
# ---------------------------------------------------------------------------

# triples_by_player: player -> list of {rank, suits_so_far}
triples_by_player: dict[str, list[dict]] = {}

for ev in events:
    if ev["type"] == "laydown" and ev["combo_type"] == "Triple":
        player = ev["player"]
        cards = ev["cards"]
        ranks = set(r for r, s in cards)
        if len(ranks) == 1:
            rank = list(ranks)[0]
            suits = set(s for r, s in cards)
            triple_info = {"rank": rank, "suits": suits}
            triples_by_player.setdefault(player, []).append(triple_info)

print("=" * 60)
print("TRIPLES LAID IN ROUND 2")
print("=" * 60)
if not triples_by_player:
    print("  (none found)")
else:
    for player, triples in triples_by_player.items():
        for t in triples:
            missing_suits = SUIT_SYMBOLS - t["suits"]
            print(f"  {player}: {t['rank']}  suits={sorted(t['suits'])}  "
                  f"-> 4th suit(s) that would complete: {sorted(missing_suits)}")
print()

# Also handle "added X to Triple combo" — those extend existing triples.
# We map those to the owner's triple (owner = whoever laid the triple).
# For burn opportunity tracking, the suit added to the table via add_combo
# is now no longer a burn candidate (it's on the table).

# Process add-to-combo events to update triples_by_player suits
# We match by rank only (since a player may have multiple triples of different ranks)
for ev in events:
    if ev["type"] == "add_combo" and ev["combo_type"] == "Triple":
        card_text = ev["card_text"]
        # card_text may be a single card token
        added = parse_card(card_text)
        if added is None:
            continue
        rank, suit = added
        # Find which triple on the table has this rank
        for player, triples in triples_by_player.items():
            for t in triples:
                if t["rank"] == rank:
                    t["suits"].add(suit)
                    break

# ---------------------------------------------------------------------------
# Step 4 — build discard-pile state at each turn boundary
# Discard top changes:
#   - When someone discards (non-first-card), the discarded card becomes the new top.
#   - When someone draws from discard, the previous top is taken; new top = card below.
#     (We can't reconstruct the full pile, so we track a simple "current top" that
#      becomes None when a draw-from-discard consumes it — until next discard.)
# ---------------------------------------------------------------------------

# We need to replay events in order to know discard top BEFORE each draw.
# Strategy: maintain current_discard_top; update on discard events.
# For draws from discard, the top is consumed (set to None) until next discard.

# We'll build a list of (turn_number, player, draw_source, discard_top_before_draw)
# for every draw event, so we can check burn opportunities.

turn_draw_info: list[dict] = []  # {turn, player, source, discard_top_before_draw}

current_discard_top: tuple[str, str] | None = None  # (rank, suit) or None

for ev in events:
    if ev["type"] == "draw":
        # Record what was on top BEFORE this draw
        turn_draw_info.append({
            "turn": ev["turn"],
            "player": ev["player"],
            "source": ev["source"],
            "discard_top_before": current_discard_top,
            "drawn_card": ev["card"],
        })
        if ev["source"] == "discard":
            # The drawn card is now gone from the top
            current_discard_top = None  # We don't know what's underneath
    elif ev["type"] == "discard" and not ev.get("is_first"):
        # A real discard — becomes new top
        if ev["card"] is not None:
            current_discard_top = ev["card"]

# ---------------------------------------------------------------------------
# Step 5 — for each triple owner, report every subsequent turn with burn analysis
# ---------------------------------------------------------------------------

# Build a map: turn_number -> draw_info for quick lookup
draw_by_turn: dict[int, dict] = {d["turn"]: d for d in turn_draw_info}

# Find the turn on which each triple was laid (so we only look at turns AFTER that)
# triple_laid_at_turn[player] = minimum turn number after which they have a triple
triple_laid_turn: dict[str, int] = {}
current_turn = 0
for ev in events:
    if ev["type"] == "draw":
        current_turn = ev["turn"]
    if ev["type"] == "laydown" and ev["combo_type"] == "Triple":
        player = ev["player"]
        if player not in triple_laid_turn:
            triple_laid_turn[player] = current_turn  # laid during this turn

# Also track which turns had a burn card drawn by anyone (for notes)
# A burn card is a card whose rank matches any existing triple's missing suit

def is_burn_card_for_anyone(card: tuple[str, str] | None, triples: dict) -> list[str]:
    """Return list of players whose triple would be completed by this card."""
    if card is None:
        return []
    rank, suit = card
    matches = []
    for player, player_triples in triples.items():
        for t in player_triples:
            if t["rank"] == rank and suit not in t["suits"] and len(t["suits"]) < 4:
                matches.append(player)
    return matches

print("=" * 60)
print("BURN OPPORTUNITY ANALYSIS — Per player with a triple")
print("=" * 60)
print()

# We need the triple state at the time of each turn (suits may grow as cards are added).
# Simplification: use the final suite state from step 3 for suit-membership, but
# only flag burns for suits NOT yet in the triple at that turn.
# For full accuracy we'd replay suit additions in turn order — let's do that.

# Rebuild triples with their suit state evolving per turn
# First pass: gather all lay-down triples with the turn they were laid

class TripleState:
    def __init__(self, player: str, rank: str, initial_suits: set[str], laid_turn: int):
        self.player = player
        self.rank = rank
        self.suits = set(initial_suits)
        self.laid_turn = laid_turn

    def missing_suits(self) -> set[str]:
        return SUIT_SYMBOLS - self.suits

    def __repr__(self):
        return f"Triple({self.player}, {self.rank}, suits={sorted(self.suits)})"

triples_states: list[TripleState] = []

current_turn = 0
for ev in events:
    if ev["type"] == "draw":
        current_turn = ev["turn"]
    if ev["type"] == "laydown" and ev["combo_type"] == "Triple":
        player = ev["player"]
        cards = ev["cards"]
        ranks = set(r for r, s in cards)
        if len(ranks) == 1:
            rank = list(ranks)[0]
            suits = set(s for r, s in cards)
            triples_states.append(TripleState(player, rank, suits, current_turn))
    if ev["type"] == "add_combo" and ev["combo_type"] == "Triple":
        added = parse_card(ev["card_text"])
        if added:
            rank, suit = added
            for ts in triples_states:
                if ts.rank == rank:
                    ts.suits.add(suit)
                    break

# Now replay turn-by-turn to produce the burn report.
# We replay in turn order. For each draw event we check burn opportunity.
# We need the triple state *at that moment* — but add_combo events happen
# AFTER the draw in the same turn. So: suit additions from the current turn
# should not retroactively affect the "before draw" check.

# Full replay: build an ordered list of (turn, event_index, event)
# and track triple suits dynamically.

# Reset triples to initial state; replay in order
class LiveTriple:
    def __init__(self, player: str, rank: str, initial_suits: set[str], laid_turn: int):
        self.player = player
        self.rank = rank
        self.suits = set(initial_suits)
        self.laid_turn = laid_turn

    def missing_suits(self) -> set[str]:
        return SUIT_SYMBOLS - self.suits

live_triples: list[LiveTriple] = []

current_turn_live = 0
current_discard_top_live: tuple[str, str] | None = None
report: list[dict] = []  # items to print

for ev in events:
    if ev["type"] == "draw":
        current_turn_live = ev["turn"]
        draw_source = ev["source"]
        drawn_card = ev["card"]
        discard_top = current_discard_top_live
        player = ev["player"]

        # For each live triple, check if this player's draw is post-triple
        # and if the discard top is a burn candidate for their triple
        for lt in live_triples:
            if lt.player == player and current_turn_live > lt.laid_turn:
                # Check if discard top is the missing suit of this rank
                if discard_top is not None:
                    d_rank, d_suit = discard_top
                    is_burn = (d_rank == lt.rank and d_suit in lt.missing_suits())
                else:
                    is_burn = False
                report.append({
                    "turn": current_turn_live,
                    "player": player,
                    "triple_rank": lt.rank,
                    "triple_suits": set(lt.suits),
                    "missing_suits": set(lt.missing_suits()),
                    "discard_top": discard_top,
                    "draw_source": draw_source,
                    "drawn_card": drawn_card,
                    "burn_match": is_burn,
                })

        # Also note if anyone else draws the burn card (it's now gone)
        if draw_source == "discard" and discard_top is not None:
            current_discard_top_live = None

        # Check if the drawn-from-discard card was a burn candidate for any triple owner
        # (other than the owner themselves — already handled above if player == triple owner)
        if draw_source == "discard" and drawn_card is not None:
            for lt in live_triples:
                if lt.player != player:
                    d_rank, d_suit = drawn_card
                    if d_rank == lt.rank and d_suit in lt.missing_suits():
                        report.append({
                            "turn": current_turn_live,
                            "player": player,  # the one who drew it
                            "triple_rank": lt.rank,
                            "triple_owner": lt.player,
                            "note": (f"BURN CARD TAKEN by {player}: "
                                     f"{card_str(*drawn_card)} was the missing suit for "
                                     f"{lt.player}'s triple {lt.rank}"),
                            "burn_match": "TAKEN",
                        })

    elif ev["type"] == "laydown" and ev["combo_type"] == "Triple":
        player = ev["player"]
        cards = ev["cards"]
        ranks = set(r for r, s in cards)
        if len(ranks) == 1:
            rank = list(ranks)[0]
            suits = set(s for r, s in cards)
            live_triples.append(LiveTriple(player, rank, suits, current_turn_live))

    elif ev["type"] == "add_combo" and ev["combo_type"] == "Triple":
        added = parse_card(ev["card_text"])
        if added:
            rank, suit = added
            for lt in live_triples:
                if lt.rank == rank:
                    lt.suits.add(suit)
                    break

    elif ev["type"] == "discard" and not ev.get("is_first"):
        if ev["card"] is not None:
            current_discard_top_live = ev["card"]

# ---------------------------------------------------------------------------
# Step 6 — print the report
# ---------------------------------------------------------------------------

if not report:
    print("  No turns found for any player after they laid a triple.")
else:
    current_player = None
    for entry in report:
        # Section header when player changes or it's a TAKEN note
        if "note" in entry:
            print(f"  [T{entry['turn']}] >>> {entry['note']} <<<")
            continue

        player = entry["player"]
        triple_rank = entry["triple_rank"]
        if player != current_player:
            print(f"\n--- {player}  (has Triple: {triple_rank}  "
                  f"suits on table: {sorted(entry['triple_suits'])}  "
                  f"missing: {sorted(entry['missing_suits'])}) ---")
            current_player = player

        discard_top = entry["discard_top"]
        discard_top_str = card_str(*discard_top) if discard_top else "(empty)"
        drawn_str = card_str(*entry["drawn_card"]) if entry["drawn_card"] else "?"
        burn_flag = "*** BURN MATCH! ***" if entry["burn_match"] else ""
        missed = ""
        if entry["burn_match"] and entry["draw_source"] == "deck":
            missed = " <-- MISSED BURN (drew from deck instead)"
        elif entry["burn_match"] and entry["draw_source"] == "discard":
            missed = " <-- drew from discard (took the burn card!)"

        print(f"  [T{entry['turn']}]  discard top before draw: {discard_top_str:>5}  "
              f"| draw source: {entry['draw_source']:>7}  "
              f"| drew: {drawn_str:<5}  "
              f"| burn match: {'YES' if entry['burn_match'] else 'NO ':>3}  "
              f"{missed}")

print()
print("=" * 60)
print("SUMMARY")
print("=" * 60)
burn_misses = [e for e in report if e.get("burn_match") is True and e["draw_source"] == "deck"]
burn_taken = [e for e in report if e.get("burn_match") == "TAKEN"]
burn_self_took = [e for e in report if e.get("burn_match") is True and e["draw_source"] == "discard"]

print(f"  Triples on table: {len(live_triples)}")
for lt in live_triples:
    ms = sorted(lt.missing_suits())
    complete = "(COMPLETE — all 4 suits)" if not ms else f"missing: {ms}"
    print(f"    {lt.player}: {lt.rank}  suits={sorted(lt.suits)}  {complete}")
print()
print(f"  Missed burn opportunities (triple owner drew from deck when burn card was on discard): "
      f"{len(burn_misses)}")
for e in burn_misses:
    print(f"    [T{e['turn']}] {e['player']} — discard was {card_str(*e['discard_top'])} "
          f"(missing suit for {e['triple_rank']} triple), drew from deck instead")
print()
print(f"  Burn card taken by triple owner (correctly drew the burn card): {len(burn_self_took)}")
for e in burn_self_took:
    print(f"    [T{e['turn']}] {e['player']} drew {card_str(*e['discard_top'])} from discard "
          f"(completes triple {e['triple_rank']})")
print()
print(f"  Burn card taken by another player (triple owner lost their chance): {len(burn_taken)}")
for e in burn_taken:
    print(f"    {e['note']}")
