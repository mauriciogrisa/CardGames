#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"Pontinho game-log analyser."

import re
import sys
import os
from collections import defaultdict
from datetime import timedelta

# ── constants ───────────────────────────────────────────────────────────────
LOG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "bin", "Debug", "net10.0", "logs", "game_log.txt")
HUMAN_NAMES = {"MAURICIO", "IRIA"}
HIGH_TIER_RANKS = {"J", "Q", "K", "A", "JKR"}


def card_rank(card_str):
    """Extract rank token from a card string like '7♥' or 'JKR'."""
    card_str = card_str.strip()
    if re.match(r'^JKR', card_str, re.IGNORECASE):
        return "JKR"
    m = re.match(r'^(10|[2-9]|J|Q|K|A)', card_str, re.IGNORECASE)
    return m.group(1).upper() if m else card_str


def is_high_tier(card_str):
    return card_rank(card_str) in HIGH_TIER_RANKS


def parse_timestamp(ts_str):
    ts_str = ts_str.strip()
    try:
        parts = ts_str.split(":")
        h = int(parts[0])
        mn = int(parts[1])
        s_parts = parts[2].split(".")
        s = int(s_parts[0])
        ms = int(s_parts[1]) if len(s_parts) > 1 else 0
        return timedelta(hours=h, minutes=mn, seconds=s, milliseconds=ms)
    except Exception:
        return None


# ── raw line parsing ─────────────────────────────────────────────────────────
LINE_RE = re.compile(
    r'^\[(\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]\s*(?:\[([0-9A-Fa-f]{6,})\])?\s*(.*)')


def parse_line(raw):
    m = LINE_RE.match(raw.rstrip("\n"))
    if not m:
        return None
    return m.group(1), (m.group(2) or ""), m.group(3).strip()


# ── data structures ──────────────────────────────────────────────────────────

class TurnData:
    __slots__ = (
        "turn_num", "player", "session_id", "round_num",
        "draw_source", "draw_card", "second_draw_used", "second_draw_card",
        "discarded_card", "discard_h", "laid_down", "added_to_table",
        "swapped_joker", "played_all", "returned_to_deck", "draw_ts", "discard_ts",
    )

    def __init__(self, turn_num, player, session_id, round_num):
        self.turn_num = turn_num
        self.player = player
        self.session_id = session_id
        self.round_num = round_num
        self.draw_source = None
        self.draw_card = None
        self.second_draw_used = False
        self.second_draw_card = None
        self.discarded_card = None
        self.discard_h = None
        self.laid_down = []       # list of (cards_list, combo_type)
        self.added_to_table = []  # list of (card, combo_type)
        self.swapped_joker = []   # list of (card, combo_type)
        self.played_all = False
        self.returned_to_deck = False
        self.draw_ts = None
        self.discard_ts = None


class RoundData:
    __slots__ = (
        "session_id", "round_num", "first_player", "cpu_players",
        "turns", "winner", "penalties", "scores", "complete",
    )

    def __init__(self, session_id, round_num, first_player):
        self.session_id = session_id
        self.round_num = round_num
        self.first_player = first_player
        self.cpu_players = set()
        self.turns = []
        self.winner = None
        self.penalties = {}
        self.scores = {}
        self.complete = False


class SessionData:
    __slots__ = (
        "session_id", "player_count", "difficulty", "rounds",
        "game_over", "game_winner", "final_scores",
        "human_names", "cpu_names", "edge_case",
    )

    def __init__(self, session_id, player_count, difficulty):
        self.session_id = session_id
        self.player_count = player_count
        self.difficulty = difficulty
        self.rounds = []
        self.game_over = False
        self.game_winner = None
        self.final_scores = {}
        self.human_names = set()
        self.cpu_names = set()
        self.edge_case = False


# ── main parser ──────────────────────────────────────────────────────────────

def parse_log(path):
    sessions = []
    cur_session = None
    cur_round = None
    cur_turn = None
    # anti-feed tracking
    last_discard_card = None
    last_discard_player = None
    last_discard_session = None
    last_discard_round = None
    last_discard_turn_num = None
    anti_feed_events = []

    def flush_turn():
        nonlocal cur_turn
        if cur_turn is not None and cur_round is not None:
            cur_round.turns.append(cur_turn)
            cur_turn = None

    def flush_round():
        nonlocal cur_round
        flush_turn()
        if cur_round is not None and cur_session is not None:
            cur_session.rounds.append(cur_round)
            cur_round = None

    def flush_session():
        flush_round()
        if cur_session is not None:
            sessions.append(cur_session)

    with open(path, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            parsed = parse_line(raw)
            if not parsed:
                continue
            ts_str, sid, body = parsed

            if body.startswith("───"):
                continue

            # ── NEW GAME ──────────────────────────────────────────────────
            if "NEW GAME" in body and "Players:" in body:
                flush_session()
                pm = re.search(r'Players:\s*(\d+)', body)
                dm = re.search(r'Difficulty:\s*(\S+)', body)
                pc = int(pm.group(1)) if pm else 0
                diff = dm.group(1) if dm else "Unknown"
                effective_sid = sid if sid else f"nosid_{len(sessions)}"
                cur_session = SessionData(effective_sid, pc, diff)
                cur_round = None
                cur_turn = None
                last_discard_card = None
                continue

            if cur_session is None:
                continue

            if "[EDGE CASE]" in body:
                cur_session.edge_case = True

            # ── ROUND header ──────────────────────────────────────────────
            m = re.match(r'ROUND\s+(\d+)\s*\|\s*First:\s*(.+)', body)
            if m:
                flush_round()
                cur_round = RoundData(
                    cur_session.session_id, int(m.group(1)), m.group(2).strip())
                last_discard_card = None
                continue

            if cur_round is None:
                continue

            # ── dealt hand (player listing) ────────────────────────────────
            # Matches lines like: "  Titi (CPU)    : 8♣  4♠ …" or "  YOUR HAND : …"
            # We only care about CPU-tagged lines to populate cpu_players
            hand_m = re.match(
                r'([A-Za-záéíóúãõâêîôàüçñÁÉÍÓÚÃÕÂÊÎÔÀÜÇÑ][A-Za-záéíóúãõâêîôàüçñÁÉÍÓÚÃÕÂÊÎÔÀÜÇÑ\s\-\']*?)'
                r'(?:\s+\(CPU\))?\s*:\s+', body)
            if (hand_m
                    and "(CPU)" in body
                    and "drew" not in body
                    and "discarded" not in body
                    and "laid down" not in body
                    and "added" not in body
                    and "swapped" not in body
                    and "played all" not in body):
                pname = hand_m.group(1).strip()
                cur_round.cpu_players.add(pname)
                cur_session.cpu_names.add(pname)

            # ── turn draw line  [TN] player drew from deck/discard: card ──
            tn_m = re.match(
                r'\[T(\d+)\]\s+(.+?)\s+drew\s+from\s+(deck|discard):\s+(\S+)', body)
            if tn_m:
                # check for "(second draw)" suffix on this same line
                is_second = "(second draw)" in body

                if is_second:
                    # This is the second-draw action; it marks the PREVIOUS turn
                    # as having used second-draw.  We do NOT create a new TurnData.
                    if cur_round.turns:
                        cur_round.turns[-1].second_draw_used = True
                        cur_round.turns[-1].second_draw_card = tn_m.group(4).strip()
                    elif cur_turn is not None:
                        cur_turn.second_draw_used = True
                        cur_turn.second_draw_card = tn_m.group(4).strip()
                else:
                    flush_turn()
                    tn = int(tn_m.group(1))
                    player = tn_m.group(2).strip()
                    source = tn_m.group(3)
                    card = tn_m.group(4).strip()

                    cur_turn = TurnData(tn, player, cur_session.session_id, cur_round.round_num)
                    cur_turn.draw_source = source
                    cur_turn.draw_card = card
                    cur_turn.draw_ts = parse_timestamp(ts_str)

                    # anti-feed check (human classification resolved in enrich_sessions)
                    if (source == "discard"
                            and last_discard_card is not None
                            and card.strip() == last_discard_card.strip()
                            and last_discard_session == cur_session.session_id
                            and last_discard_round == cur_round.round_num
                            and last_discard_player != player):
                        anti_feed_events.append({
                            "session": cur_session.session_id,
                            "round": cur_round.round_num,
                            "turn": tn,
                            "discarder": last_discard_player,
                            "discarder_cpu_name": last_discard_player,  # resolved later
                            "card": last_discard_card,
                            "drawer": player,
                            "drawer_cpu_name": player,  # resolved later
                            "discard_turn": last_discard_turn_num,
                            "session_ref": cur_session,
                        })
                continue

            # ── second-draw inline (no [TN] prefix) ──────────────────────
            # e.g. "  Beto drew from deck: 5♣  (second draw)"
            if "(second draw)" in body and cur_turn is not None and "[T" not in body:
                cur_turn.second_draw_used = True
                m2 = re.search(r'drew from deck:\s+(\S+)', body)
                if m2:
                    cur_turn.second_draw_card = m2.group(1).strip()
                continue

            # ── discard first card (drawing second) ───────────────────────
            # "PlayerName discarded first card: X  (drawing second)"
            if "discarded first card:" in body and "(drawing second)" in body:
                # The actual second draw comes as the next [TN] line with "(second draw)"
                # Nothing to do here except note it was used (handled via [TN] second draw line)
                continue

            if cur_turn is None:
                # Lines below require an active turn
                pass

            # ── discard ───────────────────────────────────────────────────
            disc_m = re.match(r'.+?\s+discarded:\s+(\S+)\s+\(h:(\d+)\)', body)
            if disc_m and cur_turn is not None:
                card_d = disc_m.group(1).strip()
                h = int(disc_m.group(2))
                if cur_turn.discarded_card is None:
                    cur_turn.discarded_card = card_d
                    cur_turn.discard_h = h
                    cur_turn.discard_ts = parse_timestamp(ts_str)
                last_discard_card = card_d
                last_discard_player = cur_turn.player
                last_discard_session = cur_session.session_id
                last_discard_round = cur_round.round_num
                last_discard_turn_num = cur_turn.turn_num
                continue

            # ── lay-down ──────────────────────────────────────────────────
            lay_m = re.match(r'.+?\s+laid down:\s+(.+?)\s+\[(\w+)\]', body)
            if lay_m and cur_turn is not None:
                cards = [c.strip() for c in lay_m.group(1).split() if c.strip()]
                cur_turn.laid_down.append((cards, lay_m.group(2)))
                continue

            # ── added to table ────────────────────────────────────────────
            add_m = re.match(r'.+?\s+added\s+(.+?)\s+to\s+(\w+)\s+combo', body)
            if add_m and cur_turn is not None:
                cards = [c.strip() for c in add_m.group(1).split() if c.strip()]
                for c in cards:
                    cur_turn.added_to_table.append((c, add_m.group(2)))
                continue

            # ── returned discard-drawn card to deck ───────────────────────
            if re.search(r'returned .+ to deck', body) and cur_turn is not None:
                cur_turn.returned_to_deck = True
                continue

            # ── joker swap ────────────────────────────────────────────────
            swap_m = re.match(r'.+?\s+swapped\s+(\S+)\s+for joker in\s+(\w+)\s+combo', body)
            if swap_m and cur_turn is not None:
                cur_turn.swapped_joker.append((swap_m.group(1).strip(), swap_m.group(2)))
                continue

            # ── played all cards ──────────────────────────────────────────
            if "played all cards" in body and cur_turn is not None:
                cur_turn.played_all = True
                continue

            # ── ROUND N OVER ──────────────────────────────────────────────
            rover_m = re.match(r'ROUND\s+(\d+)\s+OVER\s*\|\s*Winner:\s*(.+)', body)
            if rover_m:
                flush_turn()
                cur_round.winner = rover_m.group(2).strip()
                continue

            # ── penalty line ──────────────────────────────────────────────
            pen_m = re.match(r'(.+?):\s+hand=.*penalty=\+(\d+)', body)
            if pen_m and cur_round is not None:
                pname = pen_m.group(1).strip()
                pen = int(pen_m.group(2))
                cur_round.penalties[pname] = pen
                continue

            # ── scores line ───────────────────────────────────────────────
            if body.startswith("Scores:") and cur_round is not None:
                for sm in re.finditer(r'(\S+)\s+(\d+)/100', body):
                    cur_round.scores[sm.group(1)] = int(sm.group(2))
                cur_round.complete = cur_round.winner is not None
                continue

            # ── GAME OVER ─────────────────────────────────────────────────
            go_m = re.match(r'GAME OVER\s*\|\s*Winner:\s*(\S+)', body)
            if go_m:
                cur_session.game_over = True
                cur_session.game_winner = go_m.group(1).strip()
                for sm in re.finditer(r'(\S+)\s+(\d+)/100', body):
                    cur_session.final_scores[sm.group(1)] = int(sm.group(2))
                continue

    flush_session()
    return sessions, anti_feed_events


# ── post-parse enrichment ────────────────────────────────────────────────────

def enrich_sessions(sessions):
    for sess in sessions:
        all_players = set()
        for rnd in sess.rounds:
            for t in rnd.turns:
                all_players.add(t.player)
            all_players.update(rnd.cpu_players)

        # Human = any player who appeared without a (CPU) tag in their dealt-hand line.
        # sess.cpu_names is already populated from those tags during parsing.
        for pn in all_players:
            if pn not in sess.cpu_names:
                sess.human_names.add(pn)

        for rnd in sess.rounds:
            for pn in all_players:
                if pn not in sess.human_names:
                    rnd.cpu_players.add(pn)


def is_session_complete(sess):
    if sess.game_over:
        return True
    if not sess.rounds:
        return False
    last_rnd = sess.rounds[-1]
    if not last_rnd.scores:
        return False
    under_100 = [p for p, s in last_rnd.scores.items() if s < 100]
    return len(under_100) == 1


def session_exclusion_reason(sess):
    if sess.edge_case:
        return "edge_case"
    complete_rounds = [r for r in sess.rounds if r.complete]
    if len(complete_rounds) < 2:
        return "fewer_than_2_complete_rounds"
    if not is_session_complete(sess):
        return "incomplete"
    last_rnd = sess.rounds[-1]
    if last_rnd.scores:
        under_100 = [p for p, s in last_rnd.scores.items() if s < 100]
        if len(under_100) > 1:
            return "multiple_players_under_100_at_end"
    return None


def is_simulation(sess):
    humans = sess.human_names
    if not humans:
        return False
    for rnd in sess.rounds[:-1]:
        for pn in humans:
            if pn in rnd.scores and rnd.scores[pn] >= 100:
                return True
    return False


# ── DFD conversion ────────────────────────────────────────────────────────────

def dfd_converted(turn):
    if turn.draw_source != "discard":
        return False
    if turn.returned_to_deck:
        return True  # legally returned — not a wasted draw
    drawn = turn.draw_card
    for (cards, _) in turn.laid_down:
        if drawn in cards:
            return True
    for (card, _) in turn.added_to_table:
        if card == drawn:
            return True
    return False


# ── main analysis ─────────────────────────────────────────────────────────────

def analyse(sessions, anti_feed_events):
    excluded = defaultdict(list)
    valid_sessions = []

    for sess in sessions:
        reason = session_exclusion_reason(sess)
        if reason:
            excluded[reason].append(sess.session_id)
        else:
            valid_sessions.append(sess)

    valid_sids = {s.session_id for s in valid_sessions}
    af_valid = [e for e in anti_feed_events if e["session"] in valid_sids]

    # per-bucket accumulators
    bk = {"human": defaultdict(list), "cpu": defaultdict(list)}
    # per-player accumulators
    pp = defaultdict(lambda: defaultdict(list))

    sess_metrics = []

    for sess in valid_sessions:
        sim = is_simulation(sess)
        complete_rounds = [r for r in sess.rounds if r.complete]
        winner = sess.game_winner or (complete_rounds[-1].winner if complete_rounds else None)
        winner_bucket = "human" if winner in sess.human_names else ("cpu" if winner else "unknown")

        sess_metrics.append({
            "id": sess.session_id,
            "player_count": sess.player_count,
            "difficulty": sess.difficulty,
            "round_count": len(complete_rounds),
            "winner": winner,
            "winner_bucket": winner_bucket,
            "is_simulation": sim,
            "human_names": sorted(sess.human_names),
        })

        def bucket(p, _hn=sess.human_names):
            return "human" if p in _hn else "cpu"

        for rnd in complete_rounds:
            first_player = rnd.first_player
            first_laydown_turn = {}  # player -> turn_num

            # pass 1: turns
            for turn in rnd.turns:
                p = turn.player
                b = bucket(p)

                if turn.laid_down and p not in first_laydown_turn:
                    first_laydown_turn[p] = turn.turn_num

                # draws
                if turn.draw_source:
                    bk[b]["total_draws"].append(1)
                    pp[p]["total_draws"].append(1)
                    if turn.draw_source == "discard":
                        bk[b]["dfd_draws"].append(1)
                        pp[p]["dfd_draws"].append(1)
                        conv = int(dfd_converted(turn))
                        bk[b]["dfd_converted"].append(conv)
                        pp[p]["dfd_converted"].append(conv)
                    else:
                        bk[b]["deck_draws"].append(1)
                        pp[p]["deck_draws"].append(1)

                # laydowns
                if turn.laid_down:
                    bk[b]["laydowns"].append(1)
                    pp[p]["laydowns"].append(1)
                    has_joker = any(
                        card_rank(c) == "JKR"
                        for cards, _ in turn.laid_down
                        for c in cards
                    )
                    if has_joker:
                        bk[b]["joker_laydowns"].append(1)
                        pp[p]["joker_laydowns"].append(1)
                    if len(turn.laid_down) > 1:
                        bk[b]["multi_laydowns"].append(1)
                        pp[p]["multi_laydowns"].append(1)

                # joker swaps
                if turn.swapped_joker:
                    bk[b]["joker_swaps"].append(len(turn.swapped_joker))
                    pp[p]["joker_swaps"].append(len(turn.swapped_joker))

                # adds to table
                if turn.added_to_table:
                    bk[b]["adds_to_table"].append(len(turn.added_to_table))
                    pp[p]["adds_to_table"].append(len(turn.added_to_table))

                # second draw (only turn 1 of first player)
                if p == first_player and turn.turn_num == 1:
                    bk[b]["first_to_act_rounds"].append(1)
                    pp[p]["first_to_act_rounds"].append(1)
                    if turn.second_draw_used:
                        bk[b]["second_draw_used"].append(1)
                        pp[p]["second_draw_used"].append(1)

                # high-tier discard before first laydown
                if turn.discarded_card and p not in first_laydown_turn:
                    bk[b]["total_discard_pre_laydown"].append(1)
                    pp[p]["total_discard_pre_laydown"].append(1)
                    if is_high_tier(turn.discarded_card):
                        bk[b]["high_tier_discard_pre_laydown"].append(1)
                        pp[p]["high_tier_discard_pre_laydown"].append(1)

            # pass 2: round-level per-player
            for pen_player, pen in rnd.penalties.items():
                b = bucket(pen_player)
                is_winner = (pen_player == rnd.winner)

                bk[b]["rounds_played"].append(1)
                pp[pen_player]["rounds_played"].append(1)

                if is_winner:
                    bk[b]["rounds_won"].append(1)
                    pp[pen_player]["rounds_won"].append(1)
                else:
                    bk[b]["rounds_lost"].append(1)
                    pp[pen_player]["rounds_lost"].append(1)
                    bk[b]["penalties_when_losing"].append(pen)
                    pp[pen_player]["penalties_when_losing"].append(pen)
                    if pen >= 50:
                        bk[b]["penalty_gte50"].append(1)
                        pp[pen_player]["penalty_gte50"].append(1)
                    if pen >= 30:
                        bk[b]["penalty_gte30"].append(1)
                        pp[pen_player]["penalty_gte30"].append(1)

                bk[b]["penalties"].append(pen)
                pp[pen_player]["penalties"].append(pen)

                # end-h from last discard turn
                last_h = None
                for turn in reversed(rnd.turns):
                    if turn.player == pen_player and turn.discard_h is not None:
                        last_h = turn.discard_h
                        break
                if last_h is not None and not is_winner:
                    bk[b]["end_h_values_losing"].append(last_h)
                    pp[pen_player]["end_h_values_losing"].append(last_h)

                had_ld = pen_player in first_laydown_turn
                bk[b]["rounds_with_laydown_possible"].append(1)
                pp[pen_player]["rounds_with_laydown_possible"].append(1)
                if had_ld:
                    bk[b]["rounds_had_laydown"].append(1)
                    pp[pen_player]["rounds_had_laydown"].append(1)

            # first-laydown turn numbers
            for p, tn in first_laydown_turn.items():
                b = bucket(p)
                bk[b]["first_laydown_turns"].append(tn)
                pp[p]["first_laydown_turns"].append(tn)

        # game-level
        if winner:
            b = bucket(winner)
            bk[b]["games_won"].append(1)
        if sess.human_names:
            bk["human"]["games_played"].append(1)
        if sess.cpu_names:
            bk["cpu"]["games_played"].append(1)

    # ── urgency (opponent h<=4) ───────────────────────────────────────────────
    urg = {k: {"human": 0, "cpu": 0}
           for k in ["total", "dfd", "laydown", "low_discard"]}

    for sess in valid_sessions:
        def bucket(p, _hn=sess.human_names):
            return "human" if p in _hn else "cpu"

        for rnd in [r for r in sess.rounds if r.complete]:
            hand_size = {}
            for turn in sorted(rnd.turns, key=lambda t: t.turn_num):
                p = turn.player
                opp_urgent = any(
                    hand_size.get(op, 9) <= 4
                    for op in hand_size if op != p
                )
                b = bucket(p)
                if opp_urgent and turn.draw_source:
                    urg["total"][b] += 1
                    if turn.draw_source == "discard":
                        urg["dfd"][b] += 1
                    if turn.laid_down:
                        urg["laydown"][b] += 1
                    if turn.discarded_card and not is_high_tier(turn.discarded_card):
                        urg["low_discard"][b] += 1
                if turn.discard_h is not None:
                    hand_size[p] = turn.discard_h

    # ── round length ─────────────────────────────────────────────────────────
    rl = defaultdict(list)
    stalled_rounds = []
    explosive_rounds = []

    for sess in valid_sessions:
        for rnd in [r for r in sess.rounds if r.complete]:
            n_turns = len(rnd.turns)
            rl[sess.player_count].append(n_turns)

            # stalled: max consecutive turns with no lay-down
            consec = 0
            max_consec = 0
            for t in sorted(rnd.turns, key=lambda x: x.turn_num):
                if t.laid_down:
                    consec = 0
                else:
                    consec += 1
                    max_consec = max(max_consec, consec)
            if max_consec >= 8:
                stalled_rounds.append((sess.session_id, rnd.round_num, max_consec))
            if n_turns <= 3:
                explosive_rounds.append((sess.session_id, rnd.round_num, n_turns))

    # ── CPU improvement signals ───────────────────────────────────────────────
    sigs = {
        "second_draw_missed": [],
        "anti_feed_failures": [],
        "dfd_wasted": [],
        "high_card_retention": [],
        "urgency_miss": [],
    }

    for sess in valid_sessions:
        def bucket_local(p, _hn=sess.human_names):
            return "human" if p in _hn else "cpu"

        for rnd in [r for r in sess.rounds if r.complete]:
            hand_size_sig = {}
            first_player = rnd.first_player

            for turn in sorted(rnd.turns, key=lambda t: t.turn_num):
                p = turn.player
                is_cpu = p not in sess.human_names

                # second draw missed
                if (is_cpu and p == first_player and turn.turn_num == 1
                        and not turn.second_draw_used
                        and turn.discarded_card
                        and is_high_tier(turn.discarded_card)):
                    sigs["second_draw_missed"].append({
                        "session": sess.session_id, "round": rnd.round_num,
                        "turn": turn.turn_num, "player": p,
                        "discarded": turn.discarded_card,
                    })

                # DFD wasted
                if (is_cpu and turn.draw_source == "discard"
                        and not turn.laid_down
                        and not turn.added_to_table
                        and not turn.returned_to_deck):
                    sigs["dfd_wasted"].append({
                        "session": sess.session_id, "round": rnd.round_num,
                        "turn": turn.turn_num, "player": p,
                        "draw_card": turn.draw_card,
                    })

                # urgency miss
                opp_urgent_sig = any(
                    hand_size_sig.get(op, 9) <= 4
                    for op in hand_size_sig if op != p
                )
                if (is_cpu and opp_urgent_sig
                        and turn.draw_source == "deck"
                        and turn.discarded_card
                        and is_high_tier(turn.discarded_card)):
                    sigs["urgency_miss"].append({
                        "session": sess.session_id, "round": rnd.round_num,
                        "turn": turn.turn_num, "player": p,
                        "discarded": turn.discarded_card,
                    })

                if turn.discard_h is not None:
                    hand_size_sig[p] = turn.discard_h

            # high-card retention
            for pen_player, pen in rnd.penalties.items():
                if pen_player in sess.human_names or pen < 30:
                    continue
                low_tier_d = [
                    turn.discarded_card
                    for turn in rnd.turns
                    if turn.player == pen_player
                    and turn.discarded_card
                    and not is_high_tier(turn.discarded_card)
                ]
                if low_tier_d:
                    sigs["high_card_retention"].append({
                        "session": sess.session_id, "round": rnd.round_num,
                        "player": pen_player, "penalty": pen,
                        "low_tier_discards": low_tier_d[:3],
                    })

    # anti-feed with combo type
    for e in af_valid:
        sid, rn, tn = e["session"], e["round"], e["turn"]
        ct = None
        for sess in valid_sessions:
            if sess.session_id != sid:
                continue
            for rnd in sess.rounds:
                if rnd.round_num != rn:
                    continue
                for turn in rnd.turns:
                    if turn.turn_num == tn and turn.laid_down:
                        ct = turn.laid_down[0][1]
        sigs["anti_feed_failures"].append({**e, "combo_type_used": ct})

    # ── difficulty / bracket stats ────────────────────────────────────────────
    diff_stats = defaultdict(lambda: {"sessions": 0, "rounds": 0,
                                       "human_wins": 0, "cpu_wins": 0,
                                       "total_turns": 0})
    for sm in sess_metrics:
        d = sm["difficulty"]
        diff_stats[d]["sessions"] += 1
        diff_stats[d]["rounds"] += sm["round_count"]
        if sm["winner_bucket"] == "human":
            diff_stats[d]["human_wins"] += 1
        elif sm["winner_bucket"] == "cpu":
            diff_stats[d]["cpu_wins"] += 1

    for sess in valid_sessions:
        for rnd in [r for r in sess.rounds if r.complete]:
            diff_stats[sess.difficulty]["total_turns"] += len(rnd.turns)

    bracket_stats = {b: {"human": 0, "cpu": 0, "total": 0}
                     for b in ["2p", "3-4p", "5-8p"]}
    for sm in sess_metrics:
        pc = sm["player_count"]
        bk_name = "2p" if pc == 2 else ("3-4p" if pc <= 4 else "5-8p")
        bracket_stats[bk_name]["total"] += 1
        if sm["winner_bucket"] == "human":
            bracket_stats[bk_name]["human"] += 1
        elif sm["winner_bucket"] == "cpu":
            bracket_stats[bk_name]["cpu"] += 1

    all_human_names = set()
    for sess in valid_sessions:
        all_human_names.update(sess.human_names)

    return {
        "sessions": sess_metrics,
        "excluded": excluded,
        "buckets": bk,
        "per_player": pp,
        "anti_feed": af_valid,
        "cpu_signals": sigs,
        "urgency": urg,
        "round_lengths": rl,
        "stalled": stalled_rounds,
        "explosive": explosive_rounds,
        "diff_stats": dict(diff_stats),
        "bracket_stats": bracket_stats,
        "valid_session_count": len(valid_sessions),
        "total_session_count": len(sessions),
        "all_human_names": all_human_names,
    }


# ── print helpers ─────────────────────────────────────────────────────────────

def pct(num, denom, dec=1):
    if denom == 0:
        return "n/a"
    return f"{100*num/denom:.{dec}f}%"


def avg(lst, dec=2):
    if not lst:
        return "n/a"
    return f"{sum(lst)/len(lst):.{dec}f}"


def _sum(d, key):
    return sum(d.get(key, []))


def _max(d, key):
    lst = d.get(key, [])
    return max(lst) if lst else "n/a"


def header(title, w=72):
    print()
    print("=" * w)
    print(f"  {title}")
    print("=" * w)


def sub(title, w=60):
    print(f"\n  {'─'*4} {title}")


# ── print report ──────────────────────────────────────────────────────────────

def print_report(data):
    sess_metrics = data["sessions"]
    excl = data["excluded"]
    bk = data["buckets"]
    pp = data["per_player"]
    af = data["anti_feed"]
    sigs = data["cpu_signals"]
    urg = data["urgency"]
    rl = data["round_lengths"]
    diff = data["diff_stats"]
    bracket = data["bracket_stats"]
    all_human_names = data.get("all_human_names", set())

    print()
    print("╔══════════════════════════════════════════════════════════════════════╗")
    print("║              PONTINHO GAME LOG COMPREHENSIVE ANALYSIS               ║")
    print("╚══════════════════════════════════════════════════════════════════════╝")
    print(f"  Total sessions parsed : {data['total_session_count']}")
    print(f"  Valid sessions        : {data['valid_session_count']}")

    # ── SESSION LIST ──────────────────────────────────────────────────────────
    header("SESSION LIST")
    print(f"  {'Session-ID':<12} {'Plrs':>4}  {'Difficulty':<12} {'Rounds':>6}  {'Winner':<16} {'Type':<6}  Sim?")
    print("  " + "-"*70)
    for sm in sess_metrics:
        sid = sm["id"][:12]
        win = (sm["winner"] or "?")[:16]
        sim_s = "YES" if sm["is_simulation"] else "-"
        print(f"  {sid:<12} {sm['player_count']:>4}  {sm['difficulty']:<12} {sm['round_count']:>6}"
              f"  {win:<16} {sm['winner_bucket']:<6}  {sim_s}")

    # ── EXCLUDED SESSIONS ─────────────────────────────────────────────────────
    header("EXCLUDED SESSIONS")
    labels = {
        "edge_case": "Has [EDGE CASE] line",
        "fewer_than_2_complete_rounds": "Fewer than 2 complete rounds",
        "incomplete": "Incomplete game (no GAME OVER, not single survivor)",
        "multiple_players_under_100_at_end": "Multiple players under 100 at end",
    }
    total_excl = sum(len(v) for v in excl.values())
    print(f"  Total excluded: {total_excl}")
    for reason, ids in sorted(excl.items()):
        lbl = labels.get(reason, reason)
        sample = ids[:6]
        print(f"  {lbl}: {len(ids)}")
        print(f"    IDs: {', '.join(sample)}" + ("…" if len(ids) > 6 else ""))

    # ── AGGREGATE STATS ───────────────────────────────────────────────────────
    human_label = ", ".join(sorted(all_human_names)) or "none detected"
    header(f"AGGREGATE STATS  (Human = {human_label}  |  CPU = all others)")

    for bucket_name in ["human", "cpu"]:
        b = bk[bucket_name]
        print(f"\n  ┌── Bucket: {bucket_name.upper()} ──")
        rounds_played  = _sum(b, "rounds_played")
        rounds_won     = _sum(b, "rounds_won")
        rounds_lost    = _sum(b, "rounds_lost")
        games_played   = _sum(b, "games_played")
        games_won      = _sum(b, "games_won")
        total_draws    = _sum(b, "total_draws")
        dfd_draws      = _sum(b, "dfd_draws")
        dfd_conv       = _sum(b, "dfd_converted")
        laydowns       = _sum(b, "laydowns")
        multi_ld       = _sum(b, "multi_laydowns")
        joker_ld       = _sum(b, "joker_laydowns")
        swaps          = _sum(b, "joker_swaps")
        adds           = _sum(b, "adds_to_table")
        first_to_act   = _sum(b, "first_to_act_rounds")
        second_draw    = _sum(b, "second_draw_used")
        end_h          = b.get("end_h_values_losing", [])
        ld_turns       = b.get("first_laydown_turns", [])
        rds_had_ld     = _sum(b, "rounds_had_laydown")
        rds_ld_poss    = _sum(b, "rounds_with_laydown_possible")
        pen_all        = b.get("penalties", [])
        pen_losing     = b.get("penalties_when_losing", [])
        pen_gte50      = _sum(b, "penalty_gte50")
        pen_gte30      = _sum(b, "penalty_gte30")
        htdp           = _sum(b, "high_tier_discard_pre_laydown")
        tdp            = _sum(b, "total_discard_pre_laydown")

        print(f"  │  Round win rate              : {pct(rounds_won, rounds_played)}"
              f"  ({rounds_won}/{rounds_played})")
        print(f"  │  Game win rate               : {pct(games_won, games_played)}"
              f"  ({games_won}/{games_played})")
        print(f"  │  Avg penalty / round         : {avg(pen_all, 1)}")
        print(f"  │  Avg penalty (not winning)   : {avg(pen_losing, 1)}")
        print(f"  │  Max penalty in a round      : {_max({'x': pen_all}, 'x') if pen_all else 'n/a'}")
        print(f"  │  % rounds h>=7 at end (losing): {pct(sum(1 for h in end_h if h >= 7), len(end_h))}")
        print(f"  │  % rounds penalty >= 50      : {pct(pen_gte50, rounds_lost)}")
        print(f"  │  % rounds penalty >= 30      : {pct(pen_gte30, rounds_lost)}")
        print(f"  │  Second-draw usage rate      : {pct(second_draw, first_to_act)}"
              f"  ({second_draw}/{first_to_act} first-to-act rounds)")
        print(f"  │  Draw-from-discard rate      : {pct(dfd_draws, total_draws)}"
              f"  ({dfd_draws}/{total_draws})")
        print(f"  │  DFD conversion rate         : {pct(dfd_conv, dfd_draws)}"
              f"  ({dfd_conv}/{dfd_draws} DFD draws)")
        print(f"  │  Avg turn of first lay-down  : {avg(ld_turns, 1)}")
        print(f"  │  % rounds with any lay-down  : {pct(rds_had_ld, rds_ld_poss)}")
        print(f"  │  % lay-downs that are multi  : {pct(multi_ld, laydowns)}")
        print(f"  │  % lay-downs involving joker : {pct(joker_ld, laydowns)}")
        print(f"  │  Anti-feed failure rate (vs draws proxy): ", end="")
        af_bucket = sum(1 for e in af if e["discarder_is_human"] == (bucket_name == "human"))
        print(f"{pct(af_bucket, max(total_draws,1))}  ({af_bucket} events / {total_draws} draws)")
        print(f"  │  % high-tier discards pre-ld  : {pct(htdp, tdp)}")
        print(f"  │  Joker swaps (total events)   : {swaps}")
        print(f"  │  Cards added to table (total) : {adds}")

        u_t = urg["total"][bucket_name]
        u_d = urg["dfd"][bucket_name]
        u_l = urg["laydown"][bucket_name]
        u_lo = urg["low_discard"][bucket_name]
        print(f"  │  [Opp h<=4] DFD rate          : {pct(u_d, u_t)}  ({u_d}/{u_t})")
        print(f"  │  [Opp h<=4] Lay-down rate      : {pct(u_l, u_t)}  ({u_l}/{u_t})")
        print(f"  │  [Opp h<=4] Low-tier discard   : {pct(u_lo, u_t)}  ({u_lo}/{u_t})")
        print(f"  └─────────────────────────────────────────────────────")

    # ── ANTI-FEED FAILURES TABLE ───────────────────────────────────────────────
    header("ANTI-FEED FAILURES  (all valid sessions, top 25 shown)")
    print(f"  Total events: {len(af)}")
    if af:
        print(f"  {'Session':<10} {'Rnd':>4} {'Tn':>4}  {'Discarder':<14} H/C  {'Card':<8}  {'Drawer':<14} H/C")
        print("  " + "-"*72)
        for e in af[:25]:
            dc = "H" if e["discarder_is_human"] else "C"
            dr = "H" if e["drawer_is_human"] else "C"
            print(f"  {e['session']:<10} {e['round']:>4} {e['turn']:>4}  "
                  f"{e['discarder']:<14}  {dc}   {e['card']:<8}  "
                  f"{e['drawer']:<14}  {dr}")

    # ── PER-PLAYER SUMMARY ────────────────────────────────────────────────────
    header("PER-PLAYER SUMMARY")

    human_players = sorted(p for p in pp if p in all_human_names)
    cpu_all = [p for p in pp if p not in all_human_names]

    def print_player(label, pdata):
        rds   = _sum(pdata, "rounds_played")
        won   = _sum(pdata, "rounds_won")
        los   = _sum(pdata, "rounds_lost")
        draws = _sum(pdata, "total_draws")
        dfd   = _sum(pdata, "dfd_draws")
        dfd_c = _sum(pdata, "dfd_converted")
        ld    = _sum(pdata, "laydowns")
        mld   = _sum(pdata, "multi_laydowns")
        jld   = _sum(pdata, "joker_laydowns")
        sw    = _sum(pdata, "joker_swaps")
        adds  = _sum(pdata, "adds_to_table")
        fa    = _sum(pdata, "first_to_act_rounds")
        sd    = _sum(pdata, "second_draw_used")
        pen   = pdata.get("penalties", [])
        pen_l = pdata.get("penalties_when_losing", [])
        end_h = pdata.get("end_h_values_losing", [])
        ld_ts = pdata.get("first_laydown_turns", [])
        rds_ld = _sum(pdata, "rounds_had_laydown")
        rds_lp = _sum(pdata, "rounds_with_laydown_possible")
        htdp  = _sum(pdata, "high_tier_discard_pre_laydown")
        tdp   = _sum(pdata, "total_discard_pre_laydown")
        p50   = _sum(pdata, "penalty_gte50")
        p30   = _sum(pdata, "penalty_gte30")

        print(f"\n  ▶ {label}")
        print(f"    Rounds played/won           : {rds} / {won}  ({pct(won, rds)} win rate)")
        print(f"    Avg penalty / round         : {avg(pen, 1)}")
        print(f"    Avg penalty when losing     : {avg(pen_l, 1)}")
        print(f"    Max penalty                 : {max(pen) if pen else 'n/a'}")
        print(f"    % penalty >= 50             : {pct(p50, los)}")
        print(f"    % penalty >= 30             : {pct(p30, los)}")
        print(f"    % h>=7 at end (losing rds)  : {pct(sum(1 for h in end_h if h >= 7), len(end_h))}")
        print(f"    DFD rate                    : {pct(dfd, draws)}  ({dfd}/{draws})")
        print(f"    DFD conversion rate         : {pct(dfd_c, dfd)}")
        print(f"    Second-draw usage           : {pct(sd, fa)}  ({sd}/{fa})")
        print(f"    Avg turn of first lay-down  : {avg(ld_ts, 1)}")
        print(f"    % rounds with lay-down      : {pct(rds_ld, rds_lp)}")
        print(f"    % lay-downs multi-combo     : {pct(mld, ld)}")
        print(f"    % lay-downs with joker      : {pct(jld, ld)}")
        print(f"    High-tier discard pre-ld    : {pct(htdp, tdp)}")
        print(f"    Joker swaps (total)         : {sw}")
        print(f"    Cards added to table (total): {adds}")

    for p in human_players:
        print_player(p, pp[p])

    # CPU aggregate
    cpu_agg = defaultdict(list)
    for p in cpu_all:
        for k, v in pp[p].items():
            cpu_agg[k].extend(v)
    print_player("CPU-AGGREGATE (all CPUs combined)", cpu_agg)

    # ── DIFFICULTY BREAKDOWN ──────────────────────────────────────────────────
    header("DIFFICULTY BREAKDOWN")
    print(f"  {'Difficulty':<14} {'Sess':>5} {'Rounds':>7} {'Human Win%':>11}"
          f" {'CPU Win%':>9} {'Avg Turns/Rnd':>14}  Verdict")
    print("  " + "-"*72)
    for d, ds in sorted(diff.items()):
        if ds["sessions"] == 0:
            continue
        hw = pct(ds["human_wins"], ds["sessions"])
        cw = pct(ds["cpu_wins"], ds["sessions"])
        at = (f"{ds['total_turns']/ds['rounds']:.1f}"
              if ds["rounds"] else "n/a")
        verdict = ("Human-favoured" if ds["human_wins"] > ds["cpu_wins"]
                   else "CPU-favoured" if ds["cpu_wins"] > ds["human_wins"]
                   else "Even")
        print(f"  {d:<14} {ds['sessions']:>5} {ds['rounds']:>7} {hw:>11}"
              f" {cw:>9} {at:>14}  {verdict}")

    # ── PLAYER COUNT EFFECTS ──────────────────────────────────────────────────
    header("PLAYER COUNT EFFECTS — Human Win Rate by Bracket")
    print(f"  {'Bracket':<10} {'Sessions':>8} {'Human Wins':>11} {'Human Win%':>12}")
    print("  " + "-"*46)
    for bk_name in ["2p", "3-4p", "5-8p"]:
        bs = bracket[bk_name]
        print(f"  {bk_name:<10} {bs['total']:>8} {bs['human']:>11}"
              f" {pct(bs['human'], bs['total']):>12}")

    # ── ROUND LENGTH ──────────────────────────────────────────────────────────
    header("ROUND LENGTH ANALYSIS")
    print(f"  {'Player Count':<14} {'Avg Turns':>10} {'Total Rounds':>13}")
    print("  " + "-"*40)
    for pc in sorted(rl.keys()):
        tl = rl[pc]
        print(f"  {str(pc)+'p':<14} {avg(tl, 1):>10} {len(tl):>13}")

    print(f"\n  Stalled rounds (8+ consecutive turns, no lay-down): {len(data['stalled'])}")
    for s in data["stalled"][:15]:
        print(f"    Session {s[0]}  Round {s[1]}  (streak: {s[2]} turns)")

    print(f"\n  Explosive rounds (<= 3 turns): {len(data['explosive'])}")
    for s in data["explosive"][:15]:
        print(f"    Session {s[0]}  Round {s[1]}  ({s[2]} turns)")

    # ── CPU IMPROVEMENT SIGNALS ───────────────────────────────────────────────
    header("CPU IMPROVEMENT SIGNALS")

    sub("1. Second-Draw Missed  (CPU first-to-act, no 2nd draw used, high-tier discard)")
    items = sigs["second_draw_missed"]
    print(f"  Count: {len(items)}")
    for it in items[:20]:
        print(f"  [S:{it['session']}  R{it['round']}  T{it['turn']}]"
              f"  {it['player']:<14}  discarded: {it['discarded']}")

    sub("2. Anti-Feed Failures with Combo Context")
    items2 = sigs["anti_feed_failures"]
    print(f"  Count: {len(items2)}")
    for it in items2[:20]:
        ct = it.get("combo_type_used") or "no-laydown"
        print(f"  [S:{it['session']}  R{it['round']}  T{it['turn']}]"
              f"  {it['discarder']:<12} discarded {it['card']}"
              f" → {it['drawer']:<12} drew  (drawer used: {ct})")

    sub("3. DFD Wasted  (CPU drew from discard but no lay-down AND no add-to-table same turn)")
    items3 = sigs["dfd_wasted"]
    print(f"  Count: {len(items3)}")
    for it in items3[:20]:
        print(f"  [S:{it['session']}  R{it['round']}  T{it['turn']}]"
              f"  {it['player']:<14}  drew: {it['draw_card']}")

    sub("4. High-Card Retention  (CPU penalty>=30, discarded low-tier cards)")
    items4 = sigs["high_card_retention"]
    print(f"  Count: {len(items4)}")
    for it in items4[:20]:
        lo = ", ".join(it["low_tier_discards"])
        print(f"  [S:{it['session']}  R{it['round']}]"
              f"  {it['player']:<14}  pen={it['penalty']}  low-tier discards: {lo}")

    sub("5. Urgency Miss  (opp h<=4, CPU drew from deck, discarded high-tier)")
    items5 = sigs["urgency_miss"]
    print(f"  Count: {len(items5)}")
    for it in items5[:20]:
        print(f"  [S:{it['session']}  R{it['round']}  T{it['turn']}]"
              f"  {it['player']:<14}  discarded: {it['discarded']}")

    print()
    print("=" * 72)
    print("  END OF REPORT")
    print("=" * 72)


# ── entry point ───────────────────────────────────────────────────────────────

def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    if not os.path.exists(LOG_PATH):
        print(f"ERROR: log file not found: {LOG_PATH}", file=sys.stderr)
        sys.exit(1)

    print("Parsing log …", file=sys.stderr)
    sessions, anti_feed = parse_log(LOG_PATH)
    print(f"  {len(sessions)} session blocks found.", file=sys.stderr)

    enrich_sessions(sessions)

    # Resolve is_human flags on anti-feed events now that cpu_names are finalised
    for e in anti_feed:
        sess_ref = e.pop("session_ref", None)
        if sess_ref is not None:
            e["discarder_is_human"] = e["discarder"] not in sess_ref.cpu_names
            e["drawer_is_human"]    = e["drawer"]    not in sess_ref.cpu_names
        else:
            e["discarder_is_human"] = False
            e["drawer_is_human"]    = False

    print("Analysing …", file=sys.stderr)
    result = analyse(sessions, anti_feed)

    print_report(result)


if __name__ == "__main__":
    main()
