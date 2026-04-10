window.getNavigatorLanguage = function () {
    return (navigator.languages && navigator.languages[0]) || navigator.language || 'en';
};

window.saveSettings = function (settings) {
    try { localStorage.setItem('pontinho_settings', JSON.stringify(settings)); } catch {}
};

window.loadSettings = function () {
    try {
        const raw = localStorage.getItem('pontinho_settings');
        return raw ? JSON.parse(raw) : null;
    } catch { return null; }
};

window._soundEnabled = true;
window.setSoundEnabled = function (val) { window._soundEnabled = val; };

window.playLastCardSound = function () {
    if (!window._soundEnabled) return;
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        // Two-note rising chime: short A5 followed by C#6
        function note(freq, startAt, duration, gain) {
            var osc = ctx.createOscillator();
            var env = ctx.createGain();
            osc.type = 'sine';
            osc.frequency.value = freq;
            env.gain.setValueAtTime(0, ctx.currentTime + startAt);
            env.gain.linearRampToValueAtTime(gain, ctx.currentTime + startAt + 0.01);
            env.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + startAt + duration);
            osc.connect(env);
            env.connect(ctx.destination);
            osc.start(ctx.currentTime + startAt);
            osc.stop(ctx.currentTime + startAt + duration);
        }
        note(880,  0,    0.18, 0.4);   // A5
        note(1109, 0.15, 0.28, 0.35);  // C#6
    } catch {}
};

window.playJokerSwapSound = function () {
    if (!window._soundEnabled) return;
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        // Magical shimmer: four ascending sine tones, each slightly detuned by a twin
        // a few Hz apart to create a shimmering beat — like a sparkle arpeggio.
        function shimmer(freq, t, dur, gain) {
            [0, 4].forEach(function (detune) {
                var osc = ctx.createOscillator();
                var env = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.value = freq + detune;
                env.gain.setValueAtTime(0, ctx.currentTime + t);
                env.gain.linearRampToValueAtTime(gain, ctx.currentTime + t + 0.008);
                env.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + t + dur);
                osc.connect(env); env.connect(ctx.destination);
                osc.start(ctx.currentTime + t); osc.stop(ctx.currentTime + t + dur);
            });
        }
        shimmer(1047, 0.00, 0.18, 0.22);  // C6
        shimmer(1319, 0.10, 0.18, 0.20);  // E6
        shimmer(1568, 0.20, 0.18, 0.18);  // G6
        shimmer(2093, 0.30, 0.30, 0.16);  // C7 — held sparkle top
    } catch {}
};

window.playRoundWinSound = function () {
    if (!window._soundEnabled) return;
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        function note(freq, t, dur, gain) {
            var osc = ctx.createOscillator();
            var env = ctx.createGain();
            osc.type = 'triangle';
            osc.frequency.value = freq;
            env.gain.setValueAtTime(0, ctx.currentTime + t);
            env.gain.linearRampToValueAtTime(gain, ctx.currentTime + t + 0.01);
            env.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + t + dur);
            osc.connect(env); env.connect(ctx.destination);
            osc.start(ctx.currentTime + t); osc.stop(ctx.currentTime + t + dur);
        }
        note(523,  0,    0.14, 0.3);   // C5
        note(659,  0.13, 0.14, 0.3);   // E5
        note(784,  0.26, 0.14, 0.3);   // G5
        note(1047, 0.39, 0.32, 0.38);  // C6 — held ending note
    } catch {}
};

window.playRoundLoseSound = function () {
    if (!window._soundEnabled) return;
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        function note(freq, t, dur, gain) {
            var osc = ctx.createOscillator();
            var env = ctx.createGain();
            osc.type = 'sine';
            osc.frequency.value = freq;
            env.gain.setValueAtTime(0, ctx.currentTime + t);
            env.gain.linearRampToValueAtTime(gain, ctx.currentTime + t + 0.02);
            env.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + t + dur);
            osc.connect(env); env.connect(ctx.destination);
            osc.start(ctx.currentTime + t); osc.stop(ctx.currentTime + t + dur);
        }
        note(392, 0,    0.26, 0.22);  // G4
        note(330, 0.24, 0.26, 0.19);  // E4
        note(262, 0.48, 0.40, 0.15);  // C4 — soft final note
    } catch {}
};

window.playGameLoseSound = function () {
    if (!window._soundEnabled) return;
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        var now = ctx.currentTime;

        // ── Layer 1: deep bass thud ──
        var thud = ctx.createOscillator();
        var thudEnv = ctx.createGain();
        thud.type = 'sine';
        thud.frequency.setValueAtTime(80, now);
        thud.frequency.exponentialRampToValueAtTime(20, now + 0.4);
        thudEnv.gain.setValueAtTime(0.7, now);
        thudEnv.gain.exponentialRampToValueAtTime(0.001, now + 0.5);
        thud.connect(thudEnv); thudEnv.connect(ctx.destination);
        thud.start(now); thud.stop(now + 0.5);

        // ── Layer 2: noise burst (the crack) ──
        var bufSize = ctx.sampleRate * 0.6;
        var buf = ctx.createBuffer(1, bufSize, ctx.sampleRate);
        var data = buf.getChannelData(0);
        for (var i = 0; i < bufSize; i++) data[i] = (Math.random() * 2 - 1);
        var noise = ctx.createBufferSource();
        noise.buffer = buf;
        var noiseFilter = ctx.createBiquadFilter();
        noiseFilter.type = 'lowpass';
        noiseFilter.frequency.setValueAtTime(2000, now);
        noiseFilter.frequency.exponentialRampToValueAtTime(200, now + 0.5);
        var noiseEnv = ctx.createGain();
        noiseEnv.gain.setValueAtTime(0.8, now);
        noiseEnv.gain.exponentialRampToValueAtTime(0.001, now + 0.6);
        noise.connect(noiseFilter); noiseFilter.connect(noiseEnv); noiseEnv.connect(ctx.destination);
        noise.start(now); noise.stop(now + 0.6);

        // ── Layer 3: descending rumble ──
        var rumble = ctx.createOscillator();
        var rumbleEnv = ctx.createGain();
        rumble.type = 'sawtooth';
        rumble.frequency.setValueAtTime(120, now + 0.1);
        rumble.frequency.exponentialRampToValueAtTime(30, now + 1.0);
        rumbleEnv.gain.setValueAtTime(0, now);
        rumbleEnv.gain.linearRampToValueAtTime(0.25, now + 0.15);
        rumbleEnv.gain.exponentialRampToValueAtTime(0.001, now + 1.0);
        rumble.connect(rumbleEnv); rumbleEnv.connect(ctx.destination);
        rumble.start(now + 0.1); rumble.stop(now + 1.0);
    } catch {}
};

window.playBurnSound = function () {
    if (!window._soundEnabled) return;
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        var now = ctx.currentTime;

        // ── Layer 1: low roar — the body of the flame ──
        var roarBuf = ctx.createBuffer(1, Math.floor(ctx.sampleRate * 0.75), ctx.sampleRate);
        var rd = roarBuf.getChannelData(0);
        for (var i = 0; i < rd.length; i++) rd[i] = Math.random() * 2 - 1;
        var roar = ctx.createBufferSource();
        roar.buffer = roarBuf;
        var roarLp = ctx.createBiquadFilter();
        roarLp.type = 'lowpass';
        roarLp.frequency.value = 220;
        var roarGain = ctx.createGain();
        roarGain.gain.setValueAtTime(0, now);
        roarGain.gain.linearRampToValueAtTime(0.55, now + 0.04);
        roarGain.gain.exponentialRampToValueAtTime(0.001, now + 0.65);
        roar.connect(roarLp);
        roarLp.connect(roarGain);
        roarGain.connect(ctx.destination);
        roar.start(now);
        roar.stop(now + 0.75);

        // ── Layer 2: sharp crackle pops — the texture of burning ──
        function pop(t) {
            var len = Math.floor(ctx.sampleRate * 0.022);
            var b = ctx.createBuffer(1, len, ctx.sampleRate);
            var d = b.getChannelData(0);
            for (var j = 0; j < len; j++) d[j] = Math.random() * 2 - 1;
            var s = ctx.createBufferSource();
            s.buffer = b;
            var hp = ctx.createBiquadFilter();
            hp.type = 'highpass';
            hp.frequency.value = 1400 + Math.random() * 2200;
            var g = ctx.createGain();
            g.gain.setValueAtTime(0, now + t);
            g.gain.linearRampToValueAtTime(0.35 + Math.random() * 0.35, now + t + 0.002);
            g.gain.exponentialRampToValueAtTime(0.001, now + t + 0.022);
            s.connect(hp);
            hp.connect(g);
            g.connect(ctx.destination);
            s.start(now + t);
            s.stop(now + t + 0.03);
        }

        // Scatter 9–13 pops randomly across the first 0.55 s
        var n = 9 + Math.floor(Math.random() * 5);
        for (var k = 0; k < n; k++) pop(Math.random() * 0.55);

    } catch {}
};

window.animateCardDraw = function (sourceSelector, targetSelector, endAngleDeg) {
    try {
        var src = document.querySelector(sourceSelector);
        if (!src) return;
        var sr = src.getBoundingClientRect();

        // Detect whether the source card lives in a rotated side-hand.
        // CSS rotates the card via a parent-context rule (.hand-side .card), which stops
        // applying once the clone is appended to document.body. We must replicate the
        // starting rotation manually so the clone visually matches its origin.
        var srcSideEl   = src.closest && src.closest('.hand-side');
        var srcIsLeft   = srcSideEl && (src.closest('.seat-left') || src.closest('.seat-top-left') || src.closest('.side-left'));
        var srcStartDeg = srcSideEl ? (srcIsLeft ? -90 : 90) : 0;

        // getBoundingClientRect on a 90°-rotated card returns swapped dimensions.
        // Restore natural (portrait) card dimensions for the clone.
        var cloneW, cloneH, cloneLeft, cloneTop;
        if (srcSideEl) {
            cloneW    = sr.height;                    // natural width  = rotated height
            cloneH    = sr.width;                     // natural height = rotated width
            cloneLeft = sr.left + sr.width  / 2 - cloneW / 2;
            cloneTop  = sr.top  + sr.height / 2 - cloneH / 2;
        } else {
            cloneW = sr.width; cloneH = sr.height;
            cloneLeft = sr.left; cloneTop = sr.top;
        }

        var clone = src.cloneNode(true);
        Object.assign(clone.style, {
            position: 'fixed', margin: '0', zIndex: '9000',
            pointerEvents: 'none', transition: 'none', cursor: 'default',
            left: cloneLeft + 'px', top: cloneTop + 'px',
            width: cloneW + 'px', height: cloneH + 'px',
            transform: srcStartDeg ? 'rotate(' + srcStartDeg + 'deg)' : '',
            boxShadow: '0 4px 14px rgba(0,0,0,0.55)', willChange: 'transform,opacity',
        });
        document.body.appendChild(clone);
        void clone.offsetWidth; // reflow

        function flyTo(tr, tgtEl) {
            var destLeft = tr.left + (tr.width  - cloneW) / 2;
            var destTop  = tr.top  + (tr.height - cloneH) / 2;
            var endTransform;
            if (endAngleDeg !== undefined && endAngleDeg !== null) {
                endTransform = 'rotate(' + endAngleDeg + 'deg) scale(0.8)';
            } else {
                // Detect rotation via DOM structure — cannot use bounding rect because the
                // card-drawn-in animation fill-mode overrides the card's rotate() transform,
                // making getBoundingClientRect() return portrait dims even for side-hand cards.
                var isSide = tgtEl && tgtEl.closest && tgtEl.closest('.hand-side');
                var isLeft = isSide && (tgtEl.closest('.seat-left') || tgtEl.closest('.seat-top-left') || tgtEl.closest('.side-left'));
                endTransform = isSide
                    ? ((isLeft ? 'rotate(-90deg)' : 'rotate(90deg)') + ' scale(0.8)')
                    : 'scale(0.8)';
            }
            clone.style.transition =
                'left 380ms cubic-bezier(0.4,0,0.2,1),' +
                'top 380ms cubic-bezier(0.4,0,0.2,1),' +
                'transform 380ms ease,' +
                'opacity 160ms ease 220ms';
            clone.style.left      = destLeft + 'px';
            clone.style.top       = destTop  + 'px';
            clone.style.transform = endTransform;
            clone.style.opacity   = '0';
            setTimeout(function () { clone.remove(); }, 430);
        }

        var tgt = document.querySelector(targetSelector);
        if (tgt) {
            flyTo(tgt.getBoundingClientRect(), tgt);
        } else {
            // Target not in DOM yet (Blazor re-render pending) — observe for it
            var obs = new MutationObserver(function (_, o) {
                var t = document.querySelector(targetSelector);
                if (t) { o.disconnect(); flyTo(t.getBoundingClientRect(), t); }
            });
            obs.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
            setTimeout(function () { obs.disconnect(); clone.remove(); }, 700); // safety cleanup
        }
    } catch (e) {}
};

// Animate one or more hand cards flying to a target (discard pile, table, combo row).
// sourceSelector may match multiple elements; each flies to the center of targetSelector.
window.animateCardsPlay = function (sourceSelector, targetSelector) {
    try {
        var srcs = Array.from(document.querySelectorAll(sourceSelector));
        var tgt  = document.querySelector(targetSelector);
        if (!srcs.length || !tgt) return;
        var tr = tgt.getBoundingClientRect();
        srcs.forEach(function (src, i) {
            var sr = src.getBoundingClientRect();
            var clone = src.cloneNode(true);
            clone.classList.remove('card-drawn-in', 'selected', 'dragging');
            Object.assign(clone.style, {
                position: 'fixed', margin: '0', zIndex: '9000',
                pointerEvents: 'none', transition: 'none', cursor: 'default',
                left: sr.left + 'px', top: sr.top + 'px',
                width: sr.width + 'px', height: sr.height + 'px',
                opacity: '1', transform: 'none',
                boxShadow: '0 4px 14px rgba(0,0,0,0.55)', willChange: 'transform,opacity',
            });
            document.body.appendChild(clone);
            void clone.offsetWidth;
            setTimeout(function () {
                var destLeft = tr.left + tr.width  / 2 - sr.width  / 2;
                var destTop  = tr.top  + tr.height / 2 - sr.height / 2;
                clone.style.transition =
                    'left 320ms cubic-bezier(0.4,0,0.2,1),' +
                    'top 320ms cubic-bezier(0.4,0,0.2,1),' +
                    'transform 320ms ease,' +
                    'opacity 120ms ease 200ms';
                clone.style.left      = destLeft + 'px';
                clone.style.top       = destTop  + 'px';
                clone.style.transform = 'scale(0.75)';
                clone.style.opacity   = '0';
                setTimeout(function () { clone.remove(); }, 380);
            }, i * 60);
        });
    } catch (e) {}
};

// Phase-1/2 animation for human lay-down and add-to-combo.
// Phase 1 (called before state change): clones source cards and sets up a
// MutationObserver on .combos-scroll.  Phase 2 fires automatically when the
// new combo cards appear in the DOM, flying each clone to its exact position.
window.captureAndFlyToNewCombo = function (sourceSelector) {
    try {
        var srcs = Array.from(document.querySelectorAll(sourceSelector));
        if (!srcs.length) return;

        // Capture clones at current positions
        var items = srcs.map(function (src) {
            var sr = src.getBoundingClientRect();
            var clone = src.cloneNode(true);
            clone.classList.remove('card-drawn-in', 'selected', 'dragging');
            Object.assign(clone.style, {
                position: 'fixed', margin: '0', zIndex: '9000',
                pointerEvents: 'none', transition: 'none', cursor: 'default',
                left: sr.left + 'px', top: sr.top + 'px',
                width: sr.width + 'px', height: sr.height + 'px',
                opacity: '1', transform: 'none',
                boxShadow: '0 4px 14px rgba(0,0,0,0.55)', willChange: 'transform,opacity',
            });
            document.body.appendChild(clone);
            void clone.offsetWidth;
            return { clone: clone, sr: sr };
        });

        var comboScroll = document.querySelector('.combos-scroll');
        if (!comboScroll) { items.forEach(function (it) { it.clone.remove(); }); return; }
        var existingCards = new Set(Array.from(comboScroll.querySelectorAll('.card')));

        function flyItemsToTargets(newCards) {
            items.forEach(function (item, i) {
                var tgtEl = newCards[i];
                if (!tgtEl) { item.clone.remove(); return; }
                var tr  = tgtEl.getBoundingClientRect();
                var sr  = item.sr;
                var destLeft = tr.left + tr.width  / 2 - sr.width  / 2;
                var destTop  = tr.top  + tr.height / 2 - sr.height / 2;
                setTimeout(function () {
                    item.clone.style.transition =
                        'left 320ms cubic-bezier(0.4,0,0.2,1),' +
                        'top 320ms cubic-bezier(0.4,0,0.2,1),' +
                        'transform 320ms ease,' +
                        'opacity 120ms ease 200ms';
                    item.clone.style.left      = destLeft + 'px';
                    item.clone.style.top       = destTop  + 'px';
                    item.clone.style.transform = 'scale(0.8)';
                    item.clone.style.opacity   = '0';
                    setTimeout(function () { item.clone.remove(); }, 380);
                }, i * 60);
            });
        }

        var obs = new MutationObserver(function (_, o) {
            var newCards = Array.from(comboScroll.querySelectorAll('.card'))
                .filter(function (c) { return !existingCards.has(c); });
            if (newCards.length < items.length) return; // still rendering
            o.disconnect();
            flyItemsToTargets(newCards.slice(0, items.length));
        });
        obs.observe(comboScroll, { childList: true, subtree: true });
        // Safety cleanup: fade clones if combo never appears (failed validation etc.)
        setTimeout(function () {
            obs.disconnect();
            items.forEach(function (it) {
                it.clone.style.transition = 'opacity 200ms ease';
                it.clone.style.opacity = '0';
                setTimeout(function () { it.clone.remove(); }, 250);
            });
        }, 2000);
    } catch (e) {}
};

// Clones each target card into a fixed-position overlay on document.body so the burn animation
// survives Blazor re-renders that would otherwise remove the original DOM element mid-animation.
// items: array of { comboIdx: number, cardIdx: number }
window._burnDebug = null;
window.burnComboCards = function (items, suppressSound) {
    window._burnDebug = { called: true, itemCount: items.length, items: JSON.parse(JSON.stringify(items)), results: [] };
    items.forEach(function (t) {
        var row = document.querySelector('[data-combo-idx="' + t.comboIdx + '"]');
        var result = { comboIdx: t.comboIdx, cardIdx: t.cardIdx, rowFound: !!row, cardFound: false };
        if (!row) { window._burnDebug.results.push(result); return; }
        var cards = row.querySelectorAll(':scope > .card');
        result.totalCards = cards.length;
        var card = cards[t.cardIdx];
        result.cardFound = !!card;
        if (card) {
            if (!suppressSound) window.playBurnSound();
            var rect = card.getBoundingClientRect();
            var clone = card.cloneNode(true);
            clone.style.cssText = 'position:fixed;left:' + rect.left + 'px;top:' + rect.top + 'px;' +
                'width:' + rect.width + 'px;height:' + rect.height + 'px;margin:0;z-index:9999;pointer-events:none;';
            clone.classList.add('burning');
            document.body.appendChild(clone);
            setTimeout(function () { clone.remove(); }, 1400);
        }
        window._burnDebug.results.push(result);
    });
};

window.checkCombosOverflow = function () {
    const el = document.querySelector('.combos-scroll');
    return el ? el.scrollHeight > el.clientHeight + 2 : false;
};


// ── Fireworks ─────────────────────────────────────────────────────────────────
(function () {
    var _rafId = null;
    var _canvas = null;
    var _ctx = null;
    var _rockets = [];
    var _particles = [];
    var _audioCtx = null;
    var _stopAt = 0;

    function getAudio() {
        if (!_audioCtx) _audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        return _audioCtx;
    }

    function playLaunch() {
        if (!window._soundEnabled) return;
        try {
            var ctx = getAudio();
            var osc = ctx.createOscillator();
            var gain = ctx.createGain();
            osc.type = 'sawtooth';
            osc.frequency.setValueAtTime(120 + Math.random() * 80, ctx.currentTime);
            osc.frequency.exponentialRampToValueAtTime(600 + Math.random() * 400, ctx.currentTime + 0.35);
            gain.gain.setValueAtTime(0.07, ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.35);
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.start(ctx.currentTime);
            osc.stop(ctx.currentTime + 0.35);
        } catch (e) {}
    }

    function playExplosion() {
        if (!window._soundEnabled) return;
        try {
            var ctx = getAudio();
            var now = ctx.currentTime;
            // Low thump
            var thump = ctx.createOscillator();
            var thumpGain = ctx.createGain();
            thump.type = 'sine';
            thump.frequency.setValueAtTime(80, now);
            thump.frequency.exponentialRampToValueAtTime(30, now + 0.15);
            thumpGain.gain.setValueAtTime(0.45, now);
            thumpGain.gain.exponentialRampToValueAtTime(0.001, now + 0.22);
            thump.connect(thumpGain);
            thumpGain.connect(ctx.destination);
            thump.start(now);
            thump.stop(now + 0.25);
            // Crackle burst
            var bufLen = Math.floor(ctx.sampleRate * 0.4);
            var buf = ctx.createBuffer(1, bufLen, ctx.sampleRate);
            var d = buf.getChannelData(0);
            for (var i = 0; i < bufLen; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / bufLen, 1.5);
            var noise = ctx.createBufferSource();
            noise.buffer = buf;
            var bp = ctx.createBiquadFilter();
            bp.type = 'bandpass';
            bp.frequency.value = 800 + Math.random() * 1200;
            bp.Q.value = 0.8;
            var noiseGain = ctx.createGain();
            noiseGain.gain.setValueAtTime(0.28, now);
            noiseGain.gain.exponentialRampToValueAtTime(0.001, now + 0.4);
            noise.connect(bp);
            bp.connect(noiseGain);
            noiseGain.connect(ctx.destination);
            noise.start(now);
        } catch (e) {}
    }

    function ensureCanvas() {
        if (!_canvas) {
            _canvas = document.createElement('canvas');
            _canvas.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:9998;display:none;';
            document.body.appendChild(_canvas);
        }
        _ctx = _canvas.getContext('2d');
        _canvas.width = window.innerWidth;
        _canvas.height = window.innerHeight;
    }

    var COLORS = [
        'rgba(255,215,0,', 'rgba(255,80,80,', 'rgba(80,200,255,',
        'rgba(160,255,80,', 'rgba(255,120,255,', 'rgba(255,180,40,',
        'rgba(255,255,255,', 'rgba(120,255,200,'
    ];

    function spawnRocket() {
        var w = _canvas.width, h = _canvas.height;
        var x = w * 0.15 + Math.random() * w * 0.7;
        var targetY = h * 0.1 + Math.random() * h * 0.45;
        var speed = 9 + Math.random() * 6;
        _rockets.push({ x: x, y: h, vx: (Math.random() - 0.5) * 1.5, vy: -speed, targetY: targetY,
            color: COLORS[Math.floor(Math.random() * COLORS.length)], trail: [] });
        playLaunch();
    }

    function explode(x, y, color) {
        var count = 90 + Math.floor(Math.random() * 40);
        for (var i = 0; i < count; i++) {
            var angle = (Math.PI * 2 / count) * i + (Math.random() - 0.5) * 0.25;
            var speed = 1.5 + Math.random() * 4.5;
            _particles.push({ x: x, y: y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed,
                alpha: 1, size: 1.5 + Math.random() * 2.5, color: color, decay: 0.011 + Math.random() * 0.009 });
        }
        playExplosion();
    }

    function tick() {
        if (!_ctx) return;
        var now = Date.now();
        var w = _canvas.width, h = _canvas.height;
        _ctx.clearRect(0, 0, w, h);

        if (now < _stopAt && Math.random() < 0.035) spawnRocket();

        for (var i = _rockets.length - 1; i >= 0; i--) {
            var r = _rockets[i];
            r.trail.push({ x: r.x, y: r.y });
            if (r.trail.length > 9) r.trail.shift();
            for (var t = 0; t < r.trail.length; t++) {
                _ctx.beginPath();
                _ctx.arc(r.trail[t].x, r.trail[t].y, 2 * (t / r.trail.length), 0, Math.PI * 2);
                _ctx.fillStyle = r.color + (t / r.trail.length * 0.7) + ')';
                _ctx.fill();
            }
            _ctx.beginPath();
            _ctx.arc(r.x, r.y, 3, 0, Math.PI * 2);
            _ctx.fillStyle = r.color + '1)';
            _ctx.fill();
            r.x += r.vx;
            r.y += r.vy;
            r.vy += 0.13;
            if (r.y <= r.targetY || r.vy >= 0) {
                explode(r.x, r.y, r.color);
                _rockets.splice(i, 1);
            }
        }

        for (var j = _particles.length - 1; j >= 0; j--) {
            var p = _particles[j];
            p.x += p.vx; p.y += p.vy;
            p.vy += 0.05; p.vx *= 0.98; p.vy *= 0.98;
            p.alpha -= p.decay;
            if (p.alpha <= 0) { _particles.splice(j, 1); continue; }
            _ctx.beginPath();
            _ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
            _ctx.fillStyle = p.color + p.alpha + ')';
            _ctx.fill();
        }

        if (now < _stopAt || _rockets.length > 0 || _particles.length > 0) {
            _rafId = requestAnimationFrame(tick);
        } else {
            _canvas.style.display = 'none';
        }
    }

    window.startFireworks = function () {
        ensureCanvas();
        _rockets = []; _particles = [];
        _stopAt = Date.now() + 6500;
        _canvas.style.display = 'block';
        if (_rafId) cancelAnimationFrame(_rafId);
        // Open with an immediate burst of 3 rockets
        spawnRocket();
        setTimeout(spawnRocket, 220);
        setTimeout(spawnRocket, 480);
        _rafId = requestAnimationFrame(tick);
    };

    window.stopFireworks = function () {
        _stopAt = 0;
        _rockets = []; _particles = [];
        if (_rafId) { cancelAnimationFrame(_rafId); _rafId = null; }
        if (_canvas) _canvas.style.display = 'none';
    };
}());

// ── Falling Ashes ─────────────────────────────────────────────────────────────
(function () {
    var _rafId   = null;
    var _canvas  = null;
    var _ctx     = null;
    var _particles = [];
    var _audioCtx  = null;
    var _stopAt    = 0;

    function getAudio() {
        if (!_audioCtx) _audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        return _audioCtx;
    }

    function playWind() {
        if (!window._soundEnabled) return;
        try {
            var ctx = getAudio();
            var duration = 6.5;
            var bufLen = ctx.sampleRate * 2;
            var buffer = ctx.createBuffer(1, bufLen, ctx.sampleRate);
            var data = buffer.getChannelData(0);
            for (var i = 0; i < bufLen; i++) data[i] = Math.random() * 2 - 1;
            var noise = ctx.createBufferSource();
            noise.buffer = buffer;
            noise.loop = true;
            var filter = ctx.createBiquadFilter();
            filter.type = 'lowpass';
            filter.Q.value = 1.8;
            var t0 = ctx.currentTime;
            filter.frequency.setValueAtTime(80,  t0);
            filter.frequency.linearRampToValueAtTime(600, t0 + 2.0);
            filter.frequency.linearRampToValueAtTime(180, t0 + 4.5);
            filter.frequency.linearRampToValueAtTime(60,  t0 + duration);
            var gain = ctx.createGain();
            gain.gain.setValueAtTime(0,    t0);
            gain.gain.linearRampToValueAtTime(0.22, t0 + 1.2);
            gain.gain.linearRampToValueAtTime(0.18, t0 + 4.0);
            gain.gain.linearRampToValueAtTime(0,    t0 + duration);
            noise.connect(filter);
            filter.connect(gain);
            gain.connect(ctx.destination);
            noise.start(t0);
            noise.stop(t0 + duration);
        } catch (e) {}
    }

    function ensureCanvas() {
        if (!_canvas) {
            _canvas = document.createElement('canvas');
            _canvas.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:9998;display:none;';
            document.body.appendChild(_canvas);
        }
        _ctx = _canvas.getContext('2d');
        _canvas.width  = window.innerWidth;
        _canvas.height = window.innerHeight;
    }

    var ASH_COLOURS = [
        'rgba(80,80,80,',  'rgba(60,60,60,',  'rgba(100,95,90,',
        'rgba(120,100,80,','rgba(50,50,50,',   'rgba(90,85,80,'
    ];

    function spawnParticle() {
        var size = 2 + Math.random() * 4;
        return {
            x:         Math.random() * _canvas.width,
            y:         -10 - Math.random() * 15,
            vx:        (Math.random() - 0.5) * 0.6,
            vy:        0.4 + Math.random() * 1.0,
            rot:       Math.random() * Math.PI * 2,
            rotV:      (Math.random() - 0.5) * 0.03,
            size:      size,
            aspect:    0.5 + Math.random() * 0.5,  // blob squash (pre-calculated)
            isFlake:   Math.random() < 0.4,
            alpha:     0.5 + Math.random() * 0.4,
            fadeRate:  0.0006 + Math.random() * 0.002,
            colour:    ASH_COLOURS[Math.floor(Math.random() * ASH_COLOURS.length)],
            swayAmp:   0.2 + Math.random() * 0.7,
            swayFreq:  0.01 + Math.random() * 0.02,
            swayPhase: Math.random() * Math.PI * 2,
            age:       0
        };
    }

    function drawParticle(p) {
        _ctx.save();
        _ctx.translate(p.x, p.y);
        _ctx.rotate(p.rot);
        _ctx.fillStyle = p.colour + p.alpha + ')';
        _ctx.beginPath();
        if (p.isFlake)
            _ctx.ellipse(0, 0, p.size * 0.3, p.size * 1.4, 0, 0, Math.PI * 2);
        else
            _ctx.ellipse(0, 0, p.size, p.size * p.aspect, 0, 0, Math.PI * 2);
        _ctx.fill();
        _ctx.restore();
    }

    function tick() {
        if (!_ctx) return;
        var now = Date.now();
        var h = _canvas.height;
        _ctx.clearRect(0, 0, _canvas.width, h);

        if (now < _stopAt && _particles.length < 200) {
            var count = 1 + Math.floor(Math.random() * 3);
            for (var i = 0; i < count; i++) _particles.push(spawnParticle());
        }

        for (var j = _particles.length - 1; j >= 0; j--) {
            var p = _particles[j];
            p.age++;
            p.x   += p.vx + Math.sin(p.age * p.swayFreq + p.swayPhase) * p.swayAmp;
            p.y   += p.vy;
            p.rot += p.rotV;
            p.alpha -= p.fadeRate;
            if (p.alpha <= 0 || p.y > h + 20) { _particles.splice(j, 1); continue; }
            drawParticle(p);
        }

        if (now < _stopAt || _particles.length > 0) {
            _rafId = requestAnimationFrame(tick);
        } else {
            _canvas.style.display = 'none';
        }
    }

    window.startAshes = function () {
        ensureCanvas();
        _particles = [];
        _stopAt = Infinity;
        _canvas.style.display = 'block';
        if (_rafId) cancelAnimationFrame(_rafId);
        playWind();
        _rafId = requestAnimationFrame(tick);
    };

    window.stopAshes = function () {
        _stopAt = 0;
        _particles = [];
        if (_rafId) { cancelAnimationFrame(_rafId); _rafId = null; }
        if (_canvas) _canvas.style.display = 'none';
    };
}());

// Prevent dragover default so that ondrop fires on hand cards (reorder)
// and on combo rows (add to combination).
document.addEventListener('dragover', function (e) {
    if (e.target && e.target.closest) {
        if (e.target.closest('.hand') || e.target.closest('.combo-row') || e.target.closest('.hand-endzone') || e.target.closest('.discard-drop-zone') || e.target.closest('.table-area') || e.target.closest('.player-section') || e.target.closest('.deck-drop-zone')) {
            e.preventDefault();
            if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
        }
    }
});

// ── Hand drag visual manager + DotNet drop handler ───────────────────────────
// Tracks drag position entirely in JS — zero Blazor round-trips during dragging.
// Uses dragover + getBoundingClientRect for left/right half detection so the
// insertion indicator updates smoothly as the cursor moves within a card.
//
// Hand-card reorder is triggered directly via DotNet invokeMethodAsync so it
// works for both real browser drags and synthetic DragEvents used in E2E tests
// (Blazor's @ondrop event delegation does not fire for synthetic events).
(function () {
    var _from = -1;       // source card index (-1 = not a hand-card drag)
    var _dropTarget = -1; // computed insertBefore index
    var _handDropRef = null; // DotNetObjectReference<Game> registered from C#

    function getCards() {
        var fan = document.querySelector('.hand-fan');
        return fan ? Array.from(fan.querySelectorAll(':scope > .card')) : [];
    }

    function clearHighlights(cards) {
        (cards || getCards()).forEach(function (c) {
            c.classList.remove('drop-before', 'drop-after');
        });
    }

    // Record source index when a hand card starts being dragged.
    // If multiple cards are selected, replace the browser's single-card ghost with a
    // composite image showing all selected cards grouped together.
    document.addEventListener('dragstart', function (e) {
        var cards = getCards();
        var card = e.target.closest && e.target.closest('.hand-fan > .card');
        _from = card ? cards.indexOf(card) : -1;
        _dropTarget = -1;

        if (_from < 0 || !e.dataTransfer) return;
        var selected = cards.filter(function (c) { return c.classList.contains('selected'); });
        // If dragging an unselected card there's nothing to group; use browser default.
        if (selected.length < 2) return;

        // Sort to match table order: non-jokers ascending by rank, jokers interspersed in gaps.
        var jokers   = selected.filter(function (c) { return parseInt(c.dataset.rank, 10) === 0; });
        var nonJokers = selected.filter(function (c) { return parseInt(c.dataset.rank, 10) !== 0; });
        nonJokers.sort(function (a, b) { return parseInt(a.dataset.rank, 10) - parseInt(b.dataset.rank, 10); });
        // Place each joker after the non-joker whose successor is missing (fills the gap).
        var sorted = nonJokers.slice();
        jokers.forEach(function (j) {
            // Find the first gap between consecutive non-jokers and insert the joker there.
            var inserted = false;
            for (var gi = 0; gi < sorted.length - 1; gi++) {
                var rA = parseInt(sorted[gi].dataset.rank, 10);
                var rB = parseInt(sorted[gi + 1].dataset.rank, 10);
                if (rB - rA > 1) { sorted.splice(gi + 1, 0, j); inserted = true; break; }
            }
            if (!inserted) sorted.push(j); // no gap found — append at end
        });

        // Build an off-screen row of card clones matching the table combo-row layout
        // (each card after the first overlaps the previous by 50% of its width).
        var wrap = document.createElement('div');
        wrap.style.cssText = 'position:fixed;top:-9999px;left:0;display:flex;align-items:center;pointer-events:none;';
        sorted.forEach(function (c, i) {
            var rect = c.getBoundingClientRect();
            var cl = c.cloneNode(true);
            cl.style.width      = rect.width  + 'px';
            cl.style.height     = rect.height + 'px';
            cl.style.opacity    = '1';
            cl.style.transform  = 'none';
            cl.style.position   = 'relative';
            cl.style.flexShrink = '0';
            if (i > 0) cl.style.marginLeft = (-rect.width * 0.5) + 'px';
            wrap.appendChild(cl);
        });
        document.body.appendChild(wrap);
        // Anchor the ghost so the cursor is roughly over the middle card.
        e.dataTransfer.setDragImage(wrap, wrap.offsetWidth / 2, wrap.offsetHeight / 2);
        setTimeout(function () { if (wrap.parentNode) wrap.parentNode.removeChild(wrap); }, 0);
    }, true);

    // On every dragover update the indicator based on which half of the hovered card
    // the cursor is on: left half -> insert before that card; right half -> insert after.
    // Works for both hand-card reorder (_from >= 0) and external drags (deck/discard, _from < 0).
    document.addEventListener('dragover', function (e) {
        var card = e.target.closest && e.target.closest('.hand-fan > .card');
        if (!card) { clearHighlights(); _dropTarget = -1; return; }
        var cards = getCards();
        var idx = cards.indexOf(card);
        if (idx < 0) return;
        var rect = card.getBoundingClientRect();
        var ins = e.clientX < rect.left + rect.width / 2 ? idx : idx + 1;
        if (_dropTarget === ins) return; // no change -- skip DOM update
        _dropTarget = ins;
        clearHighlights(cards);
        if (_from >= 0 && (ins === _from || ins === _from + 1)) return; // no-op position for reorder
        if (ins < cards.length) {
            cards[ins].classList.add('drop-before');
        } else if (cards.length > 0) {
            cards[cards.length - 1].classList.add('drop-after');
        }
    }, true);

    // Hand-card drop: invoke the C# HandleHandCardDrop method directly.
    // Fires in capture phase before Blazor's bubble-phase @ondrop handlers.
    // Works for both real and synthetic DragEvents (E2E tests dispatch synthetic ones).
    document.addEventListener('drop', function (e) {
        if (_from < 0 || _dropTarget < 0) return;
        if (!e.target || !e.target.closest) return;
        if (!e.target.closest('.hand-fan')) return; // only reorder drops within the hand fan
        if (_handDropRef) {
            _handDropRef.invokeMethodAsync('HandleHandCardDrop', _from, _dropTarget);
        }
    }, true);

    // Clean up on drag end. _from and _dropTarget are NOT cleared here:
    // HandleHandCardDrop is invoked async via DotNet and may still be in flight.
    // Both are reset at the start of the NEXT dragstart instead.
    document.addEventListener('dragend', function () {
        clearHighlights();
    }, true);

    // Blazor registers its DotNetObjectReference here on first render.
    window.registerHandDropHandler = function (ref) { _handDropRef = ref; }
    window.discardFirstCard = function () { if (_handDropRef) _handDropRef.invokeMethodAsync('HandleDiscardFirstCard'); };
    window.setupBurnTestScenario = async function () { if (_handDropRef) return await _handDropRef.invokeMethodAsync('SetupBurnTestScenario'); return -1; };
    window.getTripleDiscardsPendingCount = async function () { if (_handDropRef) return await _handDropRef.invokeMethodAsync('GetTripleDiscardsPendingCount'); return -1; };
    window.setupDrainSingleCardAddsScenario = async function () { if (_handDropRef) return await _handDropRef.invokeMethodAsync('SetupDrainSingleCardAddsScenario'); return -1; };
    window.setupMultiComboLayDownScenario = async function () { if (_handDropRef) return await _handDropRef.invokeMethodAsync('SetupMultiComboLayDownScenario'); return -1; };
    window.setupDiscardDrawScenario = async function () { if (_handDropRef) return await _handDropRef.invokeMethodAsync('SetupDiscardDrawScenario'); return false; };

    // Read-only accessors used by tests and legacy C# code.
    window.getHandDragState  = function () { return [_from, _dropTarget]; };
    window.getHandDragFrom   = function () { return _from; };
    window.getHandDropTarget = function () { return _dropTarget; };
}());
