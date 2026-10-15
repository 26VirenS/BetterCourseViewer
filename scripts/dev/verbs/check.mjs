#!/usr/bin/env node
// check.mjs — reference rules engine + schema validation + test vectors for the
// Simpl "Verb Conjugations" data files (es/fr/de/it/pt .json in this folder).
// Node ≥ 18, no dependencies.  Run:  node check.mjs
//
// The engine below is the normative reading of the JSON schema (see README.md).
// Platform ports (Swift / C#) should reproduce exactly this behaviour.

import { readFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
export const LANGS = ['es', 'fr', 'de', 'it', 'pt'];
export const PLACEHOLDER = '—';

// ───────────────────────────── engine ─────────────────────────────

export function tenseInfo(data, id) {
  return data.tenses.find(t => t.id === id);
}

/** First class whose regex matches (and whose optional verb list / except list agrees). */
export function classify(data, verb) {
  for (const c of data.rules.classes) {
    if (!new RegExp(c.match).test(verb)) continue;
    if (c.verbs && !c.verbs.includes(verb)) continue;
    if (c.except && c.except.includes(verb)) continue;
    return c;
  }
  return null;
}

function splitSeparable(data, verb) {
  if (!data.separable || !data.separable.includes(verb)) return null;
  const prefixes = (data.rules.separablePrefixes || []).slice().sort((a, b) => b.length - a.length);
  for (const p of prefixes) {
    if (verb.startsWith(p) && verb.length - p.length >= 3) return { prefix: p, base: verb.slice(p.length) };
  }
  throw new Error(`separable verb "${verb}" has no known prefix`);
}

/**
 * Conjugate `verb` in `tense`. Returns an array of 6 strings (one per pronoun, PLACEHOLDER
 * for persons that do not exist, e.g. 1st-person imperative) or a single string for
 * tenses marked `single` (participles, gerund).
 * opts.ignoreIrregular = verb → compute the purely regular result for that verb
 * (used to decide which cells to tint as irregular).
 */
export function conjugate(data, verb, tense, opts = {}) {
  const info = tenseInfo(data, tense);
  if (!info) throw new Error(`unknown tense ${tense}`);
  const irr = data.irregulars[verb];
  if (irr && irr[tense] != null && opts.ignoreIrregular !== verb) {
    return Array.isArray(irr[tense]) ? irr[tense].slice() : irr[tense];
  }
  const R = data.rules;
  const sep = splitSeparable(data, verb);
  if (R.compound && R.compound[tense]) return compound(data, verb, tense, opts, sep);
  if (sep) {
    if (info.single) return sep.prefix + conjugate(data, sep.base, tense, opts);
    return conjugate(data, sep.base, tense, opts).map(f => (f === PLACEHOLDER ? f : f + ' ' + sep.prefix));
  }
  if (R.compose && R.compose[tense]) return compose(data, verb, tense, opts);
  return regular(data, verb, tense, opts);
}

function compound(data, verb, tense, opts, sep) {
  const c = data.rules.compound[tense];
  let aux = c.aux.split('|')[0];
  if (opts.ignoreIrregular === verb && (aux === verb || c.aux.split('|').includes(verb))) {
    throw new Error(`${verb} has no regular ${tense} (it is the auxiliary)`);
  }
  if (c.auxOf) {
    for (const [a, list] of Object.entries(c.auxOf)) {
      if (list.includes(verb) || (sep && list.includes(sep.base))) aux = a;
    }
  }
  const auxForms = conjugate(data, aux, c.auxTense, opts);
  const part = c.with === 'infinitive' ? verb : conjugate(data, verb, c.with, opts);
  const agree = (c.agreement && c.agreement[aux]) || ['', '', '', '', '', ''];
  return auxForms.map((a, i) => a + ' ' + part + agree[i]);
}

function compose(data, verb, tense, opts) {
  let spec = data.rules.compose[tense];
  const cls = classify(data, verb);
  if (!Array.isArray(spec)) {
    if (!cls) throw new Error(`cannot classify ${verb}`);
    spec = spec[cls.id] ?? spec['*'];
  }
  return spec.map((item, person) => {
    if (item.literal != null) return item.literal;
    if (item.form) {
      let f = conjugate(data, verb, item.form, opts)[item.person];
      if (item.stripIf && new RegExp(item.stripIf).test(f)) f = f.slice(0, -1);
      return f;
    }
    if (item.stem) {
      if (!cls) throw new Error(`cannot classify ${verb}`);
      return buildForm(data, verb, cls, tense, person, { kind: item.stem }, item.ending ?? '', opts);
    }
    throw new Error(`bad compose item in ${tense}`);
  });
}

function endingsFor(data, cls, tense) {
  const E = data.rules.endings;
  let c = cls;
  while (c) {
    if (E[c.id] && E[c.id][tense] != null) return E[c.id][tense];
    c = c.inherits ? data.rules.classes.find(x => x.id === c.inherits) : null;
  }
  return null;
}

function stemSpecFor(data, cls, tense, person) {
  let spec = data.rules.stems[tense] ?? { kind: 'infinitiveStem' };
  if (spec.byClass) spec = spec.byClass[cls.id] ?? spec.byClass['*'];
  if (Array.isArray(spec)) spec = spec.find(s => s.persons === 'all' || s.persons.includes(person));
  if (!spec) throw new Error(`no stem spec for ${tense} person ${person}`);
  return spec;
}

function regular(data, verb, tense, opts) {
  const info = tenseInfo(data, tense);
  const cls = classify(data, verb);
  if (!cls) throw new Error(`cannot classify "${verb}"`);
  const endings = endingsFor(data, cls, tense);
  if (endings == null) throw new Error(`no endings for class ${cls.id} tense ${tense}`);
  if (info.single) {
    return buildForm(data, verb, cls, tense, 0, stemSpecFor(data, cls, tense, 0), endings, opts);
  }
  return endings.map((e, i) => buildForm(data, verb, cls, tense, i, stemSpecFor(data, cls, tense, i), e, opts));
}

function baseStem(data, verb, cls, spec, opts) {
  switch (spec.kind) {
    case 'infinitiveStem': return verb.slice(0, verb.length - cls.strip);
    case 'infinitive': return verb;
    case 'infinitiveMinusR': return verb.replace(/r$/, '');
    case 'form': {
      const forms = conjugate(data, verb, spec.tense, opts);
      let f = Array.isArray(forms) ? forms[spec.person] : forms;
      if (spec.strip && f.endsWith(spec.strip)) f = f.slice(0, f.length - spec.strip.length);
      return f;
    }
    default: throw new Error(`unknown stem kind ${spec.kind}`);
  }
}

function applyStemChange(data, verb, stem, tense, person) {
  const sc = data.stemChanging && data.stemChanging[verb];
  if (!sc) return stem;
  const raised = data.rules.raisedStemPersons && data.rules.raisedStemPersons[tense];
  if (sc.raisedStem && raised && (raised === 'all' || raised.includes(person))) return sc.raisedStem;
  const kind = data.stemChangeKinds[sc.kind];
  if (!kind) throw new Error(`unknown stem change kind ${sc.kind}`);
  const persons = kind.persons[tense];
  if (persons && (persons === 'all' || persons.includes(person))) {
    const idx = stem.lastIndexOf(kind.from);
    if (idx >= 0) stem = stem.slice(0, idx) + kind.to + stem.slice(idx + kind.from.length);
  }
  return stem;
}

function applySpelling(data, verb, cls, stem, ending, tense, person) {
  // Conditions are tested against the stem/ending BEFORE any spelling rule ran,
  // so rules never cascade (conozc must not then trigger "consonant + c → z").
  const stem0 = stem, ending0 = ending;
  for (const rule of data.rules.spelling) {
    const persons = rule.tenses[tense];
    if (!persons) continue;
    if (persons !== 'all' && !persons.includes(person)) continue;
    if (rule.classes && !rule.classes.includes(cls.id)) continue;
    if (rule.verbs && !rule.verbs.includes(verb)) continue;
    if (rule.except && rule.except.includes(verb)) continue;
    if (rule.match && !new RegExp(rule.match).test(stem0)) continue;
    if (rule.verbMatch && !new RegExp(rule.verbMatch).test(verb)) continue;
    if (rule.endingMatch && !new RegExp(rule.endingMatch).test(ending0)) continue;
    if (rule.do.stem) stem = stem.replace(new RegExp(rule.do.stem.from), rule.do.stem.to);
    if (rule.do.ending) ending = ending.replace(new RegExp(rule.do.ending.from), rule.do.ending.to);
  }
  return { stem, ending };
}

function accentLastVowel(stem, map) {
  for (let i = stem.length - 1; i >= 0; i--) {
    const ch = stem[i];
    if (map[ch]) return stem.slice(0, i) + map[ch] + stem.slice(i + 1);
  }
  return stem;
}

function buildForm(data, verb, cls, tense, person, spec, ending, opts) {
  let stem = baseStem(data, verb, cls, spec, opts);
  if (spec.kind === 'infinitiveStem') stem = applyStemChange(data, verb, stem, tense, person);
  ({ stem, ending } = applySpelling(data, verb, cls, stem, ending, tense, person));
  if (spec.suffix != null) stem += typeof spec.suffix === 'string' ? spec.suffix : (spec.suffix[cls.id] ?? spec.suffix['*'] ?? '');
  if (ending.startsWith('´')) {
    stem = accentLastVowel(stem, data.rules.accentMap || {});
    ending = ending.slice(1);
  }
  return ending.includes('%') ? ending.replace('%', stem) : stem + ending;
}

/** Full table: { tenseId: forms } plus `irregular: { tenseId: [bool×6] }` for tinting. */
export function table(data, verb) {
  const out = { forms: {}, irregular: {} };
  for (const t of data.tenses) {
    const f = conjugate(data, verb, t.id);
    let r;
    try { r = conjugate(data, verb, t.id, { ignoreIrregular: verb }); } catch { r = null; }
    out.forms[t.id] = f;
    out.irregular[t.id] = Array.isArray(f) ? f.map((x, i) => r == null || x !== r[i]) : (r == null || f !== r);
  }
  return out;
}

export function loadLang(lang, dir = HERE) {
  return JSON.parse(readFileSync(join(dir, `${lang}.json`), 'utf8'));
}

// ───────────────────────────── validation ─────────────────────────────

const TOP15 = {
  es: ['ser', 'estar', 'ir', 'haber', 'tener', 'hacer', 'poder', 'decir', 'querer', 'saber', 'poner', 'venir', 'ver', 'dar', 'salir'],
  fr: ['être', 'avoir', 'aller', 'faire', 'pouvoir', 'vouloir', 'savoir', 'venir', 'voir', 'prendre', 'devoir', 'dire', 'mettre', 'partir', 'sortir'],
  de: ['sein', 'haben', 'werden', 'können', 'müssen', 'wollen', 'sollen', 'dürfen', 'mögen', 'wissen', 'gehen', 'kommen', 'sehen', 'geben', 'nehmen'],
  it: ['essere', 'avere', 'fare', 'andare', 'potere', 'volere', 'dovere', 'sapere', 'dire', 'venire', 'stare', 'dare', 'vedere', 'uscire', 'bere'],
  pt: ['ser', 'estar', 'ter', 'ir', 'fazer', 'poder', 'dizer', 'ver', 'vir', 'dar', 'saber', 'querer', 'pôr', 'trazer', 'haver'],
};
const TOP_KEYS = ['language', 'name', 'pronouns', 'imperativePronouns', 'tenses', 'groupNames', 'stemKinds', 'rules',
  'irregulars', 'stemChangeKinds', 'stemChanging', 'separable', 'commonVerbs'];
const GROUPS = ['indicativo', 'subjuntivo', 'imperativo', 'condicional', 'nonfinite'];

export function validate(data, lang) {
  const errors = [];
  const err = m => errors.push(`[${lang}] ${m}`);
  for (const k of TOP_KEYS) if (!(k in data)) err(`missing top-level key "${k}"`);
  if (data.language !== lang) err(`language field is ${data.language}`);
  if (!Array.isArray(data.pronouns) || data.pronouns.length !== 6) err('pronouns must have 6 entries');
  if (!Array.isArray(data.imperativePronouns) || data.imperativePronouns.length !== 6) err('imperativePronouns must have 6 entries');
  const tenseIds = new Set();
  for (const t of data.tenses) {
    if (!t.id || !t.name || !t.group) err(`bad tense entry ${JSON.stringify(t)}`);
    if (!GROUPS.includes(t.group)) err(`tense ${t.id} has unknown group ${t.group}`);
    if (!data.groupNames[t.group]) err(`groupNames lacks ${t.group}`);
    tenseIds.add(t.id);
  }
  const checkForms = (where, tense, forms) => {
    const info = tenseInfo(data, tense);
    if (!info) { err(`${where}: unknown tense "${tense}"`); return; }
    if (info.single) {
      if (typeof forms !== 'string' || !forms.trim()) err(`${where}.${tense}: single form must be a non-empty string`);
    } else {
      if (!Array.isArray(forms) || forms.length !== 6) { err(`${where}.${tense}: expected 6 forms`); return; }
      forms.forEach((f, i) => { if (typeof f !== 'string' || !f.trim()) err(`${where}.${tense}[${i}]: empty form`); });
    }
  };
  const irrNames = Object.keys(data.irregulars);
  if (irrNames.length < 60) err(`only ${irrNames.length} irregular verbs (need ≥ 60)`);
  for (const [verb, entry] of Object.entries(data.irregulars)) {
    for (const [k, v] of Object.entries(entry)) {
      if (k === 'stemChange') continue;
      checkForms(`irregulars.${verb}`, k, v);
    }
  }
  for (const v of TOP15[lang]) {
    const e = data.irregulars[v];
    if (!e) { err(`top-15 verb ${v} missing from irregulars`); continue; }
    for (const t of data.tenses) if (e[t.id] == null) err(`top-15 verb ${v} lacks explicit ${t.id}`);
  }
  for (const [v, sc] of Object.entries(data.stemChanging)) {
    if (!data.stemChangeKinds[sc.kind]) err(`stemChanging.${v}: unknown kind ${sc.kind}`);
  }
  if (!Array.isArray(data.commonVerbs) || data.commonVerbs.length < 300) err(`commonVerbs has ${data.commonVerbs?.length} entries (need ≥ 300)`);
  const seen = new Set();
  for (const v of data.commonVerbs || []) {
    if (seen.has(v)) err(`commonVerbs duplicate "${v}"`);
    seen.add(v);
  }
  // every common verb + every irregular + every separable must conjugate in every tense
  const all = new Set([...(data.commonVerbs || []), ...irrNames, ...(data.separable || []), ...Object.keys(data.stemChanging)]);
  for (const v of all) {
    for (const t of data.tenses) {
      try {
        const f = conjugate(data, v, t.id);
        checkForms(`engine(${v})`, t.id, f);
      } catch (e) {
        err(`engine(${v}.${t.id}) threw: ${e.message}`);
      }
      // the purely regular computation may legitimately fail (sein, tun, -oir verbs…) but must not hang
      try { conjugate(data, v, t.id, { ignoreIrregular: v }); } catch { /* fine */ }
    }
  }
  return errors;
}

// ───────────────────────────── test vectors ─────────────────────────────
// [verb, tense, expected] — expected is a full array, a {person: form} map, or a string (single tenses)

export const TESTS = {
  es: [
    // spec vectors
    ['hablar', 'presente', ['hablo', 'hablas', 'habla', 'hablamos', 'habláis', 'hablan']],
    ['ser', 'preterito', ['fui', 'fuiste', 'fue', 'fuimos', 'fuisteis', 'fueron']],
    ['tener', 'presente', ['tengo', 'tienes', 'tiene', 'tenemos', 'tenéis', 'tienen']],
    // more
    ['comer', 'presente', ['como', 'comes', 'come', 'comemos', 'coméis', 'comen']],
    ['vivir', 'preterito', ['viví', 'viviste', 'vivió', 'vivimos', 'vivisteis', 'vivieron']],
    ['hablar', 'imperfectoSubj', ['hablara', 'hablaras', 'hablara', 'habláramos', 'hablarais', 'hablaran']],
    ['tener', 'imperfectoSubj', ['tuviera', 'tuvieras', 'tuviera', 'tuviéramos', 'tuvierais', 'tuvieran']],
    ['buscar', 'preterito', { 0: 'busqué' }],
    ['buscar', 'presenteSubj', ['busque', 'busques', 'busque', 'busquemos', 'busquéis', 'busquen']],
    ['empezar', 'presente', ['empiezo', 'empiezas', 'empieza', 'empezamos', 'empezáis', 'empiezan']],
    ['empezar', 'presenteSubj', ['empiece', 'empieces', 'empiece', 'empecemos', 'empecéis', 'empiecen']],
    ['dormir', 'preterito', ['dormí', 'dormiste', 'durmió', 'dormimos', 'dormisteis', 'durmieron']],
    ['dormir', 'presenteSubj', ['duerma', 'duermas', 'duerma', 'durmamos', 'durmáis', 'duerman']],
    ['pedir', 'presente', ['pido', 'pides', 'pide', 'pedimos', 'pedís', 'piden']],
    ['seguir', 'presente', ['sigo', 'sigues', 'sigue', 'seguimos', 'seguís', 'siguen']],
    ['conocer', 'presente', { 0: 'conozco', 1: 'conoces' }],
    ['leer', 'preterito', ['leí', 'leíste', 'leyó', 'leímos', 'leísteis', 'leyeron']],
    ['leer', 'gerundio', 'leyendo'],
    ['construir', 'presente', ['construyo', 'construyes', 'construye', 'construimos', 'construís', 'construyen']],
    ['hacer', 'futuro', ['haré', 'harás', 'hará', 'haremos', 'haréis', 'harán']],
    ['ir', 'imperfecto', ['iba', 'ibas', 'iba', 'íbamos', 'ibais', 'iban']],
    ['decir', 'imperativo', ['—', 'di', 'diga', 'digamos', 'decid', 'digan']],
    ['hablar', 'imperativo', ['—', 'habla', 'hable', 'hablemos', 'hablad', 'hablen']],
    ['jugar', 'presenteSubj', ['juegue', 'juegues', 'juegue', 'juguemos', 'juguéis', 'jueguen']],
    ['estar', 'presente', ['estoy', 'estás', 'está', 'estamos', 'estáis', 'están']],
    ['volver', 'participio', 'vuelto'],
    ['coger', 'presente', { 0: 'cojo', 1: 'coges' }],
    ['llegar', 'preterito', { 0: 'llegué', 2: 'llegó' }],
    ['continuar', 'presente', { 0: 'continúo', 3: 'continuamos' }],
    ['enviar', 'presente', { 0: 'envío', 5: 'envían' }],
    ['poner', 'condicional', ['pondría', 'pondrías', 'pondría', 'pondríamos', 'pondríais', 'pondrían']],
  ],
  fr: [
    ['aller', 'present', ['vais', 'vas', 'va', 'allons', 'allez', 'vont']],
    ['manger', 'present', { 3: 'mangeons' }],
    // more
    ['parler', 'present', ['parle', 'parles', 'parle', 'parlons', 'parlez', 'parlent']],
    ['finir', 'present', ['finis', 'finis', 'finit', 'finissons', 'finissez', 'finissent']],
    ['vendre', 'present', ['vends', 'vends', 'vend', 'vendons', 'vendez', 'vendent']],
    ['manger', 'imparfait', ['mangeais', 'mangeais', 'mangeait', 'mangions', 'mangiez', 'mangeaient']],
    ['commencer', 'present', { 3: 'commençons' }],
    ['commencer', 'passeSimple', ['commençai', 'commenças', 'commença', 'commençâmes', 'commençâtes', 'commencèrent']],
    ['acheter', 'present', ['achète', 'achètes', 'achète', 'achetons', 'achetez', 'achètent']],
    ['acheter', 'futur', { 0: 'achèterai', 3: 'achèterons' }],
    ['appeler', 'present', ['appelle', 'appelles', 'appelle', 'appelons', 'appelez', 'appellent']],
    ['jeter', 'futur', { 0: 'jetterai' }],
    ['nettoyer', 'present', ['nettoie', 'nettoies', 'nettoie', 'nettoyons', 'nettoyez', 'nettoient']],
    ['préférer', 'present', ['préfère', 'préfères', 'préfère', 'préférons', 'préférez', 'préfèrent']],
    ['préférer', 'futur', { 0: 'préférerai' }],
    ['être', 'passeCompose', ['ai été', 'as été', 'a été', 'avons été', 'avez été', 'ont été']],
    ['aller', 'passeCompose', ['suis allé', 'es allé', 'est allé', 'sommes allés', 'êtes allés', 'sont allés']],
    ['prendre', 'subjonctifPresent', ['prenne', 'prennes', 'prenne', 'prenions', 'preniez', 'prennent']],
    ['boire', 'present', ['bois', 'bois', 'boit', 'buvons', 'buvez', 'boivent']],
    ['venir', 'passeSimple', ['vins', 'vins', 'vint', 'vînmes', 'vîntes', 'vinrent']],
    ['faire', 'futur', ['ferai', 'feras', 'fera', 'ferons', 'ferez', 'feront']],
    ['parler', 'imperatif', ['—', 'parle', '—', 'parlons', 'parlez', '—']],
    ['finir', 'imperatif', ['—', 'finis', '—', 'finissons', 'finissez', '—']],
    ['avoir', 'imperatif', ['—', 'aie', '—', 'ayons', 'ayez', '—']],
    ['vendre', 'participePasse', 'vendu'],
    ['finir', 'participePresent', 'finissant'],
    ['apprendre', 'passeCompose', { 0: 'ai appris' }],
    ['devenir', 'passeCompose', { 0: 'suis devenu', 5: 'sont devenus' }],
    ['pouvoir', 'conditionnel', ['pourrais', 'pourrais', 'pourrait', 'pourrions', 'pourriez', 'pourraient']],
    ['envoyer', 'futur', { 0: 'enverrai' }],
    ['ouvrir', 'imperatif', { 1: 'ouvre' }],
    ['savoir', 'subjonctifPresent', ['sache', 'saches', 'sache', 'sachions', 'sachiez', 'sachent']],
  ],
  de: [
    ['sein', 'praesens', ['bin', 'bist', 'ist', 'sind', 'seid', 'sind']],
    ['anfangen', 'praesens', ['fange an', 'fängst an', 'fängt an', 'fangen an', 'fangt an', 'fangen an']],
    // more
    ['lernen', 'praesens', ['lerne', 'lernst', 'lernt', 'lernen', 'lernt', 'lernen']],
    ['arbeiten', 'praesens', ['arbeite', 'arbeitest', 'arbeitet', 'arbeiten', 'arbeitet', 'arbeiten']],
    ['arbeiten', 'praeteritum', ['arbeitete', 'arbeitetest', 'arbeitete', 'arbeiteten', 'arbeitetet', 'arbeiteten']],
    ['arbeiten', 'partizip2', 'gearbeitet'],
    ['heißen', 'praesens', { 1: 'heißt' }],
    ['studieren', 'partizip2', 'studiert'],
    ['besuchen', 'partizip2', 'besucht'],
    ['lächeln', 'praesens', { 0: 'lächle', 3: 'lächeln' }],
    ['haben', 'praeteritum', ['hatte', 'hattest', 'hatte', 'hatten', 'hattet', 'hatten']],
    ['gehen', 'perfekt', ['bin gegangen', 'bist gegangen', 'ist gegangen', 'sind gegangen', 'seid gegangen', 'sind gegangen']],
    ['lernen', 'perfekt', { 0: 'habe gelernt' }],
    ['lernen', 'futur1', ['werde lernen', 'wirst lernen', 'wird lernen', 'werden lernen', 'werdet lernen', 'werden lernen']],
    ['lernen', 'konjunktiv2', { 0: 'würde lernen' }],
    ['sein', 'konjunktiv2', ['wäre', 'wärst', 'wäre', 'wären', 'wärt', 'wären']],
    ['fahren', 'praesens', ['fahre', 'fährst', 'fährt', 'fahren', 'fahrt', 'fahren']],
    ['lesen', 'praesens', { 1: 'liest', 2: 'liest' }],
    ['halten', 'praesens', { 1: 'hältst', 2: 'hält' }],
    ['finden', 'praeteritum', ['fand', 'fandest', 'fand', 'fanden', 'fandet', 'fanden']],
    ['nehmen', 'imperativ', ['—', 'nimm', '—', 'nehmen wir', 'nehmt', 'nehmen Sie']],
    ['anfangen', 'perfekt', { 0: 'habe angefangen' }],
    ['anfangen', 'imperativ', { 1: 'fang an', 5: 'fangen Sie an' }],
    ['aufstehen', 'praeteritum', { 0: 'stand auf' }],
    ['vorbereiten', 'partizip2', 'vorbereitet'],
    ['verstehen', 'partizip2', 'verstanden'],
    ['wissen', 'praesens', ['weiß', 'weißt', 'weiß', 'wissen', 'wisst', 'wissen']],
    ['können', 'konjunktiv2', ['könnte', 'könntest', 'könnte', 'könnten', 'könntet', 'könnten']],
    ['werden', 'praesens', ['werde', 'wirst', 'wird', 'werden', 'werdet', 'werden']],
    ['bringen', 'praeteritum', { 0: 'brachte' }],
    ['ankommen', 'perfekt', { 2: 'ist angekommen' }],
  ],
  it: [
    ['essere', 'presente', ['sono', 'sei', 'è', 'siamo', 'siete', 'sono']],
    // more
    ['parlare', 'presente', ['parlo', 'parli', 'parla', 'parliamo', 'parlate', 'parlano']],
    ['credere', 'presente', ['credo', 'credi', 'crede', 'crediamo', 'credete', 'credono']],
    ['dormire', 'presente', ['dormo', 'dormi', 'dorme', 'dormiamo', 'dormite', 'dormono']],
    ['capire', 'presente', ['capisco', 'capisci', 'capisce', 'capiamo', 'capite', 'capiscono']],
    ['cercare', 'presente', { 1: 'cerchi', 3: 'cerchiamo' }],
    ['cercare', 'futuro', { 0: 'cercherò' }],
    ['pagare', 'congiuntivoPresente', { 0: 'paghi', 4: 'paghiate' }],
    ['mangiare', 'presente', { 1: 'mangi', 3: 'mangiamo' }],
    ['mangiare', 'futuro', { 0: 'mangerò' }],
    ['cominciare', 'condizionale', { 0: 'comincerei' }],
    ['studiare', 'presente', { 1: 'studi', 3: 'studiamo' }],
    ['studiare', 'futuro', { 0: 'studierò' }],
    ['parlare', 'passatoRemoto', ['parlai', 'parlasti', 'parlò', 'parlammo', 'parlaste', 'parlarono']],
    ['prendere', 'passatoRemoto', ['presi', 'prendesti', 'prese', 'prendemmo', 'prendeste', 'presero']],
    ['avere', 'passatoRemoto', ['ebbi', 'avesti', 'ebbe', 'avemmo', 'aveste', 'ebbero']],
    ['fare', 'imperfetto', ['facevo', 'facevi', 'faceva', 'facevamo', 'facevate', 'facevano']],
    ['andare', 'futuro', ['andrò', 'andrai', 'andrà', 'andremo', 'andrete', 'andranno']],
    ['volere', 'condizionale', ['vorrei', 'vorresti', 'vorrebbe', 'vorremmo', 'vorreste', 'vorrebbero']],
    ['parlare', 'imperativo', ['—', 'parla', 'parli', 'parliamo', 'parlate', 'parlino']],
    ['prendere', 'imperativo', ['—', 'prendi', 'prenda', 'prendiamo', 'prendete', 'prendano']],
    ['finire', 'imperativo', ['—', 'finisci', 'finisca', 'finiamo', 'finite', 'finiscano']],
    ['parlare', 'congiuntivoImperfetto', ['parlassi', 'parlassi', 'parlasse', 'parlassimo', 'parlaste', 'parlassero']],
    ['essere', 'congiuntivoImperfetto', ['fossi', 'fossi', 'fosse', 'fossimo', 'foste', 'fossero']],
    ['bere', 'presente', ['bevo', 'bevi', 'beve', 'beviamo', 'bevete', 'bevono']],
    ['uscire', 'presente', ['esco', 'esci', 'esce', 'usciamo', 'uscite', 'escono']],
    ['venire', 'participio', 'venuto'],
    ['dire', 'gerundio', 'dicendo'],
    ['scrivere', 'participio', 'scritto'],
    ['stare', 'imperativo', { 1: 'stai', 2: 'stia' }],
    ['dovere', 'presente', ['devo', 'devi', 'deve', 'dobbiamo', 'dovete', 'devono']],
  ],
  pt: [
    ['ser', 'presente', ['sou', 'és', 'é', 'somos', 'sois', 'são']],
    // more
    ['falar', 'presente', ['falo', 'falas', 'fala', 'falamos', 'falais', 'falam']],
    ['comer', 'presente', ['como', 'comes', 'come', 'comemos', 'comeis', 'comem']],
    ['partir', 'presente', ['parto', 'partes', 'parte', 'partimos', 'partis', 'partem']],
    ['falar', 'preteritoPerfeito', ['falei', 'falaste', 'falou', 'falámos', 'falastes', 'falaram']],
    ['comer', 'preteritoPerfeito', ['comi', 'comeste', 'comeu', 'comemos', 'comestes', 'comeram']],
    ['ter', 'presente', ['tenho', 'tens', 'tem', 'temos', 'tendes', 'têm']],
    ['ter', 'preteritoPerfeito', ['tive', 'tiveste', 'teve', 'tivemos', 'tivestes', 'tiveram']],
    ['ir', 'preteritoPerfeito', ['fui', 'foste', 'foi', 'fomos', 'fostes', 'foram']],
    ['fazer', 'futuro', ['farei', 'farás', 'fará', 'faremos', 'fareis', 'farão']],
    ['falar', 'imperfeitoSubj', ['falasse', 'falasses', 'falasse', 'falássemos', 'falásseis', 'falassem']],
    ['comer', 'imperfeitoSubj', ['comesse', 'comesses', 'comesse', 'comêssemos', 'comêsseis', 'comessem']],
    ['ter', 'imperfeitoSubj', ['tivesse', 'tivesses', 'tivesse', 'tivéssemos', 'tivésseis', 'tivessem']],
    ['ficar', 'presenteSubj', ['fique', 'fiques', 'fique', 'fiquemos', 'fiqueis', 'fiquem']],
    ['ficar', 'preteritoPerfeito', { 0: 'fiquei' }],
    ['começar', 'presenteSubj', { 0: 'comece' }],
    ['chegar', 'preteritoPerfeito', { 0: 'cheguei' }],
    ['conhecer', 'presente', { 0: 'conheço', 1: 'conheces' }],
    ['conhecer', 'presenteSubj', { 0: 'conheça' }],
    ['dormir', 'presente', ['durmo', 'dormes', 'dorme', 'dormimos', 'dormis', 'dormem']],
    ['dormir', 'presenteSubj', ['durma', 'durmas', 'durma', 'durmamos', 'durmais', 'durmam']],
    ['preferir', 'presente', { 0: 'prefiro', 1: 'preferes' }],
    ['subir', 'presente', ['subo', 'sobes', 'sobe', 'subimos', 'subis', 'sobem']],
    ['seguir', 'presente', { 0: 'sigo', 1: 'segues' }],
    ['passear', 'presente', ['passeio', 'passeias', 'passeia', 'passeamos', 'passeais', 'passeiam']],
    ['pôr', 'presente', ['ponho', 'pões', 'põe', 'pomos', 'pondes', 'põem']],
    ['falar', 'imperativo', ['—', 'fala', 'fale', 'falemos', 'falai', 'falem']],
    ['ser', 'imperativo', ['—', 'sê', 'seja', 'sejamos', 'sede', 'sejam']],
    ['estar', 'presente', ['estou', 'estás', 'está', 'estamos', 'estais', 'estão']],
    ['ver', 'participio', 'visto'],
    ['possuir', 'preteritoPerfeito', ['possuí', 'possuíste', 'possuiu', 'possuímos', 'possuístes', 'possuíram']],
    ['vir', 'presente', ['venho', 'vens', 'vem', 'vimos', 'vindes', 'vêm']],
  ],
};

export function runTests(data, lang) {
  const failures = [];
  for (const [verb, tense, expected] of TESTS[lang] || []) {
    let got;
    try { got = conjugate(data, verb, tense); } catch (e) { failures.push(`[${lang}] ${verb}.${tense}: threw ${e.message}`); continue; }
    if (typeof expected === 'string') {
      if (got !== expected) failures.push(`[${lang}] ${verb}.${tense}: expected "${expected}", got "${got}"`);
    } else if (Array.isArray(expected)) {
      if (JSON.stringify(got) !== JSON.stringify(expected)) failures.push(`[${lang}] ${verb}.${tense}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(got)}`);
    } else {
      for (const [p, f] of Object.entries(expected)) {
        if (got[p] !== f) failures.push(`[${lang}] ${verb}.${tense}[${p}]: expected "${f}", got "${got[p]}"`);
      }
    }
  }
  return failures;
}

// ───────────────────────────── main ─────────────────────────────

function main() {
  let bad = 0;
  // (Simpl for Mac keeps the data files where the app bundles them: node scripts/dev/verbs/check.mjs mac/Simpl/Resources/Verbs)
  const DIR = process.argv[2] || HERE;
  for (const lang of LANGS) {
    const raw = readFileSync(join(DIR, `${lang}.json`), 'utf8');
    const data = JSON.parse(raw);
    const kb = (Buffer.byteLength(raw) / 1024).toFixed(1);
    const errors = validate(data, lang);
    const failures = runTests(data, lang);
    const n = (TESTS[lang] || []).length;
    const ok = errors.length === 0 && failures.length === 0;
    if (!ok) bad++;
    console.log(`${lang}.json  ${kb} KB  irregulars=${Object.keys(data.irregulars).length}  stemChanging=${Object.keys(data.stemChanging).length}  common=${data.commonVerbs.length}  tests=${n - failures.length}/${n}  ${ok ? 'OK' : 'FAIL'}`);
    for (const e of errors.slice(0, 40)) console.log('  schema: ' + e);
    if (errors.length > 40) console.log(`  … ${errors.length - 40} more schema errors`);
    for (const f of failures) console.log('  test: ' + f);
    if (Buffer.byteLength(raw) > 400 * 1024) { console.log('  size: over 400 KB'); bad++; }
  }
  if (bad) { console.log(`\n${bad} file(s) failed`); process.exit(1); }
  console.log('\nall good');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
