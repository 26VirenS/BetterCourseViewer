/* The starter widgets the importer offers (content/app/tools/widgets.js): four small widgets written
 * against the widget spec (docs/WIDGETS.md), each one file — a header, a style, some markup and a
 * script that talks to window.simpl. They are examples to begin from as much as tools: add one,
 * save its file from the popup, change it, add it again. */
(function () {
  const countdown = `<!doctype html>
<meta name="simpl-widget" content='{"name":"Countdown","note":"Days and hours to a date you pick.","icon":"clock","size":"S","color":"#ff375f","version":"1"}'>
<style>
  .row { display: flex; gap: 8px; align-items: center; }
  input { flex: 1; min-width: 0; height: 36px; padding: 0 10px; border-radius: 10px; border: 1px solid var(--bcv-edge); background: var(--bcv-card); }
  input:focus { outline: none; border-color: var(--bcv-blue); }
  .big { margin: 18px 0 4px; font: 700 44px/1 var(--bcv-display, inherit); letter-spacing: -.03em; color: #ff375f; }
  .sub { color: var(--bcv-ink3); font-size: 13px; }
  .hint { margin-top: 14px; color: var(--bcv-ink3); font-size: 12px; }
</style>
<div class="row"><input id="label" placeholder="What is it?" maxlength="40"><input id="date" type="date" aria-label="The date"></div>
<div class="big" id="big">—</div>
<div class="sub" id="sub">Pick a date above.</div>
<div class="hint">Kept on this device. Counts down to midnight of that day.</div>
<script>
  var labelEl = document.getElementById('label'), dateEl = document.getElementById('date'), big = document.getElementById('big'), sub = document.getElementById('sub');
  function paint() {
    var d = dateEl.value ? new Date(dateEl.value + 'T00:00:00') : null;
    if (!d || isNaN(d)) { big.textContent = '—'; sub.textContent = 'Pick a date above.'; return; }
    var ms = d - Date.now();
    var past = ms < 0; ms = Math.abs(ms);
    var days = Math.floor(ms / 864e5), hours = Math.floor((ms % 864e5) / 36e5);
    big.textContent = days + (days === 1 ? ' day' : ' days');
    sub.textContent = (labelEl.value || 'That day') + (past ? ' was ' : ' is in ') + days + 'd ' + hours + 'h' + (past ? ' ago' : '') + ' · ' + d.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' });
  }
  function keep() { simpl.storage.set('countdown', { label: labelEl.value, date: dateEl.value }); paint(); }
  labelEl.addEventListener('input', keep);
  dateEl.addEventListener('input', keep);
  simpl.ready(function () {
    simpl.storage.get('countdown', null).then(function (v) { if (v) { labelEl.value = v.label || ''; dateEl.value = v.date || ''; } paint(); });
    setInterval(paint, 60000);
  });
</script>`;

  const notes = `<!doctype html>
<meta name="simpl-widget" content='{"name":"Notes pad","note":"A scratch pad that keeps itself.","icon":"pencil","size":"M","color":"#ff9f0a"}'>
<style>
  textarea { width: 100%; height: 300px; resize: none; padding: 14px; border-radius: 14px; border: 1px solid var(--bcv-edge); background: var(--bcv-card); line-height: 1.5; }
  textarea:focus { outline: none; border-color: var(--bcv-blue); }
  .foot { display: flex; justify-content: space-between; align-items: center; margin-top: 10px; color: var(--bcv-ink3); font-size: 12px; }
  button { height: 30px; padding: 0 12px; border-radius: 8px; border: 1px solid var(--bcv-edge); background: var(--bcv-card); font-weight: 600; font-size: 12.5px; }
  button:hover { background: var(--bcv-hover); }
</style>
<textarea id="pad" placeholder="Type anything. It stays here." spellcheck="true"></textarea>
<div class="foot"><span id="count">0 words</span><span><button id="copy" type="button">Copy</button> <button id="clear" type="button">Clear</button></span></div>
<script>
  var pad = document.getElementById('pad'), count = document.getElementById('count'), timer = 0;
  function words(s) { var m = s.trim().match(/\\S+/g); return m ? m.length : 0; }
  function paint() { var n = words(pad.value); count.textContent = n + (n === 1 ? ' word' : ' words') + ' · ' + pad.value.length + ' characters'; }
  pad.addEventListener('input', function () { paint(); clearTimeout(timer); timer = setTimeout(function () { simpl.storage.set('text', pad.value); }, 300); });
  document.getElementById('copy').addEventListener('click', function () { simpl.copy(pad.value); });
  document.getElementById('clear').addEventListener('click', function () { pad.value = ''; paint(); simpl.storage.set('text', ''); pad.focus(); });
  simpl.ready(function () { simpl.storage.get('text', '').then(function (v) { pad.value = v || ''; paint(); }); });
</script>`;

  const units = `<!doctype html>
<meta name="simpl-widget" content='{"name":"Unit converter","note":"Length, mass, temperature, volume and speed.","icon":"convert","size":"M","color":"#30b0c7"}'>
<style>
  .kinds { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 14px; }
  .kinds button { height: 30px; padding: 0 12px; border-radius: 15px; border: 1px solid var(--bcv-edge); background: var(--bcv-card); font-weight: 600; font-size: 12.5px; }
  .kinds button.on { background: var(--bcv-ink); color: var(--bcv-bg); border-color: var(--bcv-ink); }
  .row { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
  .box { padding: 12px; border-radius: 14px; border: 1px solid var(--bcv-edge); background: var(--bcv-card); }
  .box input, .box select { width: 100%; height: 36px; padding: 0 10px; border-radius: 10px; border: 1px solid var(--bcv-edge); background: var(--bcv-bg); box-sizing: border-box; }
  .box input { font: 700 22px/1 var(--bcv-display, inherit); height: 44px; margin-bottom: 8px; }
  .box input:focus, .box select:focus { outline: none; border-color: var(--bcv-blue); }
  .hint { margin-top: 12px; color: var(--bcv-ink3); font-size: 12px; }
</style>
<div class="kinds" id="kinds"></div>
<div class="row">
  <div class="box"><input id="a" type="number" step="any" value="1" aria-label="From"><select id="ua" aria-label="From unit"></select></div>
  <div class="box"><input id="b" type="number" step="any" aria-label="To"><select id="ub" aria-label="To unit"></select></div>
</div>
<div class="hint">Type on either side. The last pair is remembered.</div>
<script>
  var KINDS = {
    Length: { m: 1, km: 1000, cm: 0.01, mm: 0.001, mi: 1609.344, yd: 0.9144, ft: 0.3048, 'in': 0.0254 },
    Mass: { kg: 1, g: 0.001, mg: 0.000001, lb: 0.45359237, oz: 0.028349523 },
    Volume: { L: 1, mL: 0.001, 'gal (US)': 3.785411784, qt: 0.946352946, cup: 0.2365882365, 'fl oz': 0.0295735296, tsp: 0.00492892159, tbsp: 0.0147867648 },
    Speed: { 'm/s': 1, 'km/h': 0.277777778, mph: 0.44704, knot: 0.514444444 },
    Temperature: { '°C': 'c', '°F': 'f', 'K': 'k' }
  };
  var kind = 'Length', a = document.getElementById('a'), b = document.getElementById('b'), ua = document.getElementById('ua'), ub = document.getElementById('ub');
  function toC(v, u) { return u === 'c' ? v : u === 'f' ? (v - 32) * 5 / 9 : v - 273.15; }
  function fromC(c, u) { return u === 'c' ? c : u === 'f' ? c * 9 / 5 + 32 : c + 273.15; }
  function convert(v, from, to) { var t = KINDS[kind]; if (kind === 'Temperature') return fromC(toC(v, t[from]), t[to]); return v * t[from] / t[to]; }
  function fmt(n) { return isFinite(n) ? String(Number(n.toPrecision(8))) : ''; }
  function fill(sel, u) { sel.innerHTML = ''; Object.keys(KINDS[kind]).forEach(function (k) { var o = document.createElement('option'); o.value = k; o.textContent = k; sel.append(o); }); if (u && KINDS[kind][u] !== undefined) sel.value = u; }
  function go(fromA) { if (fromA) b.value = fmt(convert(Number(a.value), ua.value, ub.value)); else a.value = fmt(convert(Number(b.value), ub.value, ua.value)); simpl.storage.set('last', { kind: kind, ua: ua.value, ub: ub.value, a: a.value }); }
  function paintKinds() { var box = document.getElementById('kinds'); box.innerHTML = ''; Object.keys(KINDS).forEach(function (k) { var btn = document.createElement('button'); btn.type = 'button'; btn.textContent = k; btn.className = k === kind ? 'on' : ''; btn.addEventListener('click', function () { kind = k; paintKinds(); fill(ua); fill(ub); ub.selectedIndex = Math.min(1, ub.options.length - 1); go(true); }); box.append(btn); }); }
  a.addEventListener('input', function () { go(true); }); b.addEventListener('input', function () { go(false); });
  ua.addEventListener('change', function () { go(true); }); ub.addEventListener('change', function () { go(true); });
  simpl.ready(function () {
    simpl.storage.get('last', null).then(function (v) {
      if (v && KINDS[v.kind]) { kind = v.kind; paintKinds(); fill(ua, v.ua); fill(ub, v.ub); a.value = v.a || '1'; }
      else { paintKinds(); fill(ua); fill(ub); ub.selectedIndex = 1; }
      go(true);
    });
  });
</script>`;

  const words = `<!doctype html>
<meta name="simpl-widget" content='{"name":"Word counter","note":"Words, characters, sentences and reading time.","icon":"text","size":"S","color":"#34c759"}'>
<style>
  textarea { width: 100%; height: 120px; resize: vertical; padding: 12px; border-radius: 12px; border: 1px solid var(--bcv-edge); background: var(--bcv-card); line-height: 1.5; }
  textarea:focus { outline: none; border-color: var(--bcv-blue); }
  .grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; margin-top: 10px; }
  .stat { padding: 10px 12px; border-radius: 12px; background: var(--bcv-card); border: 1px solid var(--bcv-sep); }
  .n { font: 700 22px/1.1 var(--bcv-display, inherit); letter-spacing: -.02em; }
  .l { margin-top: 2px; color: var(--bcv-ink3); font-size: 11.5px; text-transform: uppercase; letter-spacing: .06em; }
</style>
<textarea id="t" placeholder="Paste or type the text here."></textarea>
<div class="grid">
  <div class="stat"><div class="n" id="w">0</div><div class="l">words</div></div>
  <div class="stat"><div class="n" id="c">0</div><div class="l">characters</div></div>
  <div class="stat"><div class="n" id="s">0</div><div class="l">sentences</div></div>
  <div class="stat"><div class="n" id="r">0 min</div><div class="l">to read</div></div>
</div>
<script>
  var t = document.getElementById('t');
  function paint() {
    var s = t.value, m = s.trim().match(/\\S+/g), n = m ? m.length : 0;
    document.getElementById('w').textContent = n;
    document.getElementById('c').textContent = s.length;
    document.getElementById('s').textContent = (s.match(/[.!?]+(\\s|$)/g) || []).length;
    document.getElementById('r').textContent = (n ? Math.max(1, Math.round(n / 220)) : 0) + ' min';
  }
  t.addEventListener('input', paint);
  simpl.ready(paint);
</script>`;

  self.BCV_WIDGET_STARTERS = [
    { name: 'Countdown', html: countdown },
    { name: 'Notes pad', html: notes },
    { name: 'Unit converter', html: units },
    { name: 'Word counter', html: words },
  ];
})();
