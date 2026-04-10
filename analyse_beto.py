"""
analyse_beto.py
Run with: python analyse_beto.py
(On Windows the console must support UTF-8; script forces it via sys.stdout reconfiguration.)


Analyses session 3F003DA3, round 2 from the Pontinho game log.
Extracts:
  - Player order in round 2
  - All lay-down events (combos put on table) in round 2
  - Full discard pile sequence in round 2
  - Per-Beto-turn detail: discard-top before draw, Beto's triples on table, draw source
  - Burn candidate detection: discard top matches rank of any Beto triple
"""

import re
import sys
import io
from pathlib import Path

# Force UTF-8 output on Windows consoles
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

SESSION = "3F003DA3"
LOG_PATH = Path(r"C:\Users\mgrisa\Documents\test\CardGames\bin\Debug\net10.0\logs\game_log.txt")

# ── helpers ──────────────────────────────────────────────────────────────────

def parse_card(token: str) -> tuple[str, str] | None:
    """Return (rank, suit_symbol) or None if token is not a card."""
    suits = "♠♥♦♣"
    token = token.strip()
    if token == "JKR":
        return ("JKR", "")
    for s in suits:
        if token.endswith(s):
            rank = token[:-1]
            if rank:
                return (rank, s)
    return None

def cards_from_line(text: str) -> list[str]:
    """Extract all card tokens from a log line."""
    return [t for t in re.split(r"\s+", text) if parse_card(t)]

def card_rank(card_token: str) -> str:
    p = parse_card(card_token)
    return p[0] if p else ""

# ── load and filter lines for this session ───────────────────────────────────

tag = f"[{SESSION}]"
session_lines: list[str] = []

with LOG_PATH.open(encoding="utf-8") as f:
    for raw in f:
        line = raw.rstrip("\n")
        if tag in line:
            # strip the session tag prefix for easier matching
            # format: [HH:MM:SS.mmm] [SESSION]   content
            session_lines.append(line)

if not session_lines:
    print(f"ERROR: session {SESSION} not found in log.")
    sys.exit(1)

print(f"Total lines for session {SESSION}: {len(session_lines)}")

# ── locate round 2 ───────────────────────────────────────────────────────────

def round_header_pattern(n: int) -> re.Pattern:
    return re.compile(rf"\[{SESSION}\]\s+ROUND {n}\s+\|")

r2_start = r3_start = None
for i, line in enumerate(session_lines):
    if r2_start is None and round_header_pattern(2).search(line):
        r2_start = i
    elif r2_start is not None and (
        round_header_pattern(3).search(line)
        or re.search(rf"\[{SESSION}\]\s+ROUND \d+\s+\|", line)
        and not round_header_pattern(2).search(line)
    ):
        r3_start = i
        break

if r2_start is None:
    print("ERROR: ROUND 2 header not found for this session.")
    sys.exit(1)

r2_lines = session_lines[r2_start: r3_start]  # None means end of session
print(f"Round 2 starts at session line index {r2_start}, {len(r2_lines)} lines.")

# ── extract content after the session tag ────────────────────────────────────

def content(line: str) -> str:
    """Return the content after '[SESSION]' and optional leading whitespace."""
    idx = line.find(f"[{SESSION}]")
    if idx == -1:
        return line
    rest = line[idx + len(f"[{SESSION}]"):]
    return rest.strip()

# ── 1. Player order ───────────────────────────────────────────────────────────

print("\n" + "=" * 70)
print("SECTION 1 — PLAYER ORDER IN ROUND 2")
print("=" * 70)

# The round 2 header is immediately followed by player hand lines
player_order: list[str] = []
first_player: str = ""

for line in r2_lines[:20]:
    c = content(line)
    m = re.search(r"ROUND 2\s+\|\s+First:\s+(\S+)", c)
    if m:
        first_player = m.group(1)
    # Hand lines look like:
    #   "Mauricio      : J♦  5♠  JKR ..."
    #   "Cadu (CPU)    : 10♣  K♠ ..."
    #   "YOUR HAND : 4♣  3♥ ..."    <- human player; skip — we want the named line below
    # They must end in card tokens only. Distinguish from turn/discard lines by
    # requiring the part after ':' to consist entirely of card-like tokens.
    hm = re.match(r"^(\S[\w\s]*?)\s*(?:\(CPU\))?\s*:\s+(.+)$", c)
    if hm:
        name_raw = hm.group(1).strip()
        rest = hm.group(2).strip()
        # Only accept if rest is card tokens (cards + spaces)
        tokens = rest.split()
        if tokens and all(parse_card(t) is not None for t in tokens):
            if name_raw not in ("YOUR HAND", "DISCARD"):
                player_order.append(name_raw)

print(f"First to act: {first_player}")
print(f"Players (deal order): {player_order}")

# ── 2. All lay-down events ────────────────────────────────────────────────────

print("\n" + "=" * 70)
print("SECTION 2 — ALL LAY-DOWN EVENTS IN ROUND 2")
print("=" * 70)

laydowns: list[dict] = []
current_turn: int = 0
current_player: str = ""

for line in r2_lines:
    c = content(line)
    # detect turn start: [T14] Beto drew ...
    tm = re.match(r"\[T(\d+)\]\s+(\S+)\s+drew", c)
    if tm:
        current_turn = int(tm.group(1))
        current_player = tm.group(2)
    # lay-down: "Beto laid down: 4♥ JKR 6♥ [Sequence]"
    lm = re.match(r"(\S+)\s+laid down:\s+(.+?)\s+\[(\w+)\]", c)
    if lm:
        laydowns.append({
            "turn": current_turn,
            "player": lm.group(1),
            "cards": lm.group(2).strip(),
            "type": lm.group(3),
            "line": line,
        })
        print(f"  T{current_turn:>2}  {lm.group(1):<14} laid down {lm.group(3):<10}: {lm.group(2).strip()}")

# ── 3. Discard pile sequence ──────────────────────────────────────────────────

print("\n" + "=" * 70)
print("SECTION 3 — DISCARD PILE SEQUENCE IN ROUND 2 (every discard event)")
print("=" * 70)

discards: list[dict] = []  # ordered list, index = position in sequence
current_turn = 0
current_player = ""

for line in r2_lines:
    c = content(line)
    tm = re.match(r"\[T(\d+)\]\s+(\S+)\s+drew", c)
    if tm:
        current_turn = int(tm.group(1))
        current_player = tm.group(2)

    # "  Beto discarded: K♠  (h:8)"
    dm = re.match(r"\s*(\S+)\s+discarded(?:\s+first card)?:\s+(\S+)", c)
    if dm:
        discards.append({
            "turn": current_turn,
            "player": dm.group(1),
            "card": dm.group(2),
            "pile_pos": len(discards),  # 0-based position in discard sequence
        })

print(f"{'#':<4} {'Turn':<6} {'Player':<14} {'Card Discarded'}")
for d in discards:
    print(f"  {d['pile_pos']+1:<3} T{d['turn']:<5} {d['player']:<14} {d['card']}")

# ── 4. Beto's triples on the table (track via lay-down events) ────────────────

# Build a dict: turn -> list of Beto triples currently on table at START of that turn
# We accumulate as we scan

beto_triples_on_table: list[dict] = []  # [{cards, type, turn_laid, rank}]
beto_table_state: list[dict] = []       # running state

current_turn = 0
current_player = ""

for line in r2_lines:
    c = content(line)
    tm = re.match(r"\[T(\d+)\]\s+(\S+)\s+drew", c)
    if tm:
        current_turn = int(tm.group(1))
        current_player = tm.group(2)

    lm = re.match(r"(\S+)\s+laid down:\s+(.+?)\s+\[(\w+)\]", c)
    if lm and lm.group(1) == "Beto":
        card_tokens = lm.group(2).strip().split()
        combo_type = lm.group(3)
        if combo_type == "Triple":
            ranks = [card_rank(t) for t in card_tokens if card_rank(t)]
            rank = ranks[0] if ranks else "?"
            beto_table_state.append({
                "cards": lm.group(2).strip(),
                "type": "Triple",
                "rank": rank,
                "turn_laid": current_turn,
            })

# ── 5. Per-Beto-turn analysis ─────────────────────────────────────────────────

print("\n" + "=" * 70)
print("SECTION 4 — BETO'S TURNS IN ROUND 2 (detailed)")
print("=" * 70)

current_turn = 0
current_player = ""
discard_top: str | None = None      # card currently on top of discard pile (most recent discard)
beto_triples_now: list[dict] = []   # Beto's triples currently on table

# We replay the round chronologically
beto_turns: list[dict] = []
in_beto_turn = False
beto_turn_data: dict = {}

# We need to track discard top precisely:
# It changes every time ANYONE discards.
# It is consumed (becomes buried) when someone draws from discard.

pile_top: str | None = None     # current discard pile top before Beto acts

# We'll do a single pass accumulating state
current_turn = 0
current_player_of_turn = ""
beto_triples_live: list[dict] = []

# snapshot: at the moment Beto's [Tx] draw line fires, what was pile_top?
beto_turn_snapshots: list[dict] = []

for line in r2_lines:
    c = content(line)

    # track draw-from-discard (consumes pile top)
    draw_dis = re.match(r"\[T(\d+)\]\s+(\S+)\s+drew from discard:\s+(\S+)", c)
    if draw_dis:
        current_turn = int(draw_dis.group(1))
        current_player_of_turn = draw_dis.group(2)
        drawn_card = draw_dis.group(3)
        if current_player_of_turn == "Beto":
            beto_turn_snapshots.append({
                "turn": current_turn,
                "pile_top_before": pile_top,
                "draw_source": "discard",
                "drawn_card": drawn_card,
                "triples_on_table": [t.copy() for t in beto_triples_live],
            })
        pile_top = None  # consumed
        continue

    # track draw-from-deck
    draw_deck = re.match(r"\[T(\d+)\]\s+(\S+)\s+drew from deck:\s+(\S+)", c)
    if draw_deck:
        current_turn = int(draw_deck.group(1))
        current_player_of_turn = draw_deck.group(2)
        drawn_card = draw_deck.group(3)
        if current_player_of_turn == "Beto":
            beto_turn_snapshots.append({
                "turn": current_turn,
                "pile_top_before": pile_top,
                "draw_source": "deck",
                "drawn_card": drawn_card,
                "triples_on_table": [t.copy() for t in beto_triples_live],
            })
        continue

    # track discards (updates pile top)
    dm = re.match(r"\s*(\S+)\s+discarded(?:\s+first card)?:\s+(\S+)", c)
    if dm:
        pile_top = dm.group(2)
        continue

    # track Beto laying down triples
    lm = re.match(r"Beto laid down:\s+(.+?)\s+\[(\w+)\]", c)
    if lm and lm.group(2) == "Triple":
        card_tokens = lm.group(1).strip().split()
        ranks = [card_rank(t) for t in card_tokens if card_rank(t)]
        beto_triples_live.append({
            "cards": lm.group(1).strip(),
            "rank": ranks[0] if ranks else "?",
            "turn_laid": current_turn,
        })

# ── deduplicate Beto turn snapshots (second-draw produces two [Tx] entries) ──

seen_turns: set[int] = set()
beto_turns_deduped: list[dict] = []
for snap in beto_turn_snapshots:
    if snap["turn"] not in seen_turns:
        seen_turns.add(snap["turn"])
        beto_turns_deduped.append(snap)
    else:
        # second draw: update drawn_card to the second draw
        for s in beto_turns_deduped:
            if s["turn"] == snap["turn"]:
                s["second_draw"] = snap["drawn_card"]
                s["draw_source"] = "deck (second draw)"

# ── print results ─────────────────────────────────────────────────────────────

for snap in beto_turns_deduped:
    t = snap["turn"]
    top = snap["pile_top_before"] or "(empty)"
    src = snap["draw_source"]
    drawn = snap["drawn_card"]
    triples = snap["triples_on_table"]

    # burn candidate: discard top matches rank of any triple Beto has on table
    burn_candidates = []
    if snap["pile_top_before"]:
        top_rank = card_rank(snap["pile_top_before"])
        for tri in triples:
            if tri["rank"] == top_rank:
                burn_candidates.append(tri)

    print(f"\n--- T{t}: Beto's turn ---")
    print(f"  Discard top BEFORE draw : {top}")
    print(f"  Draw source             : {src} -> drew {drawn}")
    if snap.get("second_draw"):
        pass  # already merged into draw_source label
    if triples:
        print(f"  Beto's triples on table :")
        for tri in triples:
            print(f"    [{tri['rank']}] {tri['cards']}  (laid T{tri['turn_laid']})")
    else:
        print(f"  Beto's triples on table : (none)")

    if burn_candidates:
        print(f"  *** BURN CANDIDATE ***  : discard top {snap['pile_top_before']} matches triple rank {burn_candidates[0]['rank']}")
        print(f"      -> Beto had {burn_candidates[0]['cards']} on table")
        if src.startswith("deck"):
            print(f"      -> Beto drew from DECK instead of taking the burn card")
        else:
            print(f"      -> Beto drew from DISCARD (took the burn card)")
    else:
        print(f"  Burn candidate          : NO")

# ── 6. Summary ───────────────────────────────────────────────────────────────

print("\n" + "=" * 70)
print("SECTION 5 — SUMMARY")
print("=" * 70)

burn_misses = [
    s for s in beto_turns_deduped
    if s["pile_top_before"] and any(
        t["rank"] == card_rank(s["pile_top_before"])
        for t in s["triples_on_table"]
    ) and s["draw_source"].startswith("deck")
]

burn_takes = [
    s for s in beto_turns_deduped
    if s["pile_top_before"] and any(
        t["rank"] == card_rank(s["pile_top_before"])
        for t in s["triples_on_table"]
    ) and not s["draw_source"].startswith("deck")
]

print(f"Beto's total turns in round 2 : {len(beto_turns_deduped)}")
print(f"Turns with burn candidate on pile : {len(burn_misses) + len(burn_takes)}")
print(f"  - Beto took the burn card (drew from discard) : {len(burn_takes)}")
print(f"  - Beto MISSED the burn card (drew from deck)  : {len(burn_misses)}")
for s in burn_misses:
    print(f"      T{s['turn']}: pile top was {s['pile_top_before']} (rank {card_rank(s['pile_top_before'])}), drew from deck instead")
