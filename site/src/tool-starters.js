// Simpl Community tools: Simpl's own starters, so the gallery is never empty. Each is a format 1 tool like any a
// student sends in (docs/community-tools.md), made by "Simpl", with a stable id (s-…) that never changes: an app that
// added one keeps it by that id. None repeats a tool Simpl already has built in (Grade needed, the unit converter,
// Statistics, the matrix calculator, the gas laws, moles, kinematics, the circuit builder…).
//
// The gallery serves them checked (validateTool, strictly, as a tool sent in is); one that failed would be left out,
// and scripts/dev/community-tools-test.mjs fails on it first.
import { validateTool } from './tool-format.js';

const SIMPL = { format: 1, version: 1, maker: 'Simpl', created: '2026-11-01' };

export const STARTER_SOURCES = [
  {
    ...SIMPL, id: 's-ohms', name: 'Ohm’s Law', summary: 'Any two of volts, amps and ohms give the third.', group: 'science', symbol: 'bolt', color: '#ff9500',
    inputs: [
      { id: 'find', kind: 'choice', label: 'Find', options: [{ label: 'Current', value: 1 }, { label: 'Voltage', value: 2 }, { label: 'Resistance', value: 3 }], default: 1 },
      { id: 'v', kind: 'number', label: 'Voltage', unit: 'V', default: 12, min: 0, step: 0.5 },
      { id: 'i', kind: 'number', label: 'Current', unit: 'A', default: 2, min: 0, step: 0.1 },
      { id: 'r', kind: 'number', label: 'Resistance', unit: 'Ω', default: 6, min: 0, step: 1 },
    ],
    outputs: [
      { id: 'amps', label: 'Current', value: 'find == 1 ? v / r : i', unit: 'A', when: 'find == 1' },
      { id: 'volts', label: 'Voltage', value: 'find == 2 ? i * r : v', unit: 'V', when: 'find == 2' },
      { label: 'Resistance', value: 'find == 3 ? v / i : r', unit: 'Ω', when: 'find == 3' },
      { label: 'Power', value: 'volts * amps', unit: 'W', decimals: 3 },
    ],
    note: 'V = I × R, and the power P = V × I. Choose what to find; the other two boxes are what you know.',
  },
  {
    ...SIMPL, id: 's-quadratic', name: 'Quadratic Formula', summary: 'Solve ax² + bx + c = 0: its real roots and vertex.', group: 'math', symbol: 'function', color: '#5e5ce6',
    inputs: [
      { id: 'a', kind: 'number', label: 'a', default: 1 },
      { id: 'b', kind: 'number', label: 'b', default: -3 },
      { id: 'c', kind: 'number', label: 'c', default: 2 },
    ],
    outputs: [
      { label: 'x₁', value: 'a == 0 ? -c / b : (-b + sqrt(b^2 - 4*a*c)) / (2*a)' },
      { label: 'x₂', value: '(-b - sqrt(b^2 - 4*a*c)) / (2*a)', when: 'a != 0' },
      { id: 'disc', label: 'Discriminant', value: 'b^2 - 4*a*c' },
      { label: 'Real roots', value: 'a == 0 ? (b != 0 ? 1 : 0) : disc > 0 ? 2 : disc == 0 ? 1 : 0', decimals: 0 },
      { label: 'Vertex x', value: '-b / (2*a)', when: 'a != 0' },
      { label: 'Vertex y', value: 'c - b^2 / (4*a)', when: 'a != 0' },
    ],
    note: 'x = (−b ± √(b² − 4ac)) / 2a. With no real roots (a negative discriminant) the roots show “—”. With a = 0 it is the line bx + c = 0.',
  },
  {
    ...SIMPL, id: 's-percent', name: 'Percent Change', summary: 'How much something went up or down, in percent.', group: 'math', symbol: 'percent', color: '#ff375f',
    inputs: [
      { id: 'from', kind: 'number', label: 'Old value', default: 80 },
      { id: 'to', kind: 'number', label: 'New value', default: 100 },
    ],
    outputs: [
      { label: 'Change', value: '(to - from) / abs(from) * 100', unit: '%', decimals: 2 },
      { label: 'Difference', value: 'to - from' },
      { label: 'New as a share of old', value: 'to / from * 100', unit: '%', decimals: 2 },
    ],
    note: 'Change = (new − old) ÷ old × 100. Below zero is a drop. From 0 there is no percent change.',
  },
  {
    ...SIMPL, id: 's-tip', name: 'Tip & Split', summary: 'The tip, the total, and what each person pays.', group: 'everyday', symbol: 'money', color: '#34c759',
    inputs: [
      { id: 'bill', kind: 'number', label: 'Bill', default: 64, min: 0, step: 1 },
      { id: 'tip', kind: 'number', label: 'Tip', unit: '%', default: 18, min: 0, max: 100, step: 1 },
      { id: 'people', kind: 'number', label: 'People', default: 3, min: 1, max: 100, step: 1 },
      { id: 'roundup', kind: 'toggle', label: 'Round each share up', default: false },
    ],
    outputs: [
      { id: 'each', label: 'Each pays', value: 'roundup ? ceil(bill * (1 + tip / 100) / floor(people)) : bill * (1 + tip / 100) / floor(people)', decimals: 2 },
      { label: 'Tip', value: 'bill * tip / 100', decimals: 2 },
      { label: 'Total', value: 'bill * (1 + tip / 100)', decimals: 2 },
      { label: 'Paid in all', value: 'each * floor(people)', decimals: 2, when: 'roundup' },
      { label: 'Tip with rounding', value: '(each * floor(people) - bill) / bill * 100', unit: '%', decimals: 1, when: 'roundup' },
    ],
    note: 'Rounding up makes each share a whole amount; what it adds goes to the tip.',
  },
  {
    ...SIMPL, id: 's-compound', name: 'Compound Interest', summary: 'Savings growing with interest, and money added monthly.', group: 'money', symbol: 'chart', color: '#30b0c7',
    inputs: [
      { id: 'p', kind: 'number', label: 'Starting amount', default: 1000, min: 0, step: 100 },
      { id: 'rate', kind: 'number', label: 'Interest a year', unit: '%', default: 5, min: 0, max: 100, step: 0.25 },
      { id: 'years', kind: 'number', label: 'Years', default: 10, min: 0, max: 100, step: 1 },
      { id: 'n', kind: 'choice', label: 'Compounded', options: [{ label: 'Yearly', value: 1 }, { label: 'Quarterly', value: 4 }, { label: 'Monthly', value: 12 }, { label: 'Daily', value: 365 }], default: 12 },
      { id: 'add', kind: 'number', label: 'Added each month', default: 0, min: 0, step: 10 },
    ],
    outputs: [
      { id: 'bal', label: 'Balance', value: 'p * (1 + rate / 100 / n) ^ (n * years) + (rate == 0 ? add * 12 * years : add * ((1 + rate / 100 / n) ^ (n * years) - 1) / ((1 + rate / 100 / n) ^ (n / 12) - 1))', decimals: 2 },
      { id: 'put', label: 'Put in', value: 'p + add * 12 * years', decimals: 2 },
      { label: 'Interest earned', value: 'bal - put', decimals: 2 },
      { label: 'Doubling time', value: 'ln(2) / (n * ln(1 + rate / 100 / n))', unit: 'years', decimals: 1, when: 'rate > 0' },
    ],
    note: 'Money added goes in at the end of each month. No taxes or fees are counted.',
  },
  {
    ...SIMPL, id: 's-density', name: 'Density, Mass & Volume', summary: 'Any two of density, mass and volume give the third.', group: 'science', symbol: 'scale', color: '#af52de',
    inputs: [
      { id: 'find', kind: 'choice', label: 'Find', options: [{ label: 'Density', value: 1 }, { label: 'Mass', value: 2 }, { label: 'Volume', value: 3 }], default: 1 },
      { id: 'm', kind: 'number', label: 'Mass', unit: 'g', default: 27, min: 0 },
      { id: 'vol', kind: 'number', label: 'Volume', unit: 'cm³', default: 10, min: 0 },
      { id: 'rho', kind: 'number', label: 'Density', unit: 'g/cm³', default: 2.7, min: 0, step: 0.1 },
    ],
    outputs: [
      { id: 'd', label: 'Density', value: 'find == 1 ? m / vol : rho', unit: 'g/cm³', when: 'find == 1' },
      { label: 'Mass', value: 'find == 2 ? rho * vol : m', unit: 'g', when: 'find == 2' },
      { label: 'Volume', value: 'find == 3 ? m / rho : vol', unit: 'cm³', when: 'find == 3' },
      { label: 'Density in SI', value: 'd * 1000', unit: 'kg/m³', decimals: 1 },
    ],
    table: {
      title: 'Densities at room temperature',
      columns: ['Substance', 'g/cm³'],
      rows: [['Air', '0.0012'], ['Ethanol', '0.789'], ['Ice', '0.917'], ['Water', '0.998'], ['Seawater', '1.03'], ['Aluminium', '2.70'], ['Iron', '7.87'], ['Copper', '8.96'], ['Silver', '10.5'], ['Lead', '11.3'], ['Mercury', '13.5'], ['Gold', '19.3']],
    },
    note: 'ρ = m ÷ V. 1 cm³ is 1 mL. Choose what to find; the other two boxes are what you know.',
  },
  {
    ...SIMPL, id: 's-energy', name: 'Kinetic & Potential Energy', summary: 'Energy of motion and of height, and the speed at the ground.', group: 'science', symbol: 'gauge', color: '#ff3b30',
    inputs: [
      { id: 'm', kind: 'number', label: 'Mass', unit: 'kg', default: 2, min: 0, step: 0.5 },
      { id: 'v', kind: 'number', label: 'Speed', unit: 'm/s', default: 3, min: 0, step: 0.5 },
      { id: 'h', kind: 'number', label: 'Height', unit: 'm', default: 5, min: 0, step: 0.5 },
      { id: 'g', kind: 'choice', label: 'Gravity', options: [{ label: 'Earth', value: 9.81 }, { label: 'Moon', value: 1.62 }, { label: 'Mars', value: 3.71 }, { label: 'Jupiter', value: 24.79 }], default: 9.81 },
    ],
    outputs: [
      { id: 'ke', label: 'Kinetic energy', value: '0.5 * m * v^2', unit: 'J', decimals: 2 },
      { id: 'pe', label: 'Potential energy', value: 'm * g * h', unit: 'J', decimals: 2 },
      { label: 'Total energy', value: 'ke + pe', unit: 'J', decimals: 2 },
      { label: 'Speed at the ground', value: 'sqrt(v^2 + 2 * g * h)', unit: 'm/s', decimals: 2 },
      { label: 'Momentum', value: 'm * v', unit: 'kg·m/s', decimals: 2 },
    ],
    note: 'KE = ½mv², PE = mgh. The speed at the ground is with no air in the way: all the energy becomes motion.',
  },
  {
    ...SIMPL, id: 's-gpa', name: 'Grade Points', summary: 'Your GPA from up to five courses’ grades and credits.', group: 'study', symbol: 'book', color: '#0a84ff',
    inputs: [1, 2, 3, 4, 5].flatMap((k) => [
      { id: `g${k}`, kind: 'choice', label: `Course ${k}`, options: [['A', 4], ['A−', 3.7], ['B+', 3.3], ['B', 3], ['B−', 2.7], ['C+', 2.3], ['C', 2], ['C−', 1.7], ['D+', 1.3], ['D', 1], ['D−', 0.7], ['F', 0]].map(([label, value]) => ({ label, value })), default: [4, 3.3, 3.7, 3, 3][k - 1] },
      { id: `c${k}`, kind: 'number', label: `Course ${k} credits`, default: k <= 3 ? 3 : 0, min: 0, max: 20, step: 0.5 },
    ]),
    outputs: [
      { label: 'GPA', value: '(g1 * c1 + g2 * c2 + g3 * c3 + g4 * c4 + g5 * c5) / (c1 + c2 + c3 + c4 + c5)', decimals: 2 },
      { label: 'Credits', value: 'c1 + c2 + c3 + c4 + c5', decimals: 1 },
      { label: 'Quality points', value: 'g1 * c1 + g2 * c2 + g3 * c3 + g4 * c4 + g5 * c5', decimals: 2 },
    ],
    table: {
      title: 'The usual 4.0 scale',
      columns: ['Letter', 'Percent', 'Points'],
      rows: [['A', '93–100', '4.0'], ['A−', '90–92', '3.7'], ['B+', '87–89', '3.3'], ['B', '83–86', '3.0'], ['B−', '80–82', '2.7'], ['C+', '77–79', '2.3'], ['C', '73–76', '2.0'], ['C−', '70–72', '1.7'], ['D+', '67–69', '1.3'], ['D', '63–66', '1.0'], ['D−', '60–62', '0.7'], ['F', 'below 60', '0.0']],
    },
    note: 'A course at 0 credits is left out. Schools’ scales differ, so check yours.',
  },
];

/** The starters as the gallery serves them: each checked as a tool sent in would be (one that fails is left out). */
export const STARTERS = STARTER_SOURCES.map((t) => validateTool(t, { strict: true }).tool).filter(Boolean);
