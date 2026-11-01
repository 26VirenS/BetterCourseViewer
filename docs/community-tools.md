# Simpl Community tools (format 1)

Students make small tools — Ohm's law, a tip splitter, a quadratic solver, a grade-points table — and share them in
Simpl Community. Anyone can browse them and add one; the app downloads it and draws it among its own tools.

**A tool is data, never code.** It is a JSON document: inputs (numbers, choices, switches), results (formulas in the
small language below), an optional table and a note. Each app draws it with its own native controls and works out its
formulas with its own copy of the language; nothing in a tool can reach the network, the files, Canvas or another tool,
and a formula can't loop. So adding a tool from a stranger is safe in a way a script never would be.

| Where | What |
| --- | --- |
| `site/public/community/tool-format.js` | **the reference**: checking a tool, the language, the number format. One file: the website's pages load it as it is (`/community/tool-format.js`), and the site's Worker bundles it (`site/src/tool-format.js` only re-exports it) |
| `docs/community-tools-vectors.json` | the answers every copy must give (written by `scripts/dev/tool-vectors.mjs`) |
| `site/src/community.js` | the site's endpoints (below) |
| the apps | each has its own copy, checked against the vectors |

## The document

```jsonc
{
  "format": 1,                      // required; an app shows "Needs a newer Simpl" for a format it doesn't know
  "id": "k3m9x2pa",                 // given by the site (absent in a tool being sent in)
  "version": 1,                     // given by the site
  "name": "Ohm’s Law",              // 1–40 characters, plain text
  "maker": "Viren",                 // 0–30
  "summary": "Volts, amps and ohms.", // 1–60: the line under the name on a tile
  "group": "science",               // study | math | science | money | everyday
  "symbol": "bolt",                 // one of the symbols below
  "color": "#ff9500",               // #rrggbb (written lower-case once checked)
  "inputs": [ … ],                  // 1–16
  "outputs": [ … ],                 // 1–12, called "results" in the apps
  "table": { … },                   // optional
  "note": "V = I × R.",             // optional, 0–400, shown at the foot
  "created": "2026-11-01"           // given by the site
}
```

All text is plain text: never HTML or Markdown, shown as it is. Text is trimmed, runs of white space become one space,
and it is cut at its limit, counted in UTF-16 code units (as JavaScript counts a string's length) — never between the
two halves of a surrogate pair: a character that would straddle the limit is left out (39 letters and an emoji, cut at
40, are the 39 letters). A lone half of a pair (only JSON's `\u` escapes can carry one) becomes U+FFFD, so what the site
serves is always well-formed. White space is exactly what JavaScript's `\s` matches — tab, line feed, vertical tab, form
feed, carriage return, the Unicode space separators (U+0020, U+00A0, U+1680, U+2000–U+200A, U+202F, U+205F, U+3000),
U+2028, U+2029 and U+FEFF — and the control characters U+0000–U+001F and U+007F count as white space too:
`"Tip\u0007Split"` is `Tip Split`.

**Inputs** — each has an `id` and a `label` (1–32):

- `{"id","kind":"number","label","unit"?,"default"?,"min"?,"max"?,"step"?}` — `unit` 0–12; `default` 0 when absent
  (clamped into the range); `step` above 0, 1 when absent (the stepper's step). In a formula the value is clamped to
  `[min, max]`; a value that isn't a number is the default.
- `{"id","kind":"choice","label","options":[{"label","value"}],"default"?}` — 2–12 options, each a label (1–32) and a
  distinct **number**; `default` must be one of the values (else the first). A value not offered is the default.
- `{"id","kind":"toggle","label","default"?}` — `default` true/false (false when absent); 1 or 0 in a formula.

**Results** — `{"id"?, "label", "value", "unit"?, "decimals"?, "when"?}`: `value` is a formula over the inputs and the
results **before** this one that have an `id`; `decimals` 0–10 (4 when absent); `when`, a formula that may also read
this result's own value, hides the result when it is 0 (or not a number). Results are worked out in order; a hidden
result still has its value. The first result shown is drawn largest.

**Ids** match `^[a-z][a-z0-9_]{0,15}$`, are unique across inputs and results, and are none of the reserved names:
`pi e true false` and every function name.

**Table** — `{"title"?, "columns":[…], "rows":[[…]]}`: a title (0–40), 1–6 column names (1–24), 1–100 rows, each
exactly one cell per column (0–48; a number is written as text, as JavaScript writes it: `90`, not `90.0`). Shown under
the results, as it is.

**Symbols** — `bolt percent function sum divide ruler scale thermometer clock calendar money chart atom flask leaf heart
book pencil globe car drop flame gauge star lightbulb music dice triangle`. Each app maps them to its own icons (the Mac:
SF Symbols `bolt.fill percent function sum divide ruler scalemass thermometer.medium clock calendar dollarsign.circle
chart.bar atom flask leaf heart book pencil globe car drop flame gauge.medium star lightbulb music.note dice triangle`).

**Size** — the tool as JSON text is at most 32,768 characters (UTF-16 code units, as JavaScript counts them). The site
checks it on a tool sent in and never serves a bigger one.

Checking: the site refuses any key format 1 doesn't have (`strict`); an app reads leniently and passes over unknown
keys. A tool that fails a check is refused whole, with a code (`SC-T-…`: see `validateTool`) and a plain message.

## The language

Numbers are IEEE doubles. Literals: `12`, `0.5`, `.5`, `6.02e23`, `1e-3` (no `1.`). Names: the ids above, `pi`, `e`,
`true` (1), `false` (0) — lower-case only. Spaces and tabs may go anywhere between tokens. A formula is 1–300 characters.

From lowest to highest precedence:

| | |
| --- | --- |
| `c ? a : b` | right-associative; only the branch taken is worked out |
| `||` · `&&` | 1 or 0 |
| `==` `!=` | 1 or 0 (`NaN == NaN` is 0) |
| `<` `<=` `>` `>=` | 1 or 0 |
| `+` `-` | |
| `*` `/` `%` | `%` keeps the sign of the dividend (C's `fmod`) |
| `-` `+` `!` (prefix) | `!x` is 1 when x is 0 or NaN |
| `^` | right-associative; its right side may carry a prefix: `2^-1`; `-2^2` is −4 |

```
cond    := or ('?' cond ':' cond)?
or      := and ('||' and)*          and := eq ('&&' eq)*
eq      := rel (('=='|'!=') rel)*   rel := add (('<'|'<='|'>'|'>=') add)*
add     := mul (('+'|'-') mul)*     mul := unary (('*'|'/'|'%') unary)*
unary   := ('-'|'+'|'!') unary | pow
pow     := primary ('^' unary)?
primary := number | name | name '(' cond (',' cond)* ')' | '(' cond ')'
```

True is anything but 0 and NaN. Division by zero and the like follow IEEE (∞, NaN), and show as "—".

**Functions** (radians throughout): `abs sqrt cbrt exp ln log`(base 10)` log2 sin cos tan asin acos atan floor ceil
trunc` (one value each); `atan2(y, x) pow(x, y) hypot(x, y)`; `round(x)` / `round(x, n)`; `sign(x)`; `min(…)` /
`max(…)` (1–16 values); `rad(degrees) deg(radians) clamp(x, lo, hi) fact(n)`. These and nothing else: a copy looks a
name up among the language's own functions and the tool's own names only, never among what its platform's objects
happen to have (`constructor(1)` is refused, as is any other name that is none of these).

Where platforms differ, the reference decides — every copy must do exactly this:

- `x ^ y` and `pow`: NaN when y is NaN; 1 when y is 0 (even for NaN x); NaN when x is NaN; NaN when |x| is 1 and y is
  infinite; otherwise the platform's pow.
- `round(x, n)`: n is truncated and clamped to 0–10 (NaN n gives NaN); then `sign(x) · floor(|x|·10ⁿ + 0.5) / 10ⁿ`,
  with 10ⁿ from an exact table (1, 10, … 1e10), and x itself when |x|·10ⁿ ≥ 2⁵² or x is not finite.
- `sign`: 1, −1, or x itself for 0, −0 and NaN (never an exception).
- `min`, `max`, `clamp`: NaN when any value is NaN; `clamp(x, lo, hi)` is `min(max(x, lo), hi)`.
- `rad(d)` is `(d · π) / 180`; `deg(r)` is `(r · 180) / π`, in that order.
- `fact(n)`: NaN unless n is a whole number from 0 to 170; otherwise the product 2·3·…·n, multiplied in that order.

## Showing a number

1. Not finite → `—`.
2. Let d be the result's decimals, r = round(v, d) as above.
3. From 1e12 up (|v|), or when r is 0 and |v| < 1e-4 but v isn't 0: `m × 10ⁿ` — n = ⌊log₁₀|v|⌋, m = v / 10ⁿ moved
   into [1, 10) if it fell outside, m rounded to 3 decimals (and moved again if that made it 10), trailing zeros
   dropped; the exponent in superscript digits with `⁻` for minus. `6.022 × 10²³`, `1.5 × 10⁻⁷`.
4. Otherwise r with d decimals, trailing zeros (and a trailing point) dropped, `-` in front when below 0, `0` never
   negative, and the whole part grouped by threes with `,` once it has five digits or more: `1234.5`, `12,345.68`.

## The site

| | |
| --- | --- |
| `GET /api/community/tools` | `{ ok, tools: [tool…] }`: every approved tool and Simpl's own starters, newest first |
| `GET /api/community/tools/<id>` | `{ ok, tool }` |
| `POST /api/community/tools` | a tool sent in: JSON `{ tool, website }` (website: the honeypot, left empty); checked strictly; kept as pending; the owner told |
| the review (`/community/review`) | pending tools beside pending themes, with a live preview; approve, turn down, take down |

## The apps

The Tools page has a **Community** section: the tools added from Community as tiles, and a tile to browse for more. The
browser lists the gallery (search, the five groups), each with a live preview drawn as it will be; **Add** downloads the
tool, checks it again on this device, and keeps it (a tool that fails here is not added). An added tool opens in a
popup like Simpl's own tools, drawn natively, remembers the values last put in, and can be removed from its tile's
menu. It keeps working with no connection.
