/* Overlay editor: builds the overlay URL and drives a live preview iframe. */
(() => {
  'use strict';

  const STORAGE_KEY = 'pulseoverlay.editor.v1';

  const THEMES = [
    { id: 'card', name: 'Kart', ico: '▭' },
    { id: 'minimal', name: 'Sade', ico: '♥' },
    { id: 'neon', name: 'Neon', ico: '✦' },
    { id: 'ring', name: 'Gösterge', ico: '◔' },
    { id: 'ecg', name: 'EKG', ico: '〰' },
  ];
  const GRAPH_THEMES = ['card', 'minimal', 'neon'];

  // Must mirror parseConfig() in overlay.js
  const DEFAULTS = theme => ({
    theme,
    color: 'zone', accent: '#ff3b5c', text: '#ffffff', font: 'bahnschrift',
    scale: 1, bg: theme === 'minimal' ? 0 : 70, radius: 18, align: 'center',
    beat: true, tween: true, graph: theme !== 'minimal', window: 60,
    stats: false, zonebar: false, zname: true, name: false, dot: true,
    label: 'BPM', lang: 'tr', maxhr: 190, alert: 0, alertfx: 'flash', hideoff: false,
    scare: true, scarecount: true, record: 'both',
  });

  const PRESETS = [
    { name: 'Klasik kart', v: { theme: 'card' } },
    { name: 'Yayın köşesi', v: { theme: 'card', stats: true, zonebar: true, name: true } },
    { name: 'Oyun (sade)', v: { theme: 'minimal', scale: 1.2 } },
    { name: 'Neon', v: { theme: 'neon', color: 'accent', accent: '#00e5ff', bg: 80 } },
    { name: 'Gösterge', v: { theme: 'ring', stats: true } },
    { name: 'EKG monitörü', v: { theme: 'ecg', color: 'accent', accent: '#39ff88', font: 'consolas', bg: 85, radius: 10 } },
  ];

  const ZONE_INFO = [
    ['Isınma', 0.5, 0.6, '#38bdf8'],
    ['Yağ yakımı', 0.6, 0.7, '#22c55e'],
    ['Aerobik', 0.7, 0.8, '#facc15'],
    ['Anaerobik', 0.8, 0.9, '#fb923c'],
    ['Maksimum', 0.9, 1.0, '#ef4444'],
  ];

  const SECTIONS = [
    { title: 'Tema', items: [{ key: 'theme', type: 'themes' }] },
    { title: 'Görünüm', items: [
      { key: 'font', type: 'select', label: 'Yazı tipi', options: [
        ['bahnschrift', 'Bahnschrift'], ['segoe', 'Segoe UI'], ['consolas', 'Mono (Cascadia)'],
        ['impact', 'Impact'], ['arialblack', 'Arial Black'], ['georgia', 'Georgia']] },
      { key: 'scale', type: 'range', label: 'Boyut', min: 0.5, max: 3, step: 0.05, fmt: v => `%${Math.round(v * 100)}` },
      { key: 'bg', type: 'range', label: 'Arka plan opaklığı', min: 0, max: 100, step: 1, fmt: v => `%${v}` },
      { key: 'radius', type: 'range', label: 'Köşe yuvarlaklığı', min: 0, max: 40, step: 1, fmt: v => `${v} px` },
      { key: 'align', type: 'segment', label: 'Hizalama', options: [['left', 'Sol'], ['center', 'Orta'], ['right', 'Sağ']] },
    ] },
    { title: 'Renkler', items: [
      { key: 'color', type: 'segment', label: 'Renk modu', options: [['zone', 'Nabız bölgesine göre'], ['accent', 'Sabit renk']] },
      { key: 'accent', type: 'color', label: 'Vurgu rengi', hint: s => (s.color === 'zone' ? 'Bölge modunda sadece bağlantı yokken kullanılır.' : '') },
      { key: 'text', type: 'color', label: 'Yazı rengi' },
    ] },
    { title: 'Bileşenler', items: [
      { key: 'beat', type: 'check', label: 'Kalp atışı animasyonu' },
      { key: 'tween', type: 'check', label: 'Yumuşak sayı geçişi' },
      { key: 'graph', type: 'check', label: 'Canlı nabız grafiği', enabled: s => GRAPH_THEMES.includes(s.theme) },
      { key: 'window', type: 'select', label: 'Grafik süresi', options: [['30', '30 saniye'], ['60', '1 dakika'], ['120', '2 dakika'], ['300', '5 dakika']],
        enabled: s => GRAPH_THEMES.includes(s.theme) && s.graph },
      { key: 'stats', type: 'check', label: 'Min / Ort / Maks' },
      { key: 'zonebar', type: 'check', label: 'Bölge çubuğu' },
      { key: 'zname', type: 'check', label: 'Bölge adı' },
      { key: 'name', type: 'check', label: 'Saat adı' },
      { key: 'dot', type: 'check', label: 'Bağlantı noktası', enabled: s => s.theme !== 'minimal' },
      { key: 'label', type: 'text', label: 'Birim yazısı', maxlength: 16 },
      { key: 'lang', type: 'segment', label: 'Overlay dili', options: [['tr', 'Türkçe'], ['en', 'English']] },
    ] },
    { title: 'Korku anı ve rekorlar', items: [
      { key: 'scare', type: 'check', label: 'Korku anı efekti (😱 Korktu!)' },
      { key: 'scarecount', type: 'check', label: 'Korku sayacını göster', enabled: s => s.scare },
      { key: 'record', type: 'segment', label: 'Rekor kutlaması',
        options: [['off', 'Kapalı'], ['session', 'Yayın'], ['alltime', 'Tüm zamanlar'], ['both', 'İkisi']] },
      { key: '_test', type: 'test' },
      { key: '_info', type: 'info', text: 'Korku anı: nabız ~10 saniyede 20+ artarsa. Yayın rekoru ilk 2 dakikadan sonra ve '
          + 'ortalamanın 15 üstündeyse kutlanır. Tüm zamanların rekoru bilgisayarında saklanır (records.json); '
          + 'sıfırlamak için uygulamadaki “Rekoru sıfırla”.' },
    ] },
    { title: 'Nabız bölgeleri', items: [{ key: 'maxhr', type: 'maxhr', label: 'Maksimum nabız' }] },
    { title: 'Efektler', items: [
      { key: 'alert', type: 'number', label: 'Uyarı eşiği (BPM)', min: 0, max: 250,
        hint: () => '0 = kapalı. Nabız bu değeri geçince efekt devreye girer.' },
      { key: 'alertfx', type: 'segment', label: 'Uyarı efekti', options: [['flash', 'Parlama'], ['shake', 'Titreme'], ['both', 'İkisi']],
        enabled: s => s.alert > 0 },
      { key: 'hideoff', type: 'check', label: 'Sinyal yokken overlay’i gizle' },
    ] },
  ];

  // ── State ──────────────────────────────────────────────────────────
  let state = DEFAULTS('card');
  const preview = { demo: true, demoState: 'live', bg: 'checker', device: '' };
  load();

  const $ = sel => document.querySelector(sel);
  const frame = $('#frame');
  let frameKey = '';
  let frameReady = false;
  let updateTimer = 0;

  function load() {
    try {
      const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null');
      if (saved && saved.state) {
        state = { ...DEFAULTS(saved.state.theme || 'card'), ...saved.state };
        Object.assign(preview, saved.preview || {});
      }
    } catch { /* storage unavailable */ }
  }

  function save() {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify({ state, preview })); } catch { /* ignore */ }
  }

  // ── URL ────────────────────────────────────────────────────────────
  function query(forPreview) {
    const defaults = DEFAULTS(state.theme);
    const p = new URLSearchParams();
    for (const [key, value] of Object.entries(state)) {
      const isDefault = key === 'theme' ? value === 'card' : String(value) === String(defaults[key]);
      if (isDefault) continue;
      p.set(key, typeof value === 'boolean' ? (value ? '1' : '0') : String(value).replace(/^#/, ''));
    }
    // The demo watch has its own id, so a chosen device only applies to real data
    if (preview.device && !(forPreview && preview.demo)) p.set('device', preview.device);
    if (forPreview && preview.demo) {
      p.set('demo', '1');
      if (preview.demoState !== 'live') p.set('demostate', preview.demoState);
    }
    return p.toString();
  }

  function obsUrl() {
    const q = query(false);
    return `${location.origin}/overlay${q ? '?' + q : ''}`;
  }

  // ── Preview ────────────────────────────────────────────────────────
  function scheduleUpdate() {
    clearTimeout(updateTimer);
    updateTimer = setTimeout(update, 60);
  }

  function update() {
    save();
    $('#url').value = obsUrl();

    // Data source changes need a reload; everything else is pushed live
    const key = `${preview.demo}|${preview.device}`;
    const q = query(true);
    if (key !== frameKey) {
      frameKey = key;
      frameReady = false;
      frame.src = '/overlay?' + q;
    } else if (frameReady) {
      frame.contentWindow.postMessage({ type: 'pulse-config', query: '?' + q }, location.origin);
    }
    refreshEnabled();
  }

  frame.addEventListener('load', () => {
    frameReady = true;
    frame.contentWindow.postMessage({ type: 'pulse-config', query: '?' + query(true) }, location.origin);
  });

  // Suggested OBS browser-source size, measured from the rendered widget
  setInterval(() => {
    try {
      const w = frame.contentDocument && frame.contentDocument.getElementById('w');
      if (!w) return;
      const r = w.getBoundingClientRect();
      if (!r.width) return;
      const W = Math.ceil((r.width + 56) / 10) * 10;
      const H = Math.ceil((r.height + 56) / 10) * 10;
      $('#size').textContent = `Önerilen OBS boyutu: ${W} × ${H}`;
    } catch { /* frame not ready */ }
  }, 500);

  // ── Controls ───────────────────────────────────────────────────────
  const controls = $('#controls');
  const refreshers = [];

  function el(tag, attrs = {}, ...children) {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs)) {
      if (k === 'class') node.className = v;
      else if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
      else node.setAttribute(k, v);
    }
    for (const c of children) node.append(c);
    return node;
  }

  function set(key, value) {
    state[key] = value;
    scheduleUpdate();
  }

  function setTheme(theme) {
    const before = DEFAULTS(state.theme), after = DEFAULTS(theme);
    // Carry over customised values, but follow the new theme's defaults for untouched ones
    for (const key of ['bg', 'graph']) if (state[key] === before[key]) state[key] = after[key];
    state.theme = theme;
    renderAll();
    scheduleUpdate();
  }

  function segment(options, get, onPick) {
    const wrap = el('div', { class: 'seg', role: 'group' });
    const buttons = options.map(([value, label]) => {
      const b = el('button', { type: 'button', onclick: () => { onPick(value); paint(); } }, label);
      wrap.append(b);
      return [value, b];
    });
    const paint = () => buttons.forEach(([value, b]) => {
      b.classList.toggle('on', String(get()) === String(value));
      b.setAttribute('aria-pressed', String(get()) === String(value));
    });
    paint();
    wrap.paint = paint;
    return wrap;
  }

  function field(item, ...body) {
    const box = el('div', { class: 'field' });
    if (item.label && item.type !== 'check') {
      const top = el('div', { class: 'top' }, el('label', { for: 'f-' + item.key }, item.label));
      if (item.fmt) top.append(el('span', { class: 'val', id: 'v-' + item.key }, item.fmt(state[item.key])));
      box.append(top);
    }
    box.append(...body);
    if (item.hint) {
      const hint = el('div', { class: 'hint' });
      box.append(hint);
      refreshers.push(() => { hint.textContent = item.hint(state); hint.hidden = !hint.textContent; });
    }
    if (item.enabled) refreshers.push(() => box.classList.toggle('disabled', !item.enabled(state)));
    return box;
  }

  function buildItem(item) {
    const id = 'f-' + item.key;
    switch (item.type) {
      case 'themes': {
        const grid = el('div', { class: 'themes' });
        for (const th of THEMES) {
          grid.append(el('button', {
            type: 'button', class: 'theme' + (state.theme === th.id ? ' on' : ''),
            'aria-pressed': state.theme === th.id, onclick: () => setTheme(th.id),
          }, el('span', { class: 'ico' }, th.ico), th.name));
        }
        return grid;
      }
      case 'select': {
        const s = el('select', { id, onchange: e => set(item.key, item.key === 'window' ? Number(e.target.value) : e.target.value) });
        for (const [v, label] of item.options) s.append(el('option', { value: v }, label));
        s.value = String(state[item.key]);
        return field(item, s);
      }
      case 'range': {
        const r = el('input', { id, type: 'range', min: item.min, max: item.max, step: item.step, value: state[item.key] });
        r.addEventListener('input', () => {
          const v = Number(r.value);
          $('#v-' + item.key).textContent = item.fmt(v);
          set(item.key, v);
        });
        return field(item, r);
      }
      case 'segment':
        return field(item, segment(item.options, () => state[item.key], v => set(item.key, v)));
      case 'color': {
        const code = el('code', {}, state[item.key]);
        const c = el('input', { id, type: 'color', value: state[item.key] });
        c.addEventListener('input', () => { code.textContent = c.value; set(item.key, c.value); });
        return field(item, el('div', { class: 'colorrow' }, c, code));
      }
      case 'check': {
        const box = el('input', { id, type: 'checkbox' });
        box.checked = !!state[item.key];
        box.addEventListener('change', () => set(item.key, box.checked));
        return field(item, el('label', { class: 'check' }, box, item.label));
      }
      case 'text': {
        const input = el('input', { id, type: 'text', maxlength: item.maxlength || 32, value: state[item.key] });
        input.addEventListener('input', () => set(item.key, input.value));
        return field(item, input);
      }
      case 'number': {
        const input = el('input', { id, type: 'number', min: item.min, max: item.max, value: state[item.key] });
        input.addEventListener('input', () => {
          const v = Number(input.value);
          if (Number.isFinite(v)) set(item.key, Math.min(item.max, Math.max(item.min, v)));
        });
        return field(item, input);
      }
      case 'test': {
        const fire = kind => {
          if (frameReady) frame.contentWindow.postMessage({ type: 'pulse-test', kind }, location.origin);
        };
        const scare = el('button', { type: 'button', class: 'btn', onclick: () => fire('scare') }, '😱 Korkuyu dene');
        const record = el('button', { type: 'button', class: 'btn', onclick: () => fire('record') }, '🏆 Rekoru dene');
        refreshers.push(() => {
          scare.disabled = !state.scare;
          record.disabled = state.record === 'off';
        });
        return el('div', { class: 'field testrow' }, scare, record);
      }
      case 'info':
        return el('div', { class: 'field hint' }, item.text);
      case 'maxhr': {
        const input = el('input', { id, type: 'number', min: 120, max: 230, value: state.maxhr });
        const age = el('input', { type: 'number', min: 10, max: 90, placeholder: 'Yaşın', 'aria-label': 'Yaş' });
        const zones = el('div', { class: 'zones' });
        const paintZones = () => {
          zones.replaceChildren();
          for (const [name, lo, hi, color] of ZONE_INFO) {
            zones.append(el('i', { style: `background:${color}` }), el('span', {}, name),
              el('b', {}, `${Math.round(state.maxhr * lo)}–${Math.round(state.maxhr * hi)}`));
          }
        };
        input.addEventListener('input', () => {
          const v = Number(input.value);
          if (v >= 120 && v <= 230) { set('maxhr', v); paintZones(); }
        });
        const calc = el('button', {
          type: 'button', class: 'btn', onclick: () => {
            const a = Number(age.value);
            if (a >= 10 && a <= 90) { input.value = 220 - a; set('maxhr', 220 - a); paintZones(); }
          },
        }, 'Yaştan hesapla');
        paintZones();
        return field(item, input, el('div', { class: 'agerow' }, age, calc),
          el('div', { class: 'hint' }, 'Bilmiyorsan yaklaşık değer: 220 − yaşın.'), zones);
      }
    }
    return el('div');
  }

  function renderAll() {
    controls.replaceChildren();
    refreshers.length = 0;
    for (const section of SECTIONS) {
      const block = el('section', { class: 'block' }, el('h2', {}, section.title));
      for (const item of section.items) block.append(buildItem(item));
      controls.append(block);
    }
    refreshEnabled();
  }

  function refreshEnabled() {
    refreshers.forEach(fn => fn());
    $('#demoStateGroup').hidden = !preview.demo;
  }

  // ── Presets, toolbar, output ───────────────────────────────────────
  for (const p of PRESETS) {
    $('#presets').append(el('button', {
      type: 'button', class: 'preset',
      onclick: () => { state = { ...DEFAULTS(p.v.theme), ...p.v }; renderAll(); scheduleUpdate(); },
    }, p.name));
  }

  $('#reset').addEventListener('click', () => { state = DEFAULTS(state.theme); renderAll(); scheduleUpdate(); });

  $('#dataSeg').append(segment([['demo', 'Demo veri'], ['live', 'Canlı']],
    () => (preview.demo ? 'demo' : 'live'),
    v => { preview.demo = v === 'demo'; scheduleUpdate(); }));

  const demoState = $('#demoState');
  demoState.value = preview.demoState;
  demoState.addEventListener('change', () => { preview.demoState = demoState.value; scheduleUpdate(); });

  const previewBox = $('#preview');
  const paintBg = () => { previewBox.className = 'preview bg-' + preview.bg; };
  $('#bgSeg').append(segment([['checker', 'Damalı'], ['game', 'Oyun'], ['dark', 'Koyu'], ['light', 'Açık']],
    () => preview.bg,
    v => { preview.bg = v; paintBg(); save(); }));
  paintBg();

  const deviceSelect = $('#device');
  deviceSelect.addEventListener('change', () => { preview.device = deviceSelect.value; scheduleUpdate(); });

  async function refreshDevices() {
    try {
      const res = await fetch('/api/state', { cache: 'no-store' });
      const data = await res.json();
      const known = new Set(['']);
      for (const d of data.devices) {
        known.add(d.id);
        let opt = deviceSelect.querySelector(`option[value="${CSS.escape(d.id)}"]`);
        if (!opt) deviceSelect.append(opt = el('option', { value: d.id }));
        opt.textContent = `${d.name} (${d.id})`;
      }
      // Keep a chosen watch selectable even while the app doesn't list it
      if (preview.device && !known.has(preview.device)) {
        known.add(preview.device);
        if (!deviceSelect.querySelector(`option[value="${CSS.escape(preview.device)}"]`))
          deviceSelect.append(el('option', { value: preview.device }, preview.device));
      }
      for (const opt of [...deviceSelect.options]) if (!known.has(opt.value)) opt.remove();
      deviceSelect.value = preview.device;
    } catch { /* app not reachable (editor opened as a file) */ }
  }
  refreshDevices();
  setInterval(refreshDevices, 3000);

  $('#copy').addEventListener('click', async () => {
    const button = $('#copy'), url = $('#url');
    try { await navigator.clipboard.writeText(url.value); }
    catch { url.select(); document.execCommand('copy'); }
    button.textContent = 'Kopyalandı ✓';
    setTimeout(() => { button.textContent = 'Adresi kopyala'; }, 1600);
  });

  renderAll();
  update();
})();
