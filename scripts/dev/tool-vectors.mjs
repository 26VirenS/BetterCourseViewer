// Writes docs/community-tools-vectors.json from the reference (site/src/tool-format.js): the answers every app's own
// copy of Simpl Community tools' language must give. Run it again whenever the reference changes:
//   node scripts/dev/tool-vectors.mjs          (writes the file)
//   node scripts/dev/tool-vectors.mjs --check  (exit 1 when the file is not what the reference gives)
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { CONSTANTS, evaluate, formatNumber, parse, runTool, validateTool } from '../../site/src/tool-format.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const file = join(root, 'docs', 'community-tools-vectors.json');

// numbers JSON cannot carry are written as strings
const enc = (v) => (Number.isNaN(v) ? 'NaN' : v === Infinity ? 'Infinity' : v === -Infinity ? '-Infinity' : Object.is(v, -0) ? '-0' : v);

const EVAL = [
  // [expression, names (values), decimals]
  ['1 + 2 * 3', {}, 4], ['(1 + 2) * 3', {}, 4], ['2^3^2', {}, 4], ['-2^2', {}, 4], ['(-2)^2', {}, 4], ['2^-1', {}, 4],
  ['- -3', {}, 4], ['+4', {}, 4], ['10 / 4', {}, 4], ['1 / 3', {}, 4], ['2 / 3', {}, 2], ['7 % 3', {}, 4], ['-7 % 3', {}, 4],
  ['7.5 % 2', {}, 4], ['5 % 0', {}, 4], ['1 / 0', {}, 4], ['0 / 0', {}, 4], ['1e3 + .5', {}, 4], ['2.5e-3 * 4', {}, 6],
  ['3 > 2', {}, 4], ['3 < 2', {}, 4], ['2 >= 2', {}, 4], ['2 <= 1', {}, 4], ['1 == 1', {}, 4], ['1 != 1', {}, 4],
  ['1 < 2 == 1', {}, 4], ['1 && 0', {}, 4], ['1 || 0', {}, 4], ['0 || 0', {}, 4], ['!0', {}, 4], ['!5', {}, 4], ['!!5', {}, 4],
  ['(0/0) == (0/0)', {}, 4], ['(0/0) || 1', {}, 4], ['(0/0) ? 1 : 2', {}, 4], ['1 ? 2 : 3', {}, 4], ['0 ? 2 : 3', {}, 4],
  ['0 ? 1 : 0 ? 2 : 3', {}, 4], ['1 ? 0 ? 5 : 6 : 7', {}, 4], ['1 + 1 > 1 ? 10 : 20', {}, 4], ['1 || 0 && 0', {}, 4],
  ['pi', {}, 6], ['e', {}, 6], ['true + true', {}, 4], ['false', {}, 4],
  ['abs(-3.5)', {}, 4], ['sqrt(16)', {}, 4], ['sqrt(2)', {}, 6], ['sqrt(-1)', {}, 4], ['cbrt(-27)', {}, 4], ['exp(1)', {}, 5],
  ['ln(e^2)', {}, 4], ['log(1000)', {}, 4], ['log2(1024)', {}, 4], ['ln(0)', {}, 4], ['log(-1)', {}, 4],
  ['sin(pi / 6)', {}, 4], ['cos(0)', {}, 4], ['tan(pi / 4)', {}, 4], ['asin(1)', {}, 4], ['acos(2)', {}, 4], ['atan(1) * 4', {}, 6],
  ['atan2(1, -1)', {}, 4], ['sin(rad(30))', {}, 4], ['deg(pi / 3)', {}, 4], ['rad(180)', {}, 5],
  ['floor(-2.5)', {}, 4], ['ceil(-2.5)', {}, 4], ['trunc(-2.7)', {}, 4], ['round(2.5)', {}, 4], ['round(-2.5)', {}, 4],
  ['round(2.4999)', {}, 4], ['round(1.23456, 3)', {}, 6], ['round(1.005, 2)', {}, 4], ['round(123.456, -1)', {}, 4],
  ['round(5.55, 1)', {}, 4], ['round(2, 0/0)', {}, 4], ['round(1e300, 2)', {}, 4],
  ['sign(-3)', {}, 4], ['sign(0)', {}, 4], ['sign(2)', {}, 4], ['sign(0/0)', {}, 4],
  ['min(3, 1, 2)', {}, 4], ['max(3, 1, 2)', {}, 4], ['min(4)', {}, 4], ['max(1, 0/0)', {}, 4], ['min(0/0, 1)', {}, 4],
  ['pow(2, 10)', {}, 4], ['pow(1, 0/0)', {}, 4], ['pow(0/0, 0)', {}, 4], ['pow(-1, 1/0)', {}, 4], ['pow(-8, 1/3)', {}, 4],
  ['pow(0, -1)', {}, 4], ['hypot(3, 4)', {}, 4], ['clamp(5, 0, 3)', {}, 4], ['clamp(-1, 0, 3)', {}, 4], ['clamp(0/0, 0, 1)', {}, 4],
  ['fact(0)', {}, 4], ['fact(5)', {}, 4], ['fact(20)', {}, 0], ['fact(5.5)', {}, 4], ['fact(-1)', {}, 4], ['fact(171)', {}, 4], ['fact(170)', {}, 4],
  ['v / r', { v: 12, r: 4 }, 4], ['v / r', { v: 12, r: 0 }, 4], ['p * (1 + r / 100) ^ n', { p: 1000, r: 5, n: 10 }, 2],
  ['(-b + sqrt(b^2 - 4*a*c)) / (2*a)', { a: 1, b: -3, c: 2 }, 4], ['(-b - sqrt(b^2 - 4*a*c)) / (2*a)', { a: 1, b: 2, c: 5 }, 4],
  ['mode == 1 ? v / r : v * r', { mode: 1, v: 9, r: 3 }, 4], ['mode == 1 ? v / r : v * r', { mode: 2, v: 9, r: 3 }, 4],
  ['x * 9 / 5 + 32', { x: 37 }, 1], ['(f - 32) * 5 / 9', { f: 98.6 }, 2], ['a_1 + b2', { a_1: 0.1, b2: 0.2 }, 4],
  ['0.1 + 0.2', {}, 10], ['1e12', {}, 4], ['999999999999', {}, 4], ['-1e15 / 3', {}, 4], ['1 / 3 * 1e-5', {}, 4],
  ['6.02214076e23', {}, 4], ['1.602e-19', {}, 4], ['12345.6789', {}, 2], ['-12345.6789', {}, 0], ['1234.5', {}, 1],
  ['99999.99999', {}, 4], ['0.00004', {}, 4], ['0.00005', {}, 4], ['0.3', {}, 0], ['0.5', {}, 0], ['-0.5', {}, 0],
  ['1e-7', {}, 10], ['-0.00001', {}, 4], ['9.9995e11', {}, 4], ['9.99999e14', {}, 4], ['123456789', {}, 0], ['-0', {}, 4],
];

const ERRORS = [
  // [expression, names it may read]
  ['', []], ['   ', []], ['1 +', []], ['(1', []], ['1)', []], ['1 2', []], ['* 3', []], ['1 ? 2', []], ['1 : 2', []],
  ['foo(1)', []], ['sqrt()', []], ['sqrt(1, 2)', []], ['atan2(1)', []], ['round(1, 2, 3)', []], ['clamp(1, 2)', []],
  ['x + 1', []], ['X + 1', ['x']], ['1 # 2', []], ['1 = 2', []], ['1 & 2', []], ['a.b', ['a']], ['2(3)', []], ['"1"', []],
  ['1..2', []], ['sqrt', []], ['pi()', []], ['min(' + Array(17).fill('1').join(',') + ')', []], ['1'.repeat(301), []],
];

const TOOLS = [
  {
    name: 'ohms law, every kind of input',
    tool: { format: 1, name: 'Ohm’s Law', maker: 'Simpl', summary: 'Volts, amps and ohms.', group: 'science', symbol: 'bolt', color: '#FF9500',
      inputs: [
        { id: 'mode', kind: 'choice', label: 'Solve for', options: [{ label: 'Current', value: 1 }, { label: 'Voltage', value: 2 }], default: 1 },
        { id: 'v', kind: 'number', label: 'Voltage', unit: 'V', default: 12, min: 0 },
        { id: 'i', kind: 'number', label: 'Current', unit: 'A', default: 2, min: 0, max: 100, step: 0.1 },
        { id: 'r', kind: 'number', label: 'Resistance', unit: 'Ω', default: 4, min: 0 },
        { id: 'mw', kind: 'toggle', label: 'Power in milliwatts' },
      ],
      outputs: [
        { id: 'amps', label: 'Current', value: 'v / r', unit: 'A', when: 'mode == 1' },
        { id: 'volts', label: 'Voltage', value: 'i * r', unit: 'V', when: 'mode == 2', decimals: 2 },
        { label: 'Power', value: '(mode == 1 ? v * amps : volts * i) * (mw ? 1000 : 1)', unit: 'W', decimals: 1 },
      ],
      note: 'V = I × R. Power is V × I.' },
    runs: [{}, { mode: 2, i: 1.5, r: 10 }, { mode: 1, v: 9, r: 0 }, { mode: 3, v: -5, i: 500, r: 2, mw: true }, { v: 'x', i: 0.25 }],
  },
  {
    name: 'a table and no note',
    tool: { format: 1, name: '  Grade   points  ', summary: 'Letter grades as points.', group: 'study', symbol: 'book', color: '#34c759',
      inputs: [{ id: 'pct', kind: 'number', label: 'Percent', unit: '%', default: 91.5, min: 0, max: 100 }],
      outputs: [{ label: 'Points', value: 'pct >= 93 ? 4 : pct >= 90 ? 3.7 : pct >= 87 ? 3.3 : pct >= 83 ? 3 : pct >= 80 ? 2.7 : 0', decimals: 1 }],
      table: { title: 'The scale', columns: ['Letter', 'From', 'Points'], rows: [['A', '93', '4.0'], ['A-', 90, '3.7']] } },
    runs: [{}, { pct: 120 }, { pct: 84 }],
  },
  { name: 'an extra key, read leniently', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A', colour: 'red' }], outputs: [{ label: 'Twice', value: 'a * 2' }], later: true }, runs: [{ a: 21 }] },
  { name: 'needs a newer Simpl', tool: { format: 2, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [], outputs: [] } },
  { name: 'no name', tool: { format: 1, name: ' ', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'a' }] } },
  { name: 'unknown group', tool: { format: 1, name: 'X', summary: 'Y', group: 'games', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'a' }] } },
  { name: 'unknown symbol', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'rocket', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'a' }] } },
  { name: 'bad colour', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: 'red', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'a' }] } },
  { name: 'no inputs', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [], outputs: [{ label: 'B', value: '1' }] } },
  { name: 'a reserved id', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'pi', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: '1' }] } },
  { name: 'a function name as an id', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'sqrt', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: '1' }] } },
  { name: 'an id used twice', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ id: 'a', label: 'B', value: '1' }] } },
  { name: 'an id with a capital', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'Ab', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: '1' }] } },
  { name: 'a later result used too soon', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'c + 1' }, { id: 'c', label: 'C', value: 'a' }] } },
  { name: 'a result reading itself', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ id: 'b', label: 'B', value: 'b + 1' }] } },
  { name: 'when may read its own result', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ id: 'b', label: 'B', value: 'a - 1', when: 'b > 0' }] }, runs: [{ a: 3 }, { a: 1 }] },
  { name: 'a bad formula', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'a +' }] } },
  { name: 'a choice with one option', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'choice', label: 'A', options: [{ label: 'One', value: 1 }] }], outputs: [{ label: 'B', value: 'a' }] } },
  { name: 'two options alike', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'choice', label: 'A', options: [{ label: 'One', value: 1 }, { label: 'Uno', value: 1 }] }], outputs: [{ label: 'B', value: 'a' }] } },
  { name: 'min above max', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A', min: 5, max: 1 }], outputs: [{ label: 'B', value: 'a' }] } },
  { name: 'decimals too many', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'a', decimals: 11 }] } },
  { name: 'a ragged table', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'number', label: 'A' }], outputs: [{ label: 'B', value: 'a' }], table: { columns: ['A', 'B'], rows: [['1']] } } },
  { name: 'an unknown input kind', tool: { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: [{ id: 'a', kind: 'text', label: 'A' }], outputs: [{ label: 'B', value: '1' }] } },
];

const out = {
  about: 'Simpl Community tools, format 1: the answers every app must give (docs/community-tools.md). Written by scripts/dev/tool-vectors.mjs from site/src/tool-format.js — do not edit by hand. Numbers JSON cannot hold are strings: "NaN", "Infinity", "-Infinity", "-0".',
  eval: EVAL.map(([expr, names, decimals]) => {
    const v = evaluate(parse(expr), { ...CONSTANTS, ...names });
    return { expr, names, decimals, text: formatNumber(v, decimals) };
  }),
  errors: ERRORS.map(([expr, names]) => {
    // (a formula alone: refused by the parser, or for a name or a function it may not use — validateTool's checks)
    const tool = { format: 1, name: 'X', summary: 'Y', group: 'math', symbol: 'sum', color: '#000000', inputs: (names.length ? names : ['zz']).map((id) => ({ id, kind: 'number', label: id })), outputs: [{ label: 'B', value: expr }] };
    const got = validateTool(tool);
    if (!got.error) throw new Error(`"${expr}" should be refused`);
    return { expr, names };
  }),
  tools: TOOLS.map(({ name, tool, runs }) => {
    const got = validateTool(tool);
    if (got.error) return { name, tool, valid: false, code: got.error.code };
    return {
      name, tool, valid: true, checked: got.tool,
      runs: (runs || []).map((given) => ({ given, results: runTool(got.tool, given).map((r) => ({ text: r.text, shown: r.shown })) })),
    };
  }),
};
for (const e of out.eval) e.names = Object.fromEntries(Object.entries(e.names).map(([k, v]) => [k, enc(v)]));

const json = `${JSON.stringify(out, null, 1)}\n`;
if (process.argv.includes('--check')) {
  let have = '';
  try { have = readFileSync(file, 'utf8'); } catch { /* none */ }
  if (have !== json) { console.error('docs/community-tools-vectors.json is not what site/src/tool-format.js gives: run node scripts/dev/tool-vectors.mjs'); process.exit(1); }
  console.log(`✓ community tool vectors: ${out.eval.length} formulas, ${out.errors.length} refusals, ${out.tools.length} tools`);
} else {
  writeFileSync(file, json);
  console.log(`wrote ${file}: ${out.eval.length} formulas, ${out.errors.length} refusals, ${out.tools.length} tools`);
}
