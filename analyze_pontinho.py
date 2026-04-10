import re
from collections import defaultdict

with open("C:/Users/mgrisa/Documents/test/CardGames/bin/Debug/net10.0/logs/game_log.txt", 'r', encoding='utf-8') as f:
    lines = f.readlines()

def parse_scores(score_line):
    return re.findall(r'(\S+)\s+(\d+)/100', score_line)

def is_complete(scores):
    if not scores:
        return False, None
    below = [(n, int(s)) for n, s in scores if int(s) < 100]
    if len(below) == 1:
        return True, below[0][0]
    elif len(below) == 0:
        return True, "all-eliminated"
    return False, None

sessions = []
current_session = None
last_scores_line = None
session_rounds = []
session_events = []

for i, line in enumerate(lines):
    ln = i + 1
    stripped = line.strip()

    if 'NEW GAME' in stripped:
        if current_session:
            current_session['last_scores'] = last_scores_line
            current_session['rounds'] = list(session_rounds)
            current_session['events'] = list(session_events)
            current_session['end_line'] = ln - 1
            sessions.append(current_session)

        m_sid = re.search(r'Session: ([A-F0-9]+)', stripped)
        m_players = re.search(r'Players: (\d+)', stripped)
        m_diff = re.search(r'Difficulty: (\w+)', stripped)

        sid = m_sid.group(1) if m_sid else 'no-ID'
        players = int(m_players.group(1)) if m_players else 0
        diff = m_diff.group(1) if m_diff else 'Unknown'

        current_session = {
            'id': sid, 'players': players, 'difficulty': diff,
            'start_line': ln, 'rounds': [], 'events': [], 'last_scores': None
        }
        session_rounds = []
        session_events = []
        last_scores_line = None

    if current_session:
        if 'ROUND' in stripped and 'OVER' in stripped:
            m = re.search(r'ROUND (\d+) OVER.*Winner: (.+)', stripped)
            if m:
                session_rounds.append({'round': int(m.group(1)), 'winner': m.group(2).strip(), 'line': ln})
        if 'Scores:' in stripped:
            last_scores_line = stripped
        session_events.append({'line': ln, 'text': stripped})

if current_session:
    current_session['last_scores'] = last_scores_line
    current_session['rounds'] = session_rounds
    current_session['events'] = session_events
    current_session['end_line'] = len(lines)
    sessions.append(current_session)

important = [s for s in sessions if s['players'] > 2 or (s['players'] == 2 and s['difficulty'] == 'VeryHard')]

complete = []
for s in important:
    nr = len(s['rounds'])
    if nr < 2:
        continue
    if not s['last_scores']:
        continue
    scores = parse_scores(s['last_scores'])
    ok, survivor = is_complete(scores)
    if ok:
        complete.append((s, survivor, scores))

HUMAN_PLAYERS = {'MAURICIO', 'IRIA'}

total_rounds = 0
human_wins = 0
cpu_wins = 0
total_2nd_draw_cpu = 0
total_2nd_draw_human = 0
draw_from_disc_events = []
anti_feed_failures = []
joker_swaps = 0
lay_down_events = []
session_summaries = []
player_round_wins = defaultdict(int)
player_rounds_played = defaultdict(int)
player_is_human = {}
penalty_by_player = defaultdict(list)
high_card_discards_2nd = []

for s, survivor, scores in complete:
    events = s['events']
    rounds = s['rounds']
    players_list = [n for n, sc in scores]

    session_summaries.append({
        'id': s['id'], 'players': s['players'], 'difficulty': s['difficulty'],
        'rounds': len(rounds), 'winner': survivor,
        'human_won': survivor in HUMAN_PLAYERS,
        'round_winners': [r['winner'] for r in rounds]
    })

    for p in players_list:
        player_rounds_played[p] += len(rounds)
        if p in HUMAN_PLAYERS:
            player_is_human[p] = True
        else:
            player_is_human[p] = False

    for r in rounds:
        total_rounds += 1
        w = r['winner']
        player_round_wins[w] += 1
        if w in HUMAN_PLAYERS:
            human_wins += 1
        else:
            cpu_wins += 1

    prev_discard_card = None
    prev_discard_player = None

    for ev in events:
        t = ev['text']

        # Second draw
        if 'drawing second' in t:
            m = re.match(r'(?:\[\w+\]\s+)?(\w+)\s+discarded first card: (\S+)', t)
            if m:
                name = m.group(1)
                card = m.group(2)
                high_ranks = {'J', 'Q', 'K', 'A'}
                is_high = any(card.startswith(r) for r in high_ranks)
                if name in HUMAN_PLAYERS:
                    total_2nd_draw_human += 1
                else:
                    total_2nd_draw_cpu += 1
                    if is_high:
                        high_card_discards_2nd.append({'session': s['id'], 'player': name, 'card': card, 'line': ev['line']})

        if 'drew from discard:' in t:
            m = re.search(r'(\w+)\s+drew from discard: (\S+)', t)
            if m:
                drawer = m.group(1)
                card = m.group(2)
                draw_from_disc_events.append({'session': s['id'], 'player': drawer, 'card': card, 'line': ev['line']})

                if prev_discard_card and prev_discard_card == card and prev_discard_player != drawer:
                    anti_feed_failures.append({
                        'session': s['id'],
                        'discarder': prev_discard_player,
                        'drawer': drawer,
                        'card': card,
                        'line': ev['line']
                    })
                prev_discard_card = None
                prev_discard_player = None

        m2 = re.search(r'(\w+)\s+discarded: (\S+)\s+\(h:(\d+)\)', t)
        if m2:
            prev_discard_player = m2.group(1)
            prev_discard_card = m2.group(2)

        if 'swapped' in t and 'for joker' in t:
            joker_swaps += 1

        if 'laid down:' in t:
            m3 = re.search(r'(\w+)\s+laid down:', t)
            if m3:
                lay_down_events.append({'session': s['id'], 'player': m3.group(1), 'line': ev['line']})

        m4 = re.search(r'(\w+):\s+hand=.*penalty=\+(\d+)', t)
        if m4:
            penalty_by_player[m4.group(1)].append(int(m4.group(2)))

print("=== COMPLETE SESSIONS: %d ===" % len(complete))
print()

print("=== SESSION INVENTORY (TABLE D) ===")
for ss in session_summaries:
    w = ss['winner']
    hw = 'H' if w in HUMAN_PLAYERS else 'CPU'
    print("[%s] %dp %s | %2dR | Winner: %s (%s)" % (ss['id'], ss['players'], ss['difficulty'], ss['rounds'], w, hw))

print()
print("=== AGGREGATE STATS ===")
print("Total rounds: %d" % total_rounds)
print("Human round wins: %d (%.1f%%)" % (human_wins, 100*human_wins/max(1,total_rounds)))
print("CPU round wins: %d (%.1f%%)" % (cpu_wins, 100*cpu_wins/max(1,total_rounds)))
print("Game (session) wins by humans: %d/%d (%.0f%%)" % (sum(1 for ss in session_summaries if ss['human_won']), len(session_summaries), 100*sum(1 for ss in session_summaries if ss['human_won'])/len(session_summaries)))
print("Second-draw used - Human: %d, CPU: %d" % (total_2nd_draw_human, total_2nd_draw_cpu))
print("Draw-from-discard events: %d" % len(draw_from_disc_events))
print("Anti-feed failures (consecutive draw): %d" % len(anti_feed_failures))
print("Joker swaps: %d" % joker_swaps)
print()

print("=== PER-PLAYER STATS (TABLE A) ===")
all_players = set(list(player_round_wins.keys()) + list(player_rounds_played.keys()))
for p in sorted(all_players, key=lambda x: -player_rounds_played.get(x,0)):
    rp = player_rounds_played[p]
    rw = player_round_wins[p]
    if rp < 3:
        continue
    win_pct = 100.0*rw/rp if rp > 0 else 0
    pens = penalty_by_player.get(p, [])
    avg_pen = sum(pens)/len(pens) if pens else 0
    max_pen = max(pens) if pens else 0
    h = 'HUMAN' if player_is_human.get(p, False) else 'CPU  '
    print("  %-15s [%s]: %4d rounds, %3d wins (%4.1f%%), avg_pen=%.1f, max_pen=%d" % (p, h, rp, rw, win_pct, avg_pen, max_pen))

print()
print("=== ANTI-FEED FAILURES (TABLE C) ===")
for af in anti_feed_failures:
    discarder_type = 'H' if af['discarder'] in HUMAN_PLAYERS else 'CPU'
    drawer_type = 'H' if af['drawer'] in HUMAN_PLAYERS else 'CPU'
    print("  [%s] L%d: %s(%s) discarded %s -> %s(%s) drew it" % (af['session'], af['line'], af['discarder'], discarder_type, af['card'], af['drawer'], drawer_type))

print()
print("=== DIFFICULTY BREAKDOWN (TABLE B) ===")
diff_stats = defaultdict(lambda: {'sessions': 0, 'rounds': 0, 'human_wins': 0, 'cpu_wins': 0})
for ss in session_summaries:
    d = ss['difficulty']
    diff_stats[d]['sessions'] += 1
    diff_stats[d]['rounds'] += ss['rounds']
    if ss['human_won']:
        diff_stats[d]['human_wins'] += 1
    else:
        diff_stats[d]['cpu_wins'] += 1

for d, ds in diff_stats.items():
    total = ds['human_wins'] + ds['cpu_wins']
    h_pct = 100.0*ds['human_wins']/total if total > 0 else 0
    print("  %s: %d sessions, %d rounds, human won %d/%d (%.0f%%)" % (d, ds['sessions'], ds['rounds'], ds['human_wins'], total, h_pct))

print()
print("=== PLAYER COUNT BREAKDOWN ===")
pc_stats = defaultdict(lambda: {'sessions': 0, 'human_wins': 0, 'total_rounds': 0})
for ss in session_summaries:
    pc = ss['players']
    pc_stats[pc]['sessions'] += 1
    pc_stats[pc]['total_rounds'] += ss['rounds']
    if ss['human_won']:
        pc_stats[pc]['human_wins'] += 1

for pc in sorted(pc_stats.keys()):
    ps = pc_stats[pc]
    total = ps['sessions']
    hwpct = 100.0*ps['human_wins']/total if total > 0 else 0
    avg_r = ps['total_rounds']/total if total > 0 else 0
    print("  %d players: %d sessions, human won %d/%d (%.0f%%), avg rounds=%.1f" % (pc, total, ps['human_wins'], total, hwpct, avg_r))

print()
print("=== CPU HIGH-CARD 2ND-DRAW DISCARDS (4a) ===")
print("CPU discarding high-tier cards on second-draw path:")
for ev in high_card_discards_2nd[:20]:
    print("  [%s] L%d: %s discarded %s (drew second)" % (ev['session'], ev['line'], ev['player'], ev['card']))

print()
print("=== DRAW-FROM-DISCARD BY PLAYER ===")
disc_by_player = defaultdict(int)
for ev in draw_from_disc_events:
    disc_by_player[ev['player']] += 1

for p, cnt in sorted(disc_by_player.items(), key=lambda x: -x[1]):
    if cnt >= 2:
        h = 'HUMAN' if player_is_human.get(p, False) else 'CPU'
        rp = player_rounds_played.get(p, 1)
        print("  %-15s [%s]: %d draws from discard in %d rounds (%.2f/round)" % (p, h, cnt, rp, cnt/rp))
