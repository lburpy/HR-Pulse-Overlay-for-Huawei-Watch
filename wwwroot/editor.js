/* Overlay editor: builds the overlay URL and drives a live preview iframe. */
(() => {
  'use strict';

  const STORAGE_KEY = 'pulseoverlay.editor.v1';

  // ── Interface language ─────────────────────────────────────────────
  const UI_KEY = 'pulseoverlay.editor.ui';
  const I18N = {
    tr: {
      title: 'Overlay Düzenleyici', presets: 'Hazır şablonlar', reset: 'Varsayılanlara dön',
      previewData: 'Önizleme verisi', state: 'Durum', background: 'Arka plan', watch: 'Saat',
      autoWatch: 'Otomatik (aktif saat)', copy: 'Adresi kopyala', copied: 'Kopyalandı ✓', urlLabel: 'OBS adresi',
      size: 'Önerilen OBS boyutu: {0}', previewTitle: 'Overlay önizleme',
      howto: 'OBS → Kaynaklar → <b>+</b> → <b>Tarayıcı</b> → adresi yapıştır, genişlik ve yüksekliği önerilen boyuta ayarla.',
      stLive: 'Canlı', stLost: 'Sinyal yok', stReconnecting: 'Yeniden bağlanıyor', stOff: 'Bağlı değil',
      demoData: 'Demo veri', liveData: 'Canlı', bgChecker: 'Damalı', bgGame: 'Oyun', bgDark: 'Koyu', bgLight: 'Açık',
      thCard: 'Kart', thMinimal: 'Sade', thNeon: 'Neon', thRing: 'Gösterge', thEcg: 'EKG',
      prClassic: 'Klasik kart', prCorner: 'Yayın köşesi', prGaming: 'Oyun (sade)', prNeon: 'Neon', prGauge: 'Gösterge', prEcg: 'EKG monitörü',
      zone1: 'Isınma', zone2: 'Yağ yakımı', zone3: 'Aerobik', zone4: 'Anaerobik', zone5: 'Maksimum',
      secTheme: 'Tema', secLook: 'Görünüm', secColors: 'Renkler', secParts: 'Bileşenler',
      secEvents: 'Korku anı ve rekorlar', secZones: 'Nabız bölgeleri', secEffects: 'Efektler',
      font: 'Yazı tipi', scale: 'Boyut', bg: 'Arka plan opaklığı', radius: 'Köşe yuvarlaklığı',
      align: 'Hizalama', left: 'Sol', center: 'Orta', right: 'Sağ',
      colorMode: 'Renk modu', byZone: 'Nabız bölgesine göre', fixed: 'Sabit renk',
      accent: 'Vurgu rengi', accentHint: 'Bölge modunda sadece bağlantı yokken kullanılır.', text: 'Yazı rengi',
      beat: 'Kalp atışı animasyonu', tween: 'Yumuşak sayı geçişi', graph: 'Canlı nabız grafiği',
      window: 'Grafik süresi', s30: '30 saniye', m1: '1 dakika', m2: '2 dakika', m5: '5 dakika',
      stats: 'Min / Ort / Maks', zonebar: 'Bölge çubuğu', zname: 'Bölge adı', name: 'Saat adı', dot: 'Bağlantı noktası',
      label: 'Birim yazısı', lang: 'Overlay dili',
      scare: 'Korku anı efekti (😱 Korktu!)', scarecount: 'Korku sayacını göster', record: 'Rekor kutlaması',
      recOff: 'Kapalı', recSession: 'Yayın', recAll: 'Tüm zamanlar', recBoth: 'İkisi',
      testScare: '😱 Korkuyu dene', testRecord: '🏆 Rekoru dene',
      eventsInfo: 'Korku anı: nabız ~10 saniyede 20+ artarsa. Yayın rekoru ilk 2 dakikadan sonra ve ortalamanın 15 üstündeyse kutlanır. Tüm zamanların rekoru bilgisayarında saklanır (records.json); sıfırlamak için uygulamadaki “Rekoru sıfırla”.',
      maxhr: 'Maksimum nabız', age: 'Yaşın', fromAge: 'Yaştan hesapla', maxhrHint: 'Bilmiyorsan yaklaşık değer: 220 − yaşın.',
      alert: 'Uyarı eşiği (BPM)', alertHint: '0 = kapalı. Nabız bu değeri geçince efekt devreye girer.',
      alertfx: 'Uyarı efekti', flash: 'Parlama', shake: 'Titreme', both: 'İkisi', hideoff: 'Sinyal yokken overlay’i gizle',
    },
    en: {
      title: 'Overlay Editor', presets: 'Presets', reset: 'Reset to defaults',
      previewData: 'Preview data', state: 'State', background: 'Background', watch: 'Watch',
      autoWatch: 'Automatic (active watch)', copy: 'Copy URL', copied: 'Copied ✓', urlLabel: 'OBS URL',
      size: 'Suggested OBS size: {0}', previewTitle: 'Overlay preview',
      howto: 'OBS → Sources → <b>+</b> → <b>Browser</b> → paste the URL, set width and height to the suggested size.',
      stLive: 'Live', stLost: 'No signal', stReconnecting: 'Reconnecting', stOff: 'Not connected',
      demoData: 'Demo data', liveData: 'Live', bgChecker: 'Checker', bgGame: 'Game', bgDark: 'Dark', bgLight: 'Light',
      thCard: 'Card', thMinimal: 'Minimal', thNeon: 'Neon', thRing: 'Gauge', thEcg: 'ECG',
      prClassic: 'Classic card', prCorner: 'Stream corner', prGaming: 'Gaming (minimal)', prNeon: 'Neon', prGauge: 'Gauge', prEcg: 'ECG monitor',
      zone1: 'Warm-up', zone2: 'Fat burn', zone3: 'Aerobic', zone4: 'Anaerobic', zone5: 'Maximum',
      secTheme: 'Theme', secLook: 'Appearance', secColors: 'Colors', secParts: 'Components',
      secEvents: 'Scare moments & records', secZones: 'Heart rate zones', secEffects: 'Effects',
      font: 'Font', scale: 'Size', bg: 'Background opacity', radius: 'Corner radius',
      align: 'Alignment', left: 'Left', center: 'Center', right: 'Right',
      colorMode: 'Color mode', byZone: 'By heart rate zone', fixed: 'Fixed color',
      accent: 'Accent color', accentHint: 'In zone mode it is only used while disconnected.', text: 'Text color',
      beat: 'Heartbeat animation', tween: 'Smooth number changes', graph: 'Live heart rate graph',
      window: 'Graph length', s30: '30 seconds', m1: '1 minute', m2: '2 minutes', m5: '5 minutes',
      stats: 'Min / Avg / Max', zonebar: 'Zone bar', zname: 'Zone name', name: 'Watch name', dot: 'Connection dot',
      label: 'Unit label', lang: 'Overlay language',
      scare: 'Scare moment effect (😱 Scared!)', scarecount: 'Show scare counter', record: 'Record celebration',
      recOff: 'Off', recSession: 'Stream', recAll: 'All-time', recBoth: 'Both',
      testScare: '😱 Try scare', testRecord: '🏆 Try record',
      eventsInfo: 'Scare moment: heart rate rises 20+ within ~10 seconds. A stream record is celebrated after the first 2 minutes and when it is 15 above the average. The all-time record is stored on your computer (records.json); reset it with “Reset record” in the app.',
      maxhr: 'Maximum heart rate', age: 'Your age', fromAge: 'From age', maxhrHint: 'Not sure? Roughly 220 − your age.',
      alert: 'Alert threshold (BPM)', alertHint: '0 = off. The effect kicks in when heart rate goes above this.',
      alertfx: 'Alert effect', flash: 'Flash', shake: 'Shake', both: 'Both', hideoff: 'Hide the overlay while there is no signal',
    },
  };

  function detectUi() {
    const fromUrl = new URLSearchParams(location.search).get('ui');
    if (fromUrl === 'tr' || fromUrl === 'en') return fromUrl;
    try {
      const saved = localStorage.getItem(UI_KEY);
      if (saved === 'tr' || saved === 'en') return saved;
    } catch { /* storage unavailable */ }
    return (navigator.language || '').toLowerCase().startsWith('tr') ? 'tr' : 'en';
  }
  const uiLang = detectUi();
  try { localStorage.setItem(UI_KEY, uiLang); } catch { /* ignore */ }
  const t = key => I18N[uiLang][key] ?? I18N.tr[key] ?? key;

  const THEMES = [
    { id: 'card', name: t('thCard'), ico: '▭' },
    { id: 'minimal', name: t('thMinimal'), ico: '♥' },
    { id: 'neon', name: t('thNeon'), ico: '✦' },
    { id: 'ring', name: t('thRing'), ico: '◔' },
    { id: 'ecg', name: t('thEcg'), ico: '〰' },
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
    { name: t('prClassic'), v: { theme: 'card' } },
    { name: t('prCorner'), v: { theme: 'card', stats: true, zonebar: true, name: true } },
    { name: t('prGaming'), v: { theme: 'minimal', scale: 1.2 } },
    { name: t('prNeon'), v: { theme: 'neon', color: 'accent', accent: '#00e5ff', bg: 80 } },
    { name: t('prGauge'), v: { theme: 'ring', stats: true } },
    { name: t('prEcg'), v: { theme: 'ecg', color: 'accent', accent: '#39ff88', font: 'consolas', bg: 85, radius: 10 } },
  ];

  const ZONE_INFO = [
    [t('zone1'), 0.5, 0.6, '#38bdf8'],
    [t('zone2'), 0.6, 0.7, '#22c55e'],
    [t('zone3'), 0.7, 0.8, '#facc15'],
    [t('zone4'), 0.8, 0.9, '#fb923c'],
    [t('zone5'), 0.9, 1.0, '#ef4444'],
  ];

  const SECTIONS = [
    { title: t('secTheme'), items: [{ key: 'theme', type: 'themes' }] },
    { title: t('secLook'), items: [
      { key: 'font', type: 'select', label: t('font'), options: [
        ['bahnschrift', 'Bahnschrift'], ['segoe', 'Segoe UI'], ['consolas', 'Mono (Cascadia)'],
        ['impact', 'Impact'], ['arialblack', 'Arial Black'], ['georgia', 'Georgia']] },
      { key: 'scale', type: 'range', label: t('scale'), min: 0.5, max: 3, step: 0.05, fmt: v => `${Math.round(v * 100)}%` },
      { key: 'bg', type: 'range', label: t('bg'), min: 0, max: 100, step: 1, fmt: v => `${v}%` },
      { key: 'radius', type: 'range', label: t('radius'), min: 0, max: 40, step: 1, fmt: v => `${v} px` },
      { key: 'align', type: 'segment', label: t('align'), options: [['left', t('left')], ['center', t('center')], ['right', t('right')]] },
    ] },
    { title: t('secColors'), items: [
      { key: 'color', type: 'segment', label: t('colorMode'), options: [['zone', t('byZone')], ['accent', t('fixed')]] },
      { key: 'accent', type: 'color', label: t('accent'), hint: s => (s.color === 'zone' ? t('accentHint') : '') },
      { key: 'text', type: 'color', label: t('text') },
    ] },
    { title: t('secParts'), items: [
      { key: 'beat', type: 'check', label: t('beat') },
      { key: 'tween', type: 'check', label: t('tween') },
      { key: 'graph', type: 'check', label: t('graph'), enabled: s => GRAPH_THEMES.includes(s.theme) },
      { key: 'window', type: 'select', label: t('window'), options: [['30', t('s30')], ['60', t('m1')], ['120', t('m2')], ['300', t('m5')]],
        enabled: s => GRAPH_THEMES.includes(s.theme) && s.graph },
      { key: 'stats', type: 'check', label: t('stats') },
      { key: 'zonebar', type: 'check', label: t('zonebar') },
      { key: 'zname', type: 'check', label: t('zname') },
      { key: 'name', type: 'check', label: t('name') },
      { key: 'dot', type: 'check', label: t('dot'), enabled: s => s.theme !== 'minimal' },
      { key: 'label', type: 'text', label: t('label'), maxlength: 16 },
      { key: 'lang', type: 'segment', label: t('lang'), options: [['tr', 'Türkçe'], ['en', 'English']] },
    ] },
    { title: t('secEvents'), items: [
      { key: 'scare', type: 'check', label: t('scare') },
      { key: 'scarecount', type: 'check', label: t('scarecount'), enabled: s => s.scare },
      { key: 'record', type: 'segment', label: t('record'),
        options: [['off', t('recOff')], ['session', t('recSession')], ['alltime', t('recAll')], ['both', t('recBoth')]] },
      { key: '_test', type: 'test' },
      { key: '_info', type: 'info', text: t('eventsInfo') },
    ] },
    { title: t('secZones'), items: [{ key: 'maxhr', type: 'maxhr', label: t('maxhr') }] },
    { title: t('secEffects'), items: [
      { key: 'alert', type: 'number', label: t('alert'), min: 0, max: 250, hint: () => t('alertHint') },
      { key: 'alertfx', type: 'segment', label: t('alertfx'), options: [['flash', t('flash')], ['shake', t('shake')], ['both', t('both')]],
        enabled: s => s.alert > 0 },
      { key: 'hideoff', type: 'check', label: t('hideoff') },
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
      } else {
        // First visit: the overlay speaks the editor's language
        state.lang = uiLang;
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
      $('#size').textContent = t('size').replace('{0}', `${W} × ${H}`);
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
        const scare = el('button', { type: 'button', class: 'btn', onclick: () => fire('scare') }, t('testScare'));
        const record = el('button', { type: 'button', class: 'btn', onclick: () => fire('record') }, t('testRecord'));
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
        const age = el('input', { type: 'number', min: 10, max: 90, placeholder: t('age'), 'aria-label': t('age') });
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
        }, t('fromAge'));
        paintZones();
        return field(item, input, el('div', { class: 'agerow' }, age, calc),
          el('div', { class: 'hint' }, t('maxhrHint')), zones);
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

  $('#dataSeg').append(segment([['demo', t('demoData')], ['live', t('liveData')]],
    () => (preview.demo ? 'demo' : 'live'),
    v => { preview.demo = v === 'demo'; scheduleUpdate(); }));

  const demoState = $('#demoState');
  demoState.value = preview.demoState;
  demoState.addEventListener('change', () => { preview.demoState = demoState.value; scheduleUpdate(); });

  const previewBox = $('#preview');
  const paintBg = () => { previewBox.className = 'preview bg-' + preview.bg; };
  $('#bgSeg').append(segment([['checker', t('bgChecker')], ['game', t('bgGame')], ['dark', t('bgDark')], ['light', t('bgLight')]],
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
    button.textContent = t('copied');
    setTimeout(() => { button.textContent = t('copy'); }, 1600);
  });

  // ── Static page text + interface language switch ───────────────────
  document.documentElement.lang = uiLang;
  document.title = `${t('title')} · Pulse Overlay`;
  for (const node of document.querySelectorAll('[data-i18n]')) node.textContent = t(node.dataset.i18n);
  for (const node of document.querySelectorAll('[data-i18n-html]')) node.innerHTML = t(node.dataset.i18nHtml);
  for (const node of document.querySelectorAll('[data-i18n-title]')) node.title = t(node.dataset.i18nTitle);
  for (const node of document.querySelectorAll('[data-i18n-aria]')) node.setAttribute('aria-label', t(node.dataset.i18nAria));
  $('#uiLang').append(segment([['tr', 'TR'], ['en', 'EN']], () => uiLang, lang => {
    if (lang === uiLang) return;
    try { localStorage.setItem(UI_KEY, lang); } catch { /* ignore */ }
    const url = new URL(location.href);
    url.searchParams.set('ui', lang);
    location.href = url.toString(); // settings live in localStorage, so a reload keeps them
  }));

  renderAll();
  update();
})();
