/**
 * k6 load test for CardGames (Blazor Server)
 *
 * Each virtual user:
 *   1. GET /health             — liveness probe
 *   2. GET /                   — initial HTML page load (verifies gzip, checks 200)
 *   3. POST /_blazor/negotiate — SignalR negotiation (gets connectionId)
 *   4. WebSocket /_blazor?id=… — full SignalR handshake + hold 5 s
 *
 * Stages: ramp 1→50 VUs over 20 s, hold 50 VUs for 60 s, ramp down 10 s.
 * Pass criteria: p95 latency < 2 s, error rate < 1 %.
 */

import http from "k6/http";
import ws   from "k6/ws";
import { check, sleep } from "k6";
import { Rate, Trend } from "k6/metrics";

export const options = {
  stages: [
    { duration: "20s", target: 50  },   // ramp up
    { duration: "60s", target: 50  },   // steady load
    { duration: "10s", target:  0  },   // ramp down
  ],
  thresholds: {
    http_req_failed:          ["rate<0.01"],   // < 1 % HTTP errors
    http_req_duration:        ["p(95)<2000"],  // p95 < 2 s
    "ws_connect_errors":      ["rate<0.05"],   // < 5 % WS failures
    "ws_handshake_duration":  ["p(95)<3000"],  // p95 handshake < 3 s
  },
};

const wsConnectErrors    = new Rate("ws_connect_errors");
const wsHandshakeTrend   = new Trend("ws_handshake_duration");

const BASE = "http://localhost:5000";
const WS   = "ws://localhost:5000";

// SignalR record separator (ASCII 0x1e)
const RS = String.fromCharCode(0x1e);

export default function () {
  // ── 1. Health check ──────────────────────────────────────────────────────
  const health = http.get(`${BASE}/health`);
  check(health, { "health 200": (r) => r.status === 200 });

  // ── 2. Main page ─────────────────────────────────────────────────────────
  const page = http.get(BASE, {
    headers: { "Accept-Encoding": "gzip, br" },
  });
  check(page, {
    "page 200":          (r) => r.status === 200,
    "page has blazor":   (r) => r.body.includes("_blazor"),
    "page compressed":   (r) => r.headers["Content-Encoding"] !== undefined,
  });

  // ── 3. SignalR negotiate ──────────────────────────────────────────────────
  const neg = http.post(
    `${BASE}/_blazor/negotiate?negotiateVersion=1`,
    null,
    { headers: { "Content-Type": "application/json" } }
  );
  const negOk = check(neg, {
    "negotiate 200":       (r) => r.status === 200,
    "negotiate has id":    (r) => {
      try { return JSON.parse(r.body).connectionToken !== undefined; }
      catch { return false; }
    },
  });

  if (!negOk) {
    wsConnectErrors.add(1);
    sleep(1);
    return;
  }

  let token;
  try { token = JSON.parse(neg.body).connectionToken; }
  catch { wsConnectErrors.add(1); sleep(1); return; }

  // ── 4. WebSocket + SignalR handshake ──────────────────────────────────────
  const t0 = Date.now();
  let handshakeDone = false;

  const res = ws.connect(
    `${WS}/_blazor?id=${encodeURIComponent(token)}`,
    {},
    function (socket) {
      socket.on("open", () => {
        // Send SignalR JSON handshake request
        socket.send(`{"protocol":"json","version":1}${RS}`);
      });

      socket.on("message", (msg) => {
        if (!handshakeDone) {
          // First message is the handshake response: {}\x1e
          if (msg.includes(RS)) {
            handshakeDone = true;
            wsHandshakeTrend.add(Date.now() - t0);
          }
        }
        // Subsequent messages are Blazor render batches — just receive them.
      });

      socket.on("error", () => {
        wsConnectErrors.add(1);
        socket.close();
      });

      // Hold circuit open for 5 s then close cleanly
      socket.setTimeout(() => socket.close(), 5000);
    }
  );

  check(res, { "ws status 101": (r) => r && r.status === 101 });
  wsConnectErrors.add(res && res.status === 101 ? 0 : 1);

  sleep(1);
}
