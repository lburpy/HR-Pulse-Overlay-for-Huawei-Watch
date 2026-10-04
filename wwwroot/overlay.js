/* Pulse Overlay — OBS browser source.
 * Every option comes from the URL query (the editor at /editor builds it). Defaults here must
 * match DEFAULTS() in editor.js so the editor can leave default values out of the URL. */
(() => {
  'use strict';

  const THEMES = ['card', 'minimal', 'neon', 'ring', 'ecg'];

  const FONTS = {
    bahnschrift: '"Bahnschrift", "DIN Alternate", "Segoe UI", sans-serif',
    segoe: '"Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif',
    consolas: '"Cascadia Mono", Consolas, monospace',
    impact: 'Impact, "Arial Black", sans-serif',
    arialblack: '"Arial Black", "Segoe UI Black", sans-serif',
    georgia: 'Georgia, "Times New Roman", serif',
  };

  // Zones as a share of max heart rate (same split Huawei Health uses)
  const ZONES = [
    { key: 'rest', from: 0,   color: '#8b9cff' },
    { key: 'z1',   from: 0.5, color: '#38bdf8' },
    { key: 'z2',   from: 0.6, color: '#22c55e' },
    { key: 'z3',   from: 0.7, color: '#facc15' },
    { key: 'z4',   from: 0.8, color: '#fb923c' },
    { key: 'z5',   from: 0.9, color: '#ef4444' },
  ];

  const TEXT = {
    tr: {
      rest: 'Dinlenme', z1: 'Isınma', z2: 'Yağ yakımı', z3: 'Aerobik', z4: 'Anaerobik', z5: 'Maksimum',
      min: 'MİN', avg: 'ORT', max: 'MAKS',
      waiting: 'Veri bekleniyor…', connecting: 'Bağlanıyor…', lost: 'Sinyal yok',
      reconnecting: 'Yeniden bağlanıyor…', off: 'Bağlı değil', noapp: 'Uygulama kapalı', nodevice: 'Saat bekleniyor',
      demo: 'Demo saat',
      scared: 'Korktu!', recSession: 'Yayın rekoru', recAll: 'Tüm zamanların rekoru',
    },
    en: {
      rest: 'Rest', z1: 'Warm-up', z2: 'Fat burn', z3: 'Aerobic', z4: 'Anaerobic', z5: 'Maximum',
      min: 'MIN', avg: 'AVG', max: 'MAX',
      waiting: 'Waiting for data…', connecting: 'Connecting…', lost: 'No signal',
      reconnecting: 'Reconnecting…', off: 'Not connected', noapp: 'App not running', nodevice: 'Waiting for watch',
      demo: 'Demo watch',
      scared: 'Scared!', recSession: 'Stream record', recAll: 'All-time record',
    },
  };

  const HEART_PATH = 'M12,21C12,21 4.5,16.4 2.4,11.6C0.9,8 3.1,4 6.9,4C9,4 10.5,5.2 12,7C13.5,5.2 15,4 17.1,4C20.9,4 23.1,8 21.6,11.6C19.5,16.4 12,21 12,21Z';
  const HEART = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${HEART_PATH}"/><ellipse class="gloss" cx="7.3" cy="8" rx="2.3" ry="1.4" transform="rotate(-35 7.3 8)"/></svg>`;
  // 270° gauge arc, open at the bottom
  const ARC = 'M43.43,156.57 A80,80 0 1 1 156.57,156.57';
  const ECG_SPEED = 0.11; // px per ms

  // ── Config ─────────────────────────────────────────────────────────
  function parseConfig(query) {
    const P = new URLSearchParams(query);
    const get = k => P.get(k);
    const num = (k, d, lo, hi) => { const v = parseFloat(get(k)); return Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : d; };
    const bool = (k, d) => (P.has(k) ? !/^(0|false|no|off)$/i.test(get(k)) : d);
    const pick = (k, list, d) => { const v = (get(k) || '').toLowerCase(); return list.includes(v) ? v : d; };
    const color = (k, d) => { const v = (get(k) || '').replace(/^#/, ''); return /^[0-9a-f]{6}$/i.test(v) ? '#' + v : d; };
    const theme = pick('theme', THEMES, 'card');
    const pathId = location.pathname.match(/^\/overlay\/([0-9a-z]{3,16})\/?$/i);

    return {
      theme,
      device: ((pathId && pathId[1]) || get('device') || '').toUpperCase(),
      colorMode: pick('color', ['zone', 'accent'], 'zone'),
      accent: color('accent', '#ff3b5c'),
      text: color('text', '#ffffff'),
      font: pick('font', Object.keys(FONTS), 'bahnschrift'),
      scale: num('scale', 1, 0.3, 4),
      bg: num('bg', theme === 'minimal' ? 0 : 70, 0, 100),
      radius: num('radius', 18, 0, 60),
      align: pick('align', ['center', 'left', 'right'], 'center'),
      beat: bool('beat', true),
      tween: bool('tween', true),
      graph: bool('graph', theme !== 'minimal') && ['card', 'minimal', 'neon'].includes(theme),
      window: num('window', 60, 10, 600),
      stats: bool('stats', false),
      zonebar: bool('zonebar', false),
      zname: bool('zname', true),
      name: bool('name', false),
      dot: bool('dot', true) && theme !== 'minimal',
      label: P.has('label') ? get('label').slice(0, 16) : 'BPM',
      lang: pick('lang', ['tr', 'en'], 'tr'),
      maxHr: num('maxhr', 190, 120, 230),
      alert: num('alert', 0, 0, 250),
      alertFx: pick('alertfx', ['flash', 'shake', 'both'], 'flash'),
      hideOff: bool('hideoff', false),
      scare: bool('scare', true),
      scareCount: bool('scarecount', true) && bool('scare', true),
      record: pick('record', ['off', 'session', 'alltime', 'both'], 'both'),
      demo: bool('demo', false) || location.protocol === 'file:',
      demoState: pick('demostate', ['live', 'lost', 'reconnecting', 'off'], 'live'),
    };
  }

  // ── State ──────────────────────────────────────────────────────────
  let cfg = parseConfig(location.search);
  const devices = new Map(); // id -> { id, name, state, bpm, min, max, avg, updated, history: [[t, bpm]] }
  let currentId = null;
  let socketUp = false;
  let liveBpm = 0;
  let shownBpm = 0;
  let rgbNow = null;
  let rgbTarget = hexToRgb(cfg.accent);
  let nextBeat = 0;
  const beats = []; // recent beat times, so the ECG trace is right even at low frame rates
  let lastFrame = performance.now();
  let lastSpark = 0;
  let alerting = false;
  let offSince = performance.now();
  let ui = {};
  const ecg = { data: [], lastT: 0 };

  const t = key => (TEXT[cfg.lang] || TEXT.tr)[key] ?? '';
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

  function hexToRgb(hex) {
    const n = parseInt(hex.slice(1), 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }

  function zoneFor(bpm) {
    const ratio = bpm / cfg.maxHr;
    let zone = ZONES[0];
    for (const z of ZONES) if (ratio >= z.from) zone = z;
    return zone;
  }

  // ── Markup ─────────────────────────────────────────────────────────
  const parts = {
    heart: () => `<div class="heart"><span class="ring"></span><span class="ring"></span>${HEART}</div>`,
    sub: () => `<div class="sub"><span class="unit">${esc(cfg.label)}</span><span class="zname"></span>${parts.scares()}</div>`,
    scares: () => (cfg.scareCount ? '<span class="scares" hidden>😱 <b>0</b></span>' : ''),
    // Room above the panel for record badges and scare pops, so they never get clipped in OBS
    fxslot: () => (cfg.scare || cfg.record !== 'off' ? '<div class="fxslot"></div>' : ''),
    dot: () => (cfg.dot ? '<span class="dot"></span>' : ''),
    name: () => (cfg.name ? '<div class="dname"></div>' : ''),
    spark: () => (cfg.graph ? '<canvas class="spark"></canvas>' : ''),
    stats: () => (cfg.stats
      ? `<div class="stats"><span><b class="smin">--</b><i>${t('min')}</i></span><span><b class="savg">--</b><i>${t('avg')}</i></span><span><b class="smax">--</b><i>${t('max')}</i></span></div>`
      : ''),
    zbar: () => (cfg.zonebar
      ? `<div class="zbar">${ZONES.slice(1).map(z => `<span class="zs" style="--zc:${z.color}"></span>`).join('')}<span class="zmark"></span></div>`
      : ''),
  };

  const classic = () =>
    `<div class="panel">${parts.dot()}${parts.name()}<div class="main">${parts.heart()}<div class="readout"><div class="num">--</div>${parts.sub()}</div>${parts.spark()}</div>${parts.zbar()}${parts.stats()}</div>`;

  const TEMPLATES = {
    card: classic,
    minimal: classic,
    neon: classic,
    ring: () =>
      `<div class="panel">${parts.dot()}${parts.name()}<div class="gauge"><svg viewBox="0 0 200 200"><path class="track" d="${ARC}" pathLength="100"/><path class="prog" d="${ARC}" pathLength="100"/></svg><div class="gcenter">${parts.heart()}<div class="num">--</div><span class="unit">${esc(cfg.label)}</span></div></div><div class="rsub"><span class="zname"></span>${parts.scares()}</div>${parts.zbar()}${parts.stats()}</div>`,
    ecg: () =>
      `<div class="panel">${parts.dot()}${parts.name()}<div class="main"><canvas class="ecg"></canvas><div class="readout"><div class="top">${parts.heart()}<div class="num">--</div></div>${parts.sub()}</div></div>${parts.zbar()}${parts.stats()}</div>`,
  };

  function build() {
    document.body.className = 'align-' + cfg.align;
    const w = document.getElementById('w');
    w.className = 'w theme-' + cfg.theme;
    const s = w.style;
    s.setProperty('--font', FONTS[cfg.font]);
    s.setProperty('--text', cfg.text);
    s.setProperty('--bgA', String(cfg.bg / 100));
    s.setProperty('--r', cfg.radius + 'px');
    s.setProperty('--s', String(cfg.scale));
    w.innerHTML = parts.fxslot() + TEMPLATES[cfg.theme]();

    const q = sel => w.querySelector(sel);
    ui = {
      w,
      panel: q('.panel'),
      num: q('.num'),
      heart: q('.heart svg'),
      rings: [...w.querySelectorAll('.ring')],
      zname: q('.zname'),
      dname: q('.dname'),
      spark: q('canvas.spark'),
      ecg: q('canvas.ecg'),
      prog: q('.prog'),
      zsegs: [...w.querySelectorAll('.zs')],
      zmark: q('.zmark'),
      smin: q('.smin'),
      savg: q('.savg'),
      smax: q('.smax'),
      fxslot: q('.fxslot'),
      scares: q('.scares'),
      lastNum: null,
      badge: null,
    };
    for (const c of [ui.spark, ui.ecg]) if (c) sizeCanvas(c);
    ecg.data = [];
    shownBpm = 0;
    applyColor(rgbNow || rgbTarget);
    render();
  }

  function sizeCanvas(c) {
    // The widget is scaled with a transform, so render canvases at the scaled resolution
    const k = (window.devicePixelRatio || 1) * cfg.scale;
    c.width = Math.round(c.clientWidth * k);
    c.height = Math.round(c.clientHeight * k);
    c._k = k;
  }

  function applyColor(rgb) {
    const value = rgb.map(Math.round).join(',');
    if (!ui.w || ui.rgb === value) return;
    ui.w.style.setProperty('--rgb', value);
    ui.rgb = value;
  }

  // ── Data ───────────────────────────────────────────────────────────
  function ensure(id, name) {
    let d = devices.get(id);
    if (!d) devices.set(id, (d = { id, name: name || id, state: 'off', attempt: 0, bpm: 0, min: 0, max: 0, avg: 0, updated: 0, history: [] }));
    if (name) d.name = name;
    return d;
  }

  function trim(d) {
    const cutoff = Date.now() - 600000;
    while (d.history.length && d.history[0][0] < cutoff) d.history.shift();
  }

  function onMessage(msg) {
    switch (msg.type) {
      case 'snapshot':
        devices.clear();
        for (const d of msg.devices) devices.set(d.id, { ...d, history: d.history || [] });
        break;
      case 'hr': {
        const d = ensure(msg.id, msg.name);
        Object.assign(d, {
          bpm: msg.bpm, min: msg.min, max: msg.max, avg: msg.avg, updated: msg.t,
          scares: msg.scares || 0, alltime: msg.alltime || 0,
        });
        d.history.push([msg.t, msg.bpm]);
        trim(d);
        break;
      }
      case 'state': {
        const d = ensure(msg.id, msg.name);
        d.state = msg.state;
        d.attempt = msg.attempt || 0;
        break;
      }
      case 'removed':
        devices.delete(msg.id);
        break;
      case 'event': {
        const d = devices.get(msg.id);
        if (d && msg.kind === 'scare') d.scares = msg.count;
        render();
        // Only celebrate the watch this overlay is showing
        if (d && d === current()) fireEvent(msg.kind, msg.scope, msg.bpm, msg.count);
        return;
      }
    }
    render();
  }

  /** The watch to show: the one in the URL, else stick with the current one while it is live. */
  function current() {
    if (cfg.device) return devices.get(cfg.device) || null;
    const cur = currentId && devices.get(currentId);
    if (cur && cur.state === 'live') return cur;
    let best = null;
    for (const d of devices.values())
      if (d.state === 'live' && (!best || d.updated > best.updated)) best = d;
    if (!best) best = cur || [...devices.values()].sort((a, b) => (b.updated || 0) - (a.updated || 0))[0] || null;
    currentId = best ? best.id : null;
    return best;
  }

  // ── Render (on every data change) ──────────────────────────────────
  function render() {
    if (!ui.w) return;
    const d = current();
    const live = !!(socketUp && d && d.state === 'live' && d.bpm > 0);
    const status = live ? 'live' : !socketUp ? 'noapp' : !d ? 'nodevice' : d.state || 'off';

    liveBpm = live ? d.bpm : 0;
    ui.w.classList.toggle('off', !live);
    ui.w.dataset.status = status;
    if (live) offSince = 0;
    else if (!offSince) offSince = performance.now();

    const zone = live ? zoneFor(d.bpm) : null;
    rgbTarget = hexToRgb(
      cfg.colorMode === 'zone' ? (zone ? zone.color : '#8a8fa3') : cfg.accent);

    if (ui.zname) ui.zname.textContent = live ? (cfg.zname ? t(zone.key) : '') : t(status);
    if (ui.dname) ui.dname.textContent = d ? d.name : '';

    if (ui.smin) {
      const has = d && d.min > 0;
      ui.smin.textContent = has ? d.min : '--';
      ui.savg.textContent = has ? d.avg : '--';
      ui.smax.textContent = has ? d.max : '--';
    }

    if (ui.prog) {
      const p = live ? clamp((d.bpm - 40) / (cfg.maxHr - 40), 0, 1) : 0;
      ui.prog.style.strokeDasharray = `${(p * 100).toFixed(1)} 100`;
    }

    if (ui.zmark) {
      const idx = zone ? ['z1', 'z2', 'z3', 'z4', 'z5'].indexOf(zone.key) : -1;
      ui.zsegs.forEach((s, i) => s.classList.toggle('on', i === idx));
      const pct = live ? clamp((d.bpm / cfg.maxHr - 0.5) / 0.5, 0, 1) : 0;
      ui.zmark.style.left = `calc(${(pct * 100).toFixed(1)}% - 1.5px)`;
      ui.zmark.style.opacity = live ? 1 : 0;
    }

    if (ui.scares) {
      const count = (d && d.scares) || 0;
      ui.scares.hidden = count === 0;
      ui.scares.querySelector('b').textContent = count;
    }

    // Small hysteresis so the effect doesn't flicker right at the threshold
    alerting = cfg.alert > 0 && live && d.bpm >= (alerting ? cfg.alert - 3 : cfg.alert);
    ui.w.classList.toggle('fx-shake', alerting && cfg.alertFx !== 'flash');
  }

  // ── Animation loop ─────────────────────────────────────────────────
  function frame(now) {
    const dt = Math.min(100, now - lastFrame);
    lastFrame = now;

    // Number: glide towards the latest value
    if (shownBpm !== liveBpm) {
      if (!liveBpm || !shownBpm || !cfg.tween) shownBpm = liveBpm;
      else {
        shownBpm += (liveBpm - shownBpm) * Math.min(1, dt / 140);
        if (Math.abs(liveBpm - shownBpm) < 0.5) shownBpm = liveBpm;
      }
    }
    const text = shownBpm ? String(Math.round(shownBpm)) : '--';
    if (ui.num && ui.lastNum !== text) { ui.num.textContent = text; ui.lastNum = text; }

    // Colour: fade between zone colours
    if (!rgbNow) rgbNow = rgbTarget.slice();
    for (let i = 0; i < 3; i++) {
      const diff = rgbTarget[i] - rgbNow[i];
      rgbNow[i] = Math.abs(diff) > 0.5 ? rgbNow[i] + diff * Math.min(1, dt / 250) : rgbTarget[i];
    }
    applyColor(rgbNow);

    // Heartbeat in time with the BPM
    if (liveBpm > 0) {
      const interval = 60000 / liveBpm;
      if (now - nextBeat > 2000) nextBeat = now; // resync after a pause
      if (now >= nextBeat) {
        while (nextBeat <= now) { beats.push(nextBeat); nextBeat += interval; }
        if (beats.length > 12) beats.splice(0, beats.length - 12);
        beat(interval);
      }
    }

    if (ui.ecg) drawEcg(now);
    if (ui.spark && now - lastSpark > 120) { drawSpark(); lastSpark = now; }

    ui.w && ui.w.classList.toggle('hidden', cfg.hideOff && !liveBpm && offSince > 0 && now - offSince > 2500);
    requestAnimationFrame(frame);
  }

  function beat(interval) {
    const dur = Math.min(900, interval * 0.9);
    if (cfg.beat && ui.heart) {
      ui.heart.animate(
        [
          { transform: 'scale(1)' },
          { transform: 'scale(1.2)', offset: 0.14 },
          { transform: 'scale(1)', offset: 0.3 },
          { transform: 'scale(1.1)', offset: 0.44 },
          { transform: 'scale(1)' },
        ],
        { duration: Math.min(700, dur), easing: 'ease-out' });
      ui.rings.forEach((ring, i) => ring.animate(
        [{ transform: 'scale(.8)', opacity: i ? 0.35 : 0.7 }, { transform: 'scale(1.9)', opacity: 0 }],
        { duration: dur, delay: i * 90, easing: 'ease-out' }));
    }
    if (alerting && cfg.alertFx !== 'shake' && ui.panel) {
      ui.panel.animate(
        [
          { boxShadow: '0 0 0 0 rgba(255,30,60,0)' },
          { boxShadow: '0 0 38px 8px rgba(255,30,60,.85)', offset: 0.2 },
          { boxShadow: '0 0 0 0 rgba(255,30,60,0)' },
        ],
        { duration: Math.min(650, dur) });
    }
  }

  // ── Event effects (scare moment, new record) ───────────────────────
  function fireEvent(kind, scope, bpm, count) {
    if (kind === 'scare' && cfg.scare) scareFx(count);
    if (kind === 'record' && cfg.record !== 'off') {
      // An all-time record is also a stream record
      if (scope === 'alltime' && cfg.record === 'session') scope = 'session';
      if (cfg.record === 'both' || cfg.record === scope) recordFx(scope, bpm);
    }
  }

  function scareFx(count) {
    if (!ui.fxslot) return;
    const pop = document.createElement('div');
    pop.className = 'scare-pop';
    pop.innerHTML = `<i>😱</i><span>${esc(t('scared'))}</span>`;
    ui.fxslot.append(pop);
    pop.animate(
      [
        { transform: 'translateY(16px) scale(.3) rotate(-14deg)', opacity: 0 },
        { transform: 'translateY(-5px) scale(1.25) rotate(6deg)', opacity: 1, offset: 0.16 },
        { transform: 'translateY(0) scale(1) rotate(0)', opacity: 1, offset: 0.28 },
        { transform: 'translateY(0) scale(1)', opacity: 1, offset: 0.82 },
        { transform: 'translateY(-12px) scale(.9)', opacity: 0 },
      ],
      { duration: 2800, easing: 'ease-out' }).onfinish = () => pop.remove();
    pop.querySelector('i').animate(
      [{ transform: 'rotate(0)' }, { transform: 'rotate(-12deg)' }, { transform: 'rotate(12deg)' }, { transform: 'rotate(0)' }],
      { duration: 260, iterations: 5, delay: 300 });

    if (ui.panel) {
      ui.panel.animate(
        [
          { transform: 'translate(0,0)' }, { transform: 'translate(-5px,2px) rotate(-1deg)' },
          { transform: 'translate(5px,-2px) rotate(1deg)' }, { transform: 'translate(-3px,-1px)' },
          { transform: 'translate(3px,1px)' }, { transform: 'translate(0,0)' },
        ],
        { duration: 520, easing: 'linear' });
      ui.panel.animate(
        [
          { boxShadow: '0 0 0 0 rgba(139,92,246,0)' },
          { boxShadow: '0 0 40px 8px rgba(139,92,246,.85)', offset: 0.2 },
          { boxShadow: '0 0 0 0 rgba(139,92,246,0)' },
        ],
        { duration: 1400 });
    }
    if (ui.scares && count) {
      ui.scares.hidden = false;
      ui.scares.querySelector('b').textContent = count;
      ui.scares.animate([{ transform: 'scale(1)' }, { transform: 'scale(1.5)' }, { transform: 'scale(1)' }],
        { duration: 500, delay: 250, easing: 'ease-out' });
    }
  }

  function recordFx(scope, bpm) {
    if (!ui.fxslot) return;
    const current = ui.badge;
    // While a badge is up, a climbing heart rate just updates its number
    if (current && (current.scope === scope || current.scope === 'alltime')) {
      current.el.querySelector('b').textContent = bpm;
      current.el.querySelector('b').animate([{ transform: 'scale(1.35)' }, { transform: 'scale(1)' }], { duration: 350 });
      clearTimeout(current.timer);
      current.timer = setTimeout(hideBadge, 7000);
      return;
    }
    if (current) { clearTimeout(current.timer); current.el.remove(); }

    const el = document.createElement('div');
    el.className = 'rec-badge ' + scope;
    el.innerHTML = `${scope === 'alltime' ? '👑' : '🏆'}<i>${esc(t(scope === 'alltime' ? 'recAll' : 'recSession'))}</i><b>${bpm}</b>`;
    ui.fxslot.append(el);
    el.animate(
      [
        { transform: 'translate(-50%, 18px) scale(.6)', opacity: 0 },
        { transform: 'translate(-50%, -3px) scale(1.08)', opacity: 1, offset: 0.65 },
        { transform: 'translate(-50%, 0) scale(1)', opacity: 1 },
      ],
      { duration: 520, easing: 'ease-out' });
    ui.badge = { el, scope, timer: setTimeout(hideBadge, 7000) };

    if (ui.num) {
      ui.num.animate(
        [
          { filter: 'drop-shadow(0 0 0 rgba(255,200,60,0))' },
          { filter: 'drop-shadow(0 0 18px rgba(255,200,60,1))', offset: 0.25 },
          { filter: 'drop-shadow(0 0 0 rgba(255,200,60,0))' },
        ],
        { duration: 1600 });
    }
  }

  function hideBadge() {
    const badge = ui.badge;
    if (!badge) return;
    ui.badge = null;
    badge.el.animate(
      [{ transform: 'translate(-50%, 0)', opacity: 1 }, { transform: 'translate(-50%, 10px)', opacity: 0 }],
      { duration: 400, easing: 'ease-in' }).onfinish = () => badge.el.remove();
  }

  function rgbString() { return (rgbNow || rgbTarget).map(Math.round).join(','); }

  function fadeLeftEdge(g, W, H, share) {
    g.globalCompositeOperation = 'destination-out';
    const fade = g.createLinearGradient(0, 0, W * share, 0);
    fade.addColorStop(0, 'rgba(0,0,0,1)');
    fade.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = fade;
    g.fillRect(0, 0, W * share, H);
    g.globalCompositeOperation = 'source-over';
  }

  /** Line chart of recent BPM. */
  function drawSpark() {
    const c = ui.spark, g = c.getContext('2d'), k = c._k;
    const W = c.width / k, H = c.height / k;
    g.setTransform(k, 0, 0, k, 0, 0);
    g.clearRect(0, 0, W, H);

    const d = current();
    if (!d || !d.history.length) return;
    const end = Date.now(), start = end - cfg.window * 1000;
    const pts = d.history.filter(p => p[0] >= start - 2000);
    if (pts.length < 2) return;

    let lo = Infinity, hi = -Infinity;
    for (const p of pts) { lo = Math.min(lo, p[1]); hi = Math.max(hi, p[1]); }
    const mid = (lo + hi) / 2, span = Math.max(24, hi - lo + 10);
    lo = mid - span / 2; hi = mid + span / 2;

    const pad = 4;
    const X = ts => pad + ((ts - start) / (end - start)) * (W - pad * 2);
    const Y = v => H - pad - ((v - lo) / (hi - lo)) * (H - pad * 2);
    const rgb = rgbString();

    const line = new Path2D();
    pts.forEach((p, i) => (i ? line.lineTo(X(p[0]), Y(p[1])) : line.moveTo(X(p[0]), Y(p[1]))));
    const area = new Path2D(line);
    area.lineTo(X(pts[pts.length - 1][0]), H);
    area.lineTo(X(pts[0][0]), H);
    area.closePath();

    g.globalAlpha = liveBpm ? 1 : 0.4;
    const grad = g.createLinearGradient(0, 0, 0, H);
    grad.addColorStop(0, `rgba(${rgb},.38)`);
    grad.addColorStop(1, `rgba(${rgb},0)`);
    g.fillStyle = grad;
    g.fill(area);

    g.lineJoin = 'round';
    g.lineCap = 'round';
    g.lineWidth = 2;
    g.strokeStyle = `rgb(${rgb})`;
    g.shadowColor = `rgba(${rgb},.8)`;
    g.shadowBlur = 6;
    g.stroke(line);
    g.shadowBlur = 0;

    const last = pts[pts.length - 1];
    g.fillStyle = '#fff';
    g.beginPath();
    g.arc(X(last[0]), Y(last[1]), 2.6, 0, Math.PI * 2);
    g.fill();
    g.globalAlpha = 1;

    fadeLeftEdge(g, W, H, 0.22);
  }

  /** Stylised ECG trace (a visual effect timed to the BPM, not a medical signal). */
  function ecgWave(ms, interval) {
    const x = ms / Math.min(1, interval / 750); // squeeze the complex at high heart rates
    if (x < 0) return 0;
    const bump = (c, w, a) => a * Math.exp(-((x - c) ** 2) / (2 * w * w));
    return bump(70, 20, 0.1) - bump(150, 7, 0.12) + bump(170, 8, 1) - bump(190, 8, 0.26) + bump(360, 45, 0.2);
  }

  function ecgAt(ts, interval) {
    for (let i = beats.length - 1; i >= 0; i--)
      if (beats[i] <= ts) return ecgWave(ts - beats[i], interval);
    return 0;
  }

  function drawEcg(now) {
    const c = ui.ecg, g = c.getContext('2d'), k = c._k;
    const W = c.width / k, H = c.height / k, n = Math.ceil(W);
    if (ecg.data.length !== n) ecg.data = new Array(n).fill(0);

    // One sample per pixel of scroll, each at its own timestamp (frame-rate independent)
    const step = 1 / ECG_SPEED;
    if (!ecg.lastT || now - ecg.lastT > n * step) ecg.lastT = now - n * step;
    const interval = liveBpm ? 60000 / liveBpm : 1000;
    while (ecg.lastT + step <= now) {
      ecg.lastT += step;
      ecg.data.push(liveBpm ? ecgAt(ecg.lastT, interval) : 0);
    }
    if (ecg.data.length > n) ecg.data.splice(0, ecg.data.length - n);

    g.setTransform(k, 0, 0, k, 0, 0);
    g.clearRect(0, 0, W, H);
    const base = H * 0.64, amp = H * 0.52, rgb = rgbString();

    g.beginPath();
    ecg.data.forEach((v, x) => (x ? g.lineTo(x, base - v * amp) : g.moveTo(x, base - v * amp)));
    g.lineWidth = 2;
    g.lineJoin = 'round';
    g.strokeStyle = `rgba(${rgb},${liveBpm ? 1 : 0.45})`;
    g.shadowColor = `rgb(${rgb})`;
    g.shadowBlur = 8;
    g.stroke();
    g.shadowBlur = 0;

    g.fillStyle = '#fff';
    g.beginPath();
    g.arc(W - 2, base - ecg.data[n - 1] * amp, 2.6, 0, Math.PI * 2);
    g.fill();

    fadeLeftEdge(g, W, H, 0.35);
  }

  // ── Data sources ───────────────────────────────────────────────────
  function connect() {
    if (cfg.demo) return startDemo();
    const ws = new WebSocket(`ws://${location.host}/ws`);
    ws.onopen = () => { socketUp = true; render(); };
    ws.onmessage = e => { try { onMessage(JSON.parse(e.data)); } catch { /* ignore malformed */ } };
    ws.onclose = () => { socketUp = false; render(); setTimeout(connect, 2000); };
    ws.onerror = () => ws.close();
  }

  /** Fake watch for the editor preview: sweeps through every zone. */
  function startDemo() {
    socketUp = true;
    const d = ensure('DEMO', t('demo'));
    let tick = 0, v = 82, sum = 0, count = 0;
    const next = () => {
      tick++;
      const target = 118 + 52 * Math.sin(tick / 24) + 9 * Math.sin(tick / 5.5);
      v += clamp((target - v) * 0.3 + (Math.random() * 4 - 2), -6, 6);
      return Math.round(v);
    };
    const push = (ts, bpm) => {
      d.bpm = bpm; d.updated = ts;
      d.history.push([ts, bpm]);
      sum += bpm; count++;
      d.min = d.min ? Math.min(d.min, bpm) : bpm;
      d.max = Math.max(d.max, bpm);
      d.avg = Math.round(sum / count);
      trim(d);
    };
    const now = Date.now();
    for (let i = 300; i > 0; i--) push(now - i * 1000, next());
    d.state = cfg.demoState;
    setInterval(() => { d.state = cfg.demoState; push(Date.now(), next()); render(); }, 1000);
    render();
  }

  // Live config updates and effect tests from the editor preview (same origin only)
  let testScope = 'session';
  window.addEventListener('message', e => {
    if (e.origin !== location.origin || !e.data) return;
    if (e.data.type === 'pulse-test') {
      const d = current();
      if (e.data.kind === 'scare') {
        if (d) d.scares = (d.scares || 0) + 1;
        fireEvent('scare', null, 0, d ? d.scares : 1);
      } else {
        // "both" alternates so each badge style can be previewed
        testScope = cfg.record === 'both' ? (testScope === 'session' ? 'alltime' : 'session') : cfg.record;
        fireEvent('record', testScope, liveBpm || (d && d.bpm) || 150);
      }
      return;
    }
    if (e.data.type !== 'pulse-config') return;
    cfg = parseConfig(e.data.query);
    const demo = devices.get('DEMO');
    if (demo) { demo.state = cfg.demoState; demo.name = t('demo'); }
    build();
  });

  window.addEventListener('resize', () => { for (const c of [ui.spark, ui.ecg]) if (c) sizeCanvas(c); });

  build();
  connect();
  requestAnimationFrame(frame);
})();
