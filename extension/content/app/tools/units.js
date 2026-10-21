/* (2.99.79) The unit converter, the Mac's (UnitConverter.swift): a value in one unit, shown in another and, under it, in
 * every unit of its kind — lengths, weights, temperatures, volumes, areas, speeds, times, energy, pressure, data and
 * angles. Each unit is its factor to the kind's base unit (temperatures their own sums), the same factors Foundation's
 * units use on the Mac, so the two give the same answer. A press on a row makes that the unit wanted. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const T = BCV.tools;

  const u = (name, symbol, factor) => ({ name, symbol, factor });
  // [name, units, the pair it starts on]
  const KINDS = [
    ['Length', [u('Millimetres', 'mm', 1e-3), u('Centimetres', 'cm', 1e-2), u('Metres', 'm', 1), u('Kilometres', 'km', 1e3), u('Inches', 'in', 0.0254), u('Feet', 'ft', 0.3048), u('Yards', 'yd', 0.9144), u('Miles', 'mi', 1609.344), u('Nautical miles', 'nmi', 1852), u('Micrometres', 'µm', 1e-6), u('Nanometres', 'nm', 1e-9), u('Light years', 'ly', 9.4607304725808e15)], 2, 5],
    ['Mass', [u('Milligrams', 'mg', 1e-6), u('Grams', 'g', 1e-3), u('Kilograms', 'kg', 1), u('Metric tons', 't', 1e3), u('Ounces', 'oz', 0.028349523125), u('Pounds', 'lb', 0.45359237), u('Stones', 'st', 6.35029318), u('Short tons', 'ton', 907.18474)], 2, 5],
    ['Temperature', [u('Celsius', '°C', null), u('Fahrenheit', '°F', null), u('Kelvin', 'K', null)], 0, 1],
    ['Volume', [u('Millilitres', 'mL', 1e-3), u('Litres', 'L', 1), u('Cubic metres', 'm³', 1e3), u('Cubic centimetres', 'cm³', 1e-3), u('Teaspoons', 'tsp', 0.00492892159375), u('Tablespoons', 'tbsp', 0.01478676478125), u('Fluid ounces', 'fl oz', 0.0295735295625), u('Cups', 'cup', 0.24), u('Pints', 'pt', 0.473176473), u('Quarts', 'qt', 0.946352946), u('Gallons', 'gal', 3.785411784)], 1, 10],
    ['Area', [u('Square metres', 'm²', 1), u('Square kilometres', 'km²', 1e6), u('Square centimetres', 'cm²', 1e-4), u('Hectares', 'ha', 1e4), u('Square feet', 'ft²', 0.09290304), u('Square inches', 'in²', 0.00064516), u('Square miles', 'mi²', 2589988.110336), u('Acres', 'ac', 4046.8564224)], 0, 4],
    ['Speed', [u('Metres per second', 'm/s', 1), u('Kilometres per hour', 'km/h', 1 / 3.6), u('Miles per hour', 'mph', 0.44704), u('Knots', 'kn', 1852 / 3600)], 1, 2],
    ['Time', [u('Milliseconds', 'ms', 1e-3), u('Seconds', 's', 1), u('Minutes', 'min', 60), u('Hours', 'h', 3600)], 3, 2],
    ['Energy', [u('Joules', 'J', 1), u('Kilojoules', 'kJ', 1e3), u('Calories', 'cal', 4.184), u('Kilocalories', 'kcal', 4184), u('Kilowatt-hours', 'kWh', 3.6e6)], 3, 1],
    ['Pressure', [u('Pascals', 'Pa', 1), u('Kilopascals', 'kPa', 1e3), u('Bars', 'bar', 1e5), u('Millibars', 'mbar', 100), u('Atmospheres', 'atm', 101325), u('Millimetres of mercury', 'mmHg', 133.322387415), u('Pounds per square inch', 'psi', 6894.757293168)], 4, 1],
    ['Data', [u('Bits', 'b', 1), u('Bytes', 'B', 8), u('Kilobytes', 'kB', 8e3), u('Megabytes', 'MB', 8e6), u('Gigabytes', 'GB', 8e9), u('Terabytes', 'TB', 8e12), u('Kibibytes', 'KiB', 8192), u('Mebibytes', 'MiB', 8 * 1048576), u('Gibibytes', 'GiB', 8 * 1073741824)], 4, 3],
    ['Angle', [u('Degrees', '°', 1), u('Radians', 'rad', 180 / Math.PI), u('Gradians', 'grad', 0.9), u('Revolutions', 'rev', 360), u('Arc minutes', '′', 1 / 60), u('Arc seconds', '″', 1 / 3600)], 0, 1],
  ].map(([name, units, from, to]) => ({ name, units, from, to }));

  /** A temperature to Kelvin and back; anything else through its factor. */
  const TEMP = { '°C': [(v) => v + 273.15, (k) => k - 273.15], '°F': [(v) => ((v - 32) * 5) / 9 + 273.15, (k) => ((k - 273.15) * 9) / 5 + 32], K: [(v) => v, (k) => k] };
  function convert(v, from, to) {
    if (from.factor === null) return TEMP[to.symbol][1](TEMP[from.symbol][0](v));
    return (v * from.factor) / to.factor;
  }
  /** What was typed, as a number: commas between thousands and a typed minus sign allowed. */
  function number(s) {
    const t = String(s ?? '').replace(/,/g, '').replace(/−/g, '-').trim();
    if (!t || !/^[-+]?(\d+\.?\d*|\.\d+)(e[-+]?\d+)?$/i.test(t)) return null;
    const v = Number(t);
    return Number.isFinite(v) ? v : null;
  }
  /** Ten significant figures at most, grouped by thousands; very large and very small in powers of ten. */
  function text(v) {
    if (!Number.isFinite(v)) return '—';
    const a = Math.abs(v);
    const s = a !== 0 && (a >= 1e15 || a < 1e-6) ? v.toPrecision(6).replace(/\.?0+e/, 'e').replace('e+', 'e') : Number(v.toPrecision(10)).toLocaleString('en-US', { maximumFractionDigits: 10 });
    return s.replace(/-/g, '−');
  }
  /** A number with its unit: "212 °F", "3.28 ft" — the angle marks close up, as written: "90°", "30′". */
  const withUnit = (v, unit) => (['°', '′', '″'].includes(unit.symbol) ? `${text(v)}${unit.symbol}` : `${text(v)} ${unit.symbol}`);

  async function open(app, { from: fromEl = null } = {}) {
    const tool = T.toolOf('units');
    const kept = (await T.load('units:last', null)) || {};
    const st = { kind: KINDS.find((k) => k.name === kept.kind) || KINDS[0], value: typeof kept.value === 'string' ? kept.value : '1', from: 0, to: 0 };
    const seat = (k, from = null, to = null) => {
      st.kind = k;
      st.from = from !== null && k.units[from] ? from : k.from;
      st.to = to !== null && k.units[to] ? to : k.to;
    };
    seat(st.kind, Number.isInteger(kept.from) ? kept.from : null, Number.isInteger(kept.to) ? kept.to : null);
    const keep = () => T.save('units:last', { kind: st.kind.name, value: st.value, from: st.from, to: st.to });

    const body = U.el('bcv-units');
    const p = T.popup({ tool, title: 'Unit converter', sub: 'Lengths, weights, temperatures and more.', width: 640, body, from: fromEl });
    const kinds = U.el('bcv-units__kinds', null, { role: 'tablist', 'aria-label': 'Kind of unit' });
    const valueIn = T.input({ inputmode: 'decimal', 'aria-label': 'Value', autocomplete: 'off', class: 'bcv-input bcv-tool__input bcv-units__value' });
    valueIn.value = st.value;
    const fromBox = U.el('bcv-units__pick');
    const toBox = U.el('bcv-units__pick');
    const swap = h('button', { type: 'button', class: 'bcv-units__swap', title: 'Swap the units', 'aria-label': 'Swap the units', onclick: () => { [st.from, st.to] = [st.to, st.from]; keep(); paint(); } }, U.svg('M7 7h11l-3-3M17 17H6l3 3', { size: 16, stroke: 'currentColor', width: 2 }));
    const answer = U.el('bcv-units__answer');
    const echo = U.el('bcv-units__echo');
    const copy = h('button', { type: 'button', class: 'bcv-units__copy', title: 'Copy the answer', onclick: () => {
      const v = number(st.value);
      if (v === null) return;
      T.copyText(withUnit(convert(v, st.kind.units[st.from], st.kind.units[st.to]), st.kind.units[st.to]).replace(/−/g, '-'));
      U.toast('Copied.');
    } }, [U.svg(BCV.IC.copy, { size: 14, stroke: 'currentColor', width: 2 }), h('span', { text: 'Copy' })]);
    const all = U.el('bcv-units__all');
    valueIn.addEventListener('input', () => { st.value = valueIn.value; keep(); paintAnswer(); });
    body.append(...T.rise([
      kinds,
      T.card([
        U.el('bcv-units__row', [U.el('bcv-units__side', [T.label('From'), valueIn, fromBox]), swap, U.el('bcv-units__side', [T.label('To'), toBox])]),
        U.el('bcv-units__out', [U.el('bcv-units__outtext', [answer, echo]), copy]),
      ], 'bcv-units__main'),
      T.card([T.label('In every unit'), all]),
    ]));

    function paint() {
      kinds.replaceChildren(...KINDS.map((k) => h('button', { type: 'button', role: 'tab', class: `bcv-units__kind ${k === st.kind ? 'is-on' : ''}`, 'aria-selected': k === st.kind ? 'true' : 'false', dataset: { kind: k.name }, text: k.name, onclick: () => { if (k === st.kind) return; seat(k); keep(); paint(); } })));
      const opts = st.kind.units.map((x, i) => ({ value: String(i), text: `${x.name} (${x.symbol})` }));
      fromBox.replaceChildren(U.picker(opts, String(st.from), (v) => { st.from = Number(v); keep(); paint(); }, { label: 'From' }));
      toBox.replaceChildren(U.picker(opts, String(st.to), (v) => { st.to = Number(v); keep(); paint(); }, { label: 'To' }));
      paintAnswer();
    }
    function paintAnswer() {
      const v = number(st.value);
      const from = st.kind.units[st.from], to = st.kind.units[st.to];
      answer.textContent = v === null ? '—' : withUnit(convert(v, from, to), to);
      echo.textContent = v === null ? (String(st.value).trim() ? 'Type a number.' : 'Type a value to convert.') : `= ${withUnit(v, from)}`;
      copy.disabled = v === null;
      all.replaceChildren(...st.kind.units.map((x, i) => i === st.from ? null : h('button', { type: 'button', class: `bcv-units__line ${i === st.to ? 'is-on' : ''}`, dataset: { unit: x.symbol }, title: `Convert to ${x.name.toLowerCase()}`, onclick: () => { st.to = i; keep(); paint(); } }, [
        h('span', { class: 'bcv-units__lname', text: x.name }),
        h('span', { class: 'bcv-units__lval', text: v === null ? '—' : withUnit(convert(v, from, x), x) }),
      ])).filter(Boolean));
    }
    paint();
    setTimeout(() => { if (p.alive?.() !== false) { valueIn.focus(); valueIn.select(); } }, 60);
    return p;
  }

  BCV.toolsUnits = { open, convert, number, text, withUnit, KINDS };
})();
