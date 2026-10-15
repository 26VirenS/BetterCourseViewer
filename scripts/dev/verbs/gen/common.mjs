// Shared helpers for the per-language generators (gen/gen-*.mjs).
// They import the reference engine from ../check.mjs, fill in every tense of every
// irregular verb, and keep only the tenses that differ from the purely regular result
// (all tenses for the 15 "full" verbs).
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { conjugate, validate, runTests } from '../check.mjs';

// (VERBS_OUT=mac/Simpl/Resources/Verbs node scripts/dev/verbs/gen/gen-es.mjs writes where Simpl for Mac bundles the files)
export const OUT_DIR = process.env.VERBS_OUT || join(dirname(fileURLToPath(import.meta.url)), '..');

/** Copy every explicit tense of `base` onto `verb`, adding `prefix` to the first word of each form. */
export function derive(irregulars, data, verb, base, prefix, { noGe = false, over = {}, skip = [] } = {}) {
  const src = irregulars[base];
  if (!src) throw new Error(`derive: no base ${base}`);
  const out = {};
  for (const [tense, forms] of Object.entries(src)) {
    if (tense === 'stemChange') continue;
    // multi-word compound forms (ist gelungen) are recomputed by the engine (aux may differ);
    // one-word forms of a compound-by-default tense (käme → bekäme) are copied
    if (data.rules.compound && data.rules.compound[tense] && [].concat(forms).some(f => f.includes(' '))) continue;
    if (skip.includes(tense)) continue;
    const map = f => {
      if (f === '—') return f;
      const sp = f.indexOf(' ');
      let w = sp < 0 ? f : f.slice(0, sp);
      if (noGe && tense === 'partizip2' && w.startsWith('ge')) w = w.slice(2);
      return prefix + w + (sp < 0 ? '' : f.slice(sp));
    };
    out[tense] = Array.isArray(forms) ? forms.map(map) : map(forms);
  }
  Object.assign(out, over);
  if (src.stemChange !== undefined) out.stemChange = src.stemChange;
  irregulars[verb] = out;
}

/**
 * Expand irregulars: for each verb compute every tense with the engine (partial explicit
 * forms + rules), then keep the tenses whose forms differ from the regular computation.
 */
export function expand(data, fullVerbs) {
  const result = {};
  for (const verb of Object.keys(data.irregulars)) {
    const entry = { };
    const full = fullVerbs.includes(verb);
    for (const t of data.tenses) {
      const forms = conjugate(data, verb, t.id);
      let reg;
      try { reg = conjugate(data, verb, t.id, { ignoreIrregular: verb }); } catch { reg = null; }
      const same = reg != null && JSON.stringify(forms) === JSON.stringify(reg);
      if (full || !same) entry[t.id] = forms;
    }
    entry.stemChange = data.stemChanging[verb] ? data.stemChanging[verb].kind : null;
    result[verb] = entry;
  }
  data.irregulars = result;
}

export function dedupe(list) {
  const seen = new Set();
  return list.filter(v => (seen.has(v) ? false : (seen.add(v), true)));
}

export function finish(data, fullVerbs) {
  expand(data, fullVerbs);
  data.commonVerbs = dedupe(data.commonVerbs);
  const errors = validate(data, data.language);
  const failures = runTests(data, data.language);
  const path = join(OUT_DIR, `${data.language}.json`);
  const json = JSON.stringify(data);
  writeFileSync(path, json);
  console.log(`${data.language}.json written (${(Buffer.byteLength(json) / 1024).toFixed(1)} KB, ${Object.keys(data.irregulars).length} irregulars, ${data.commonVerbs.length} common)`);
  for (const e of errors) console.log('  schema: ' + e);
  for (const f of failures) console.log('  test: ' + f);
  if (errors.length || failures.length) process.exitCode = 1;
}

/** stem change kind helper for es/pt/it style boot verbs */
export const six = (...f) => f;
