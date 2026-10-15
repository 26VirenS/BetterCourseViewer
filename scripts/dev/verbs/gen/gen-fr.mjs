// French — generator for fr.json
import { derive, finish } from './common.mjs';

const FUT = ['ai', 'as', 'a', 'ons', 'ez', 'ont'];
const COND = ['ais', 'ais', 'ait', 'ions', 'iez', 'aient'];
const fut = s => FUT.map(e => s + e);
const cond = s => COND.map(e => s + e);
const PS = {
  a: ['ai', 'as', 'a', 'âmes', 'âtes', 'èrent'],
  i: ['is', 'is', 'it', 'îmes', 'îtes', 'irent'],
  u: ['us', 'us', 'ut', 'ûmes', 'ûtes', 'urent'],
  in: ['ins', 'ins', 'int', 'înmes', 'întes', 'inrent'],
};
const ps = (s, t) => PS[t].map(e => s + e);
const imparf = s => COND.map(e => s + e);
const subj = (boot, nous) => [boot + 'e', boot + 'es', boot + 'e', nous + 'ions', nous + 'iez', boot + 'ent'];

const ETRE_VERBS = ['aller', 'venir', 'arriver', 'partir', 'entrer', 'sortir', 'rester', 'tomber', 'naître', 'mourir', 'monter', 'descendre', 'retourner', 'passer',
  'rentrer', 'devenir', 'revenir', 'parvenir', 'intervenir', 'survenir', 'repartir', 'redevenir', 'décéder', 'apparaître', 'renaître'];
const E_GRAVE_VERBS = ['acheter', 'achever', 'amener', 'emmener', 'mener', 'promener', 'ramener', 'lever', 'élever', 'enlever', 'relever', 'soulever', 'prélever',
  'peser', 'soupeser', 'semer', 'geler', 'dégeler', 'congeler', 'surgeler', 'peler', 'crever', 'haleter', 'celer', 'ciseler', 'déceler', 'démanteler', 'écarteler',
  'marteler', 'modeler', 'crocheter', 'fureter', 'racheter', 'parsemer', 'receler'];

const data = {
  language: 'fr',
  name: 'French',
  pronouns: ['je', 'tu', 'il/elle/on', 'nous', 'vous', 'ils/elles'],
  imperativePronouns: ['—', 'tu', '—', 'nous', 'vous', '—'],
  tenses: [
    { id: 'present', name: 'Présent', group: 'indicativo' },
    { id: 'passeCompose', name: 'Passé composé', group: 'indicativo' },
    { id: 'imparfait', name: 'Imparfait', group: 'indicativo' },
    { id: 'futur', name: 'Futur simple', group: 'indicativo' },
    { id: 'conditionnel', name: 'Conditionnel présent', group: 'condicional' },
    { id: 'subjonctifPresent', name: 'Subjonctif présent', group: 'subjuntivo' },
    { id: 'passeSimple', name: 'Passé simple', group: 'indicativo' },
    { id: 'imperatif', name: 'Impératif', group: 'imperativo' },
    { id: 'participePresent', name: 'Participe présent', group: 'nonfinite', single: true },
    { id: 'participePasse', name: 'Participe passé', group: 'nonfinite', single: true },
  ],
  groupNames: { indicativo: 'Indicatif', subjuntivo: 'Subjonctif', imperativo: 'Impératif', condicional: 'Conditionnel', nonfinite: 'Participes' },
  stemKinds: [
    { kind: 'infinitiveStem', description: 'The infinitive minus its class ending (parler → parl, finir → fin, vendre → vend). Default. Spelling rules are applied to it before any tense suffix is added.' },
    { kind: 'infinitiveStem+suffix', description: 'infinitiveStem with a per-class suffix re-attached after spelling (futur/conditionnel: parl+er, fin+ir, vend+r → parler-ai, finir-ai, vendr-ai; achèt+er-ai).' },
    { kind: 'form', description: 'Another conjugated form of the same verb with a suffix stripped (not used by the regular French classes; irregular verbs are explicit).' },
  ],
  rules: {
    classes: [
      { id: 'er', name: '-er', match: 'er$', strip: 2 },
      { id: 'ir', name: '-ir (finir type, -iss-)', match: 'ir$', strip: 2 },
      { id: 're', name: '-re (vendre type)', match: 're$', strip: 2 },
    ],
    endings: {
      er: {
        present: ['e', 'es', 'e', 'ons', 'ez', 'ent'],
        imparfait: COND, futur: FUT, conditionnel: COND,
        subjonctifPresent: ['e', 'es', 'e', 'ions', 'iez', 'ent'],
        passeSimple: PS.a, participePresent: 'ant', participePasse: 'é',
      },
      ir: {
        present: ['is', 'is', 'it', 'issons', 'issez', 'issent'],
        imparfait: ['issais', 'issais', 'issait', 'issions', 'issiez', 'issaient'], futur: FUT, conditionnel: COND,
        subjonctifPresent: ['isse', 'isses', 'isse', 'issions', 'issiez', 'issent'],
        passeSimple: PS.i, participePresent: 'issant', participePasse: 'i',
      },
      re: {
        present: ['s', 's', '', 'ons', 'ez', 'ent'],
        imparfait: COND, futur: FUT, conditionnel: COND,
        subjonctifPresent: ['e', 'es', 'e', 'ions', 'iez', 'ent'],
        passeSimple: PS.i, participePresent: 'ant', participePasse: 'u',
      },
    },
    stems: {
      futur: { kind: 'infinitiveStem', suffix: { er: 'er', ir: 'ir', re: 'r' } },
      conditionnel: { kind: 'infinitiveStem', suffix: { er: 'er', ir: 'ir', re: 'r' } },
    },
    compose: {
      imperatif: [
        { literal: '—' },
        { form: 'present', person: 1, stripIf: 'es$' },
        { literal: '—' },
        { form: 'present', person: 3 },
        { form: 'present', person: 4 },
        { literal: '—' },
      ],
    },
    compound: {
      passeCompose: { aux: 'avoir|être', auxTense: 'present', with: 'participePasse', auxOf: { 'être': ETRE_VERBS }, agreement: { 'être': ['', '', '', 's', 's', 's'] }, note: 'with être the participle agrees: masculine forms shown (allé / allés); feminine -e not shown; reflexive verbs are not covered' },
    },
    accentMap: {},
    spelling: [
      { id: 'cer->ç', when: 'stem ends in c and the ending starts with a or o (commencer → commençons, commençais)', classes: ['er'], match: 'c$', tenses: { present: [3], imparfait: [0, 1, 2, 5], passeSimple: [0, 1, 2, 3, 4], participePresent: [0] }, do: { stem: { from: 'c$', to: 'ç' } } },
      { id: 'ger->ge', when: 'stem ends in g and the ending starts with a or o (manger → mangeons, mangeais)', classes: ['er'], match: 'g$', tenses: { present: [3], imparfait: [0, 1, 2, 5], passeSimple: [0, 1, 2, 3, 4], participePresent: [0] }, do: { stem: { from: 'g$', to: 'ge' } } },
      { id: 'yer->i', when: 'stem ends in y before a mute e (nettoyer → nettoie, nettoierai); for -ayer verbs the y spelling (paye) is also accepted', classes: ['er'], match: 'y$', tenses: { present: [0, 1, 2, 5], subjonctifPresent: [0, 1, 2, 5], futur: 'all', conditionnel: 'all' }, do: { stem: { from: 'y$', to: 'i' } } },
      { id: 'e->è', when: 'e in the last stem syllable becomes è before a mute e (acheter → achète, achèterai, lever → lève)', classes: ['er'], verbs: E_GRAVE_VERBS, tenses: { present: [0, 1, 2, 5], subjonctifPresent: [0, 1, 2, 5], futur: 'all', conditionnel: 'all' }, do: { stem: { from: 'e([^aeiouyéè]+)$', to: 'è$1' } } },
      { id: 'é->è', when: 'é in the last stem syllable becomes è before a mute e in the present/subjunctive boot (préférer → préfère); the futur keeps é', classes: ['er'], match: 'é[^aeiouyéè]+$', tenses: { present: [0, 1, 2, 5], subjonctifPresent: [0, 1, 2, 5] }, do: { stem: { from: 'é([^aeiouyéè]+)$', to: 'è$1' } } },
      { id: 'eler/eter->ll/tt', when: '-eler/-eter double the consonant before a mute e (appeler → appelle, jeter → jetterai), except the è verbs above', classes: ['er'], match: '(el|et)$', except: E_GRAVE_VERBS, tenses: { present: [0, 1, 2, 5], subjonctifPresent: [0, 1, 2, 5], futur: 'all', conditionnel: 'all' }, do: { stem: { from: '(l|t)$', to: '$1$1' } } },
    ],
  },
  stemChangeKinds: {},
  stemChanging: {},
  separable: [],
  irregulars: {},
  commonVerbs: [],
};

const I = data.irregulars;
const DASH6 = ['—', '—', '—', '—', '—', '—'];

/** compact irregular definition → partial explicit entry */
function mk(v, d) {
  const pres = d.pres;
  const nous = d.nous ?? pres[3].replace(/ons$/, '');
  const ils = d.ils ?? pres[5].replace(/ent$/, '');
  const e = { present: pres };
  e.imparfait = d.imparf ?? imparf(nous);
  e.subjonctifPresent = d.subj ?? subj(ils, nous);
  if (d.fut) { e.futur = fut(d.fut); e.conditionnel = cond(d.fut); }
  e.passeSimple = d.ps.length === 6 ? d.ps : ps(d.ps[0], d.ps[1]);
  e.participePasse = d.pp;
  e.participePresent = d.ppr ?? nous + 'ant';
  e.imperatif = d.imp ?? ['—', /es$/.test(pres[1]) ? pres[1].slice(0, -1) : pres[1], '—', pres[3], pres[4], '—'];
  if (d.pc) e.passeCompose = d.pc;
  I[v] = e;
}

mk('être', { pres: ['suis', 'es', 'est', 'sommes', 'êtes', 'sont'], imparf: ['étais', 'étais', 'était', 'étions', 'étiez', 'étaient'], ps: ['f', 'u'], fut: 'ser',
  subj: ['sois', 'sois', 'soit', 'soyons', 'soyez', 'soient'], imp: ['—', 'sois', '—', 'soyons', 'soyez', '—'], ppr: 'étant', pp: 'été' });
mk('avoir', { pres: ['ai', 'as', 'a', 'avons', 'avez', 'ont'], ps: ['e', 'u'], fut: 'aur', subj: ['aie', 'aies', 'ait', 'ayons', 'ayez', 'aient'],
  imp: ['—', 'aie', '—', 'ayons', 'ayez', '—'], ppr: 'ayant', pp: 'eu' });
mk('aller', { pres: ['vais', 'vas', 'va', 'allons', 'allez', 'vont'], ps: ['all', 'a'], fut: 'ir', subj: ['aille', 'ailles', 'aille', 'allions', 'alliez', 'aillent'],
  imp: ['—', 'va', '—', 'allons', 'allez', '—'], pp: 'allé' });
mk('faire', { pres: ['fais', 'fais', 'fait', 'faisons', 'faites', 'font'], ps: ['f', 'i'], fut: 'fer', subj: ['fasse', 'fasses', 'fasse', 'fassions', 'fassiez', 'fassent'], pp: 'fait' });
mk('pouvoir', { pres: ['peux', 'peux', 'peut', 'pouvons', 'pouvez', 'peuvent'], ps: ['p', 'u'], fut: 'pourr', subj: ['puisse', 'puisses', 'puisse', 'puissions', 'puissiez', 'puissent'], imp: DASH6, pp: 'pu' });
mk('vouloir', { pres: ['veux', 'veux', 'veut', 'voulons', 'voulez', 'veulent'], ps: ['voul', 'u'], fut: 'voudr', subj: ['veuille', 'veuilles', 'veuille', 'voulions', 'vouliez', 'veuillent'],
  imp: ['—', 'veuille', '—', 'veuillons', 'veuillez', '—'], pp: 'voulu' });
mk('savoir', { pres: ['sais', 'sais', 'sait', 'savons', 'savez', 'savent'], ps: ['s', 'u'], fut: 'saur', subj: ['sache', 'saches', 'sache', 'sachions', 'sachiez', 'sachent'],
  imp: ['—', 'sache', '—', 'sachons', 'sachez', '—'], ppr: 'sachant', pp: 'su' });
mk('venir', { pres: ['viens', 'viens', 'vient', 'venons', 'venez', 'viennent'], ps: ['v', 'in'], fut: 'viendr', pp: 'venu' });
mk('tenir', { pres: ['tiens', 'tiens', 'tient', 'tenons', 'tenez', 'tiennent'], ps: ['t', 'in'], fut: 'tiendr', pp: 'tenu' });
mk('voir', { pres: ['vois', 'vois', 'voit', 'voyons', 'voyez', 'voient'], ps: ['v', 'i'], fut: 'verr', pp: 'vu' });
mk('prendre', { pres: ['prends', 'prends', 'prend', 'prenons', 'prenez', 'prennent'], ps: ['pr', 'i'], pp: 'pris' });
mk('devoir', { pres: ['dois', 'dois', 'doit', 'devons', 'devez', 'doivent'], ps: ['d', 'u'], fut: 'devr', pp: 'dû' });
mk('dire', { pres: ['dis', 'dis', 'dit', 'disons', 'dites', 'disent'], ps: ['d', 'i'], pp: 'dit' });
mk('mettre', { pres: ['mets', 'mets', 'met', 'mettons', 'mettez', 'mettent'], ps: ['m', 'i'], pp: 'mis' });
mk('partir', { pres: ['pars', 'pars', 'part', 'partons', 'partez', 'partent'], ps: ['part', 'i'], pp: 'parti' });
mk('sortir', { pres: ['sors', 'sors', 'sort', 'sortons', 'sortez', 'sortent'], ps: ['sort', 'i'], pp: 'sorti' });
mk('dormir', { pres: ['dors', 'dors', 'dort', 'dormons', 'dormez', 'dorment'], ps: ['dorm', 'i'], pp: 'dormi' });
mk('servir', { pres: ['sers', 'sers', 'sert', 'servons', 'servez', 'servent'], ps: ['serv', 'i'], pp: 'servi' });
mk('sentir', { pres: ['sens', 'sens', 'sent', 'sentons', 'sentez', 'sentent'], ps: ['sent', 'i'], pp: 'senti' });
mk('mentir', { pres: ['mens', 'mens', 'ment', 'mentons', 'mentez', 'mentent'], ps: ['ment', 'i'], pp: 'menti' });
for (const [v, pp] of [['ouvrir', 'ouvert'], ['couvrir', 'couvert'], ['offrir', 'offert'], ['souffrir', 'souffert']]) {
  const s = v.slice(0, -2);
  mk(v, { pres: [s + 'e', s + 'es', s + 'e', s + 'ons', s + 'ez', s + 'ent'], ps: [s, 'i'], pp });
}
mk('courir', { pres: ['cours', 'cours', 'court', 'courons', 'courez', 'courent'], ps: ['cour', 'u'], fut: 'courr', pp: 'couru' });
mk('mourir', { pres: ['meurs', 'meurs', 'meurt', 'mourons', 'mourez', 'meurent'], ps: ['mour', 'u'], fut: 'mourr', pp: 'mort' });
mk('lire', { pres: ['lis', 'lis', 'lit', 'lisons', 'lisez', 'lisent'], ps: ['l', 'u'], pp: 'lu' });
mk('écrire', { pres: ['écris', 'écris', 'écrit', 'écrivons', 'écrivez', 'écrivent'], ps: ['écriv', 'i'], pp: 'écrit' });
mk('boire', { pres: ['bois', 'bois', 'boit', 'buvons', 'buvez', 'boivent'], ps: ['b', 'u'], pp: 'bu' });
mk('croire', { pres: ['crois', 'crois', 'croit', 'croyons', 'croyez', 'croient'], ps: ['cr', 'u'], pp: 'cru' });
mk('connaître', { pres: ['connais', 'connais', 'connaît', 'connaissons', 'connaissez', 'connaissent'], ps: ['conn', 'u'], pp: 'connu' });
mk('paraître', { pres: ['parais', 'parais', 'paraît', 'paraissons', 'paraissez', 'paraissent'], ps: ['par', 'u'], pp: 'paru' });
mk('naître', { pres: ['nais', 'nais', 'naît', 'naissons', 'naissez', 'naissent'], ps: ['naqu', 'i'], pp: 'né' });
mk('vivre', { pres: ['vis', 'vis', 'vit', 'vivons', 'vivez', 'vivent'], ps: ['véc', 'u'], pp: 'vécu' });
mk('suivre', { pres: ['suis', 'suis', 'suit', 'suivons', 'suivez', 'suivent'], ps: ['suiv', 'i'], pp: 'suivi' });
mk('rire', { pres: ['ris', 'ris', 'rit', 'rions', 'riez', 'rient'], ps: ['r', 'i'], pp: 'ri' });
for (const v of ['conduire', 'construire', 'produire', 'traduire', 'cuire', 'détruire', 'réduire', 'introduire', 'séduire', 'instruire', 'reconstruire', 'reproduire', 'nuire']) {
  const s = v.slice(0, -2) + 's';
  mk(v, { pres: [s, s, s.slice(0, -1) + 't', s + 'ons', s + 'ez', s + 'ent'], ps: [s, 'i'], pp: v === 'nuire' ? 'nui' : s.slice(0, -1) + 't' });
}
mk('plaire', { pres: ['plais', 'plais', 'plaît', 'plaisons', 'plaisez', 'plaisent'], ps: ['pl', 'u'], pp: 'plu' });
mk('taire', { pres: ['tais', 'tais', 'tait', 'taisons', 'taisez', 'taisent'], ps: ['t', 'u'], pp: 'tu' });
for (const v of ['recevoir', 'apercevoir', 'décevoir', 'concevoir', 'percevoir']) {
  const s = v.slice(0, -5); // recevoir → 'rec'
  const c = s.slice(0, -1) + 'ç'; // reç
  mk(v, { pres: [c + 'ois', c + 'ois', c + 'oit', s + 'evons', s + 'evez', c + 'oivent'], ps: [c, 'u'], fut: s + 'evr', pp: c + 'u' });
}
mk('falloir', { pres: ['—', '—', 'faut', '—', '—', '—'], imparf: ['—', '—', 'fallait', '—', '—', '—'], ps: ['—', '—', 'fallut', '—', '—', '—'],
  subj: ['—', '—', 'faille', '—', '—', '—'], imp: DASH6, ppr: '—', pp: 'fallu', pc: ['—', '—', 'a fallu', '—', '—', '—'] });
I.falloir.futur = ['—', '—', 'faudra', '—', '—', '—'];
I.falloir.conditionnel = ['—', '—', 'faudrait', '—', '—', '—'];
mk('pleuvoir', { pres: ['—', '—', 'pleut', '—', '—', '—'], imparf: ['—', '—', 'pleuvait', '—', '—', '—'], ps: ['—', '—', 'plut', '—', '—', '—'],
  subj: ['—', '—', 'pleuve', '—', '—', '—'], imp: DASH6, ppr: 'pleuvant', pp: 'plu', pc: ['—', '—', 'a plu', '—', '—', '—'] });
I.pleuvoir.futur = ['—', '—', 'pleuvra', '—', '—', '—'];
I.pleuvoir.conditionnel = ['—', '—', 'pleuvrait', '—', '—', '—'];
mk('valoir', { pres: ['vaux', 'vaux', 'vaut', 'valons', 'valez', 'valent'], ps: ['val', 'u'], fut: 'vaudr', subj: ['vaille', 'vailles', 'vaille', 'valions', 'valiez', 'vaillent'], pp: 'valu' });
mk('asseoir', { pres: ['assieds', 'assieds', 'assied', 'asseyons', 'asseyez', 'asseyent'], ps: ['ass', 'i'], fut: 'assiér', pp: 'assis' });
mk('battre', { pres: ['bats', 'bats', 'bat', 'battons', 'battez', 'battent'], ps: ['batt', 'i'], pp: 'battu' });
mk('rompre', { pres: ['romps', 'romps', 'rompt', 'rompons', 'rompez', 'rompent'], ps: ['romp', 'i'], pp: 'rompu' });
for (const v of ['craindre', 'peindre', 'éteindre', 'atteindre', 'joindre', 'rejoindre', 'plaindre', 'contraindre', 'feindre', 'teindre', 'restreindre']) {
  const s = v.slice(0, -3); // craindre → crain
  const g = s.slice(0, -1) + 'gn'; // craign
  mk(v, { pres: [s + 's', s + 's', s + 't', g + 'ons', g + 'ez', g + 'ent'], ps: [g, 'i'], pp: s + 't' });
}
mk('résoudre', { pres: ['résous', 'résous', 'résout', 'résolvons', 'résolvez', 'résolvent'], ps: ['résol', 'u'], pp: 'résolu' });
mk('vaincre', { pres: ['vaincs', 'vaincs', 'vainc', 'vainquons', 'vainquez', 'vainquent'], ps: ['vainqu', 'i'], pp: 'vaincu' });
mk('fuir', { pres: ['fuis', 'fuis', 'fuit', 'fuyons', 'fuyez', 'fuient'], ps: ['fu', 'i'], pp: 'fui' });
mk('cueillir', { pres: ['cueille', 'cueilles', 'cueille', 'cueillons', 'cueillez', 'cueillent'], ps: ['cueill', 'i'], fut: 'cueiller', pp: 'cueilli' });
mk('acquérir', { pres: ['acquiers', 'acquiers', 'acquiert', 'acquérons', 'acquérez', 'acquièrent'], ps: ['acqu', 'i'], fut: 'acquerr', pp: 'acquis' });
mk('conquérir', { pres: ['conquiers', 'conquiers', 'conquiert', 'conquérons', 'conquérez', 'conquièrent'], ps: ['conqu', 'i'], fut: 'conquerr', pp: 'conquis' });
mk('bouillir', { pres: ['bous', 'bous', 'bout', 'bouillons', 'bouillez', 'bouillent'], ps: ['bouill', 'i'], pp: 'bouilli' });
mk('suffire', { pres: ['suffis', 'suffis', 'suffit', 'suffisons', 'suffisez', 'suffisent'], ps: ['suff', 'i'], pp: 'suffi' });
mk('conclure', { pres: ['conclus', 'conclus', 'conclut', 'concluons', 'concluez', 'concluent'], ps: ['concl', 'u'], pp: 'conclu' });
mk('inclure', { pres: ['inclus', 'inclus', 'inclut', 'incluons', 'incluez', 'incluent'], ps: ['incl', 'u'], pp: 'inclus' });
mk('exclure', { pres: ['exclus', 'exclus', 'exclut', 'excluons', 'excluez', 'excluent'], ps: ['excl', 'u'], pp: 'exclu' });
mk('coudre', { pres: ['couds', 'couds', 'coud', 'cousons', 'cousez', 'cousent'], ps: ['cous', 'i'], pp: 'cousu' });
I.envoyer = { futur: fut('enverr'), conditionnel: cond('enverr') };
I.renvoyer = { futur: fut('renverr'), conditionnel: cond('renverr') };

// prefixed derivatives (compound tenses are recomputed by the engine)
const D = (list, base) => { for (const [v, p] of list) derive(I, data, v, base, p); };
D([['apprendre', 'ap'], ['comprendre', 'com'], ['surprendre', 'sur'], ['reprendre', 're'], ['entreprendre', 'entre']], 'prendre');
D([['permettre', 'per'], ['promettre', 'pro'], ['admettre', 'ad'], ['remettre', 're'], ['soumettre', 'sou'], ['transmettre', 'trans'], ['commettre', 'com'], ['omettre', 'o']], 'mettre');
D([['devenir', 'de'], ['revenir', 're'], ['prévenir', 'pré'], ['intervenir', 'inter'], ['parvenir', 'par'], ['survenir', 'sur'], ['convenir', 'con'], ['redevenir', 'rede']], 'venir');
D([['obtenir', 'ob'], ['appartenir', 'appar'], ['contenir', 'con'], ['maintenir', 'main'], ['soutenir', 'sou'], ['retenir', 're'], ['entretenir', 'entre']], 'tenir');
D([['revoir', 're']], 'voir');
derive(I, data, 'prévoir', 'voir', 'pré', { over: { futur: fut('prévoir'), conditionnel: cond('prévoir') } });
D([['décrire', 'dé'], ['inscrire', 'ins'], ['réécrire', 'ré']], 'écrire');
D([['relire', 're'], ['élire', 'é']], 'lire');
D([['reconnaître', 're']], 'connaître');
D([['disparaître', 'dis'], ['apparaître', 'ap']], 'paraître');
D([['poursuivre', 'pour']], 'suivre');
D([['survivre', 'sur'], ['revivre', 're']], 'vivre');
D([['sourire', 'sou']], 'rire');
D([['repartir', 're']], 'partir');
D([['ressentir', 'res'], ['consentir', 'con']], 'sentir');
D([['endormir', 'en']], 'dormir');
D([['découvrir', 'dé'], ['recouvrir', 're']], 'couvrir');
D([['satisfaire', 'satis'], ['défaire', 'dé'], ['refaire', 're']], 'faire');
D([['redire', 're']], 'dire');
for (const [v, p] of [['interdire', 'inter'], ['contredire', 'contre'], ['prédire', 'pré']]) {
  derive(I, data, v, 'dire', p);
  I[v].present[4] = v.slice(0, -2) + 'sez';
  I[v].imperatif[4] = v.slice(0, -2) + 'sez';
}
D([['combattre', 'com'], ['débattre', 'dé'], ['abattre', 'a']], 'battre');
D([['interrompre', 'inter'], ['corrompre', 'cor']], 'rompre');
D([['convaincre', 'con']], 'vaincre');
D([['accueillir', 'ac'], ['recueillir', 're']], 'cueillir');
D([['déplaire', 'dé']], 'plaire');
D([['entretenir_', 'entre']], 'tenir'); delete I['entretenir_'];

data.commonVerbs = [
  'être', 'avoir', 'faire', 'dire', 'pouvoir', 'aller', 'voir', 'savoir', 'vouloir', 'venir', 'falloir', 'devoir', 'croire', 'trouver', 'donner', 'prendre', 'parler', 'aimer', 'passer', 'mettre',
  'demander', 'tenir', 'sembler', 'laisser', 'rester', 'penser', 'entendre', 'regarder', 'répondre', 'rendre', 'connaître', 'paraître', 'arriver', 'sentir', 'attendre', 'vivre', 'chercher', 'sortir', 'comprendre', 'porter',
  'devenir', 'entrer', 'écrire', 'appeler', 'tomber', 'suivre', 'commencer', 'revenir', 'montrer', 'partir', 'jouer', 'ouvrir', 'lire', 'finir', 'perdre', 'tourner', 'reprendre', 'apprendre', 'servir', 'manger',
  'offrir', 'recevoir', 'marcher', 'dormir', 'écouter', 'boire', 'travailler', 'acheter', 'choisir', 'oublier', 'courir', 'payer', 'habiter', 'étudier', 'préférer', 'réussir', 'essayer', 'envoyer', 'nettoyer', 'jeter',
  'lever', 'mourir', 'naître', 'conduire', 'produire', 'traduire', 'construire', 'détruire', 'plaire', 'mentir', 'rire', 'sourire', 'battre', 'craindre', 'peindre', 'éteindre', 'atteindre', 'joindre', 'rejoindre', 'plaindre',
  'résoudre', 'vaincre', 'convaincre', 'fuir', 'cueillir', 'accueillir', 'acquérir', 'bouillir', 'suffire', 'conclure', 'inclure', 'exclure', 'valoir', 'pleuvoir', 'asseoir', 'apercevoir', 'décevoir', 'concevoir', 'permettre', 'promettre',
  'admettre', 'remettre', 'soumettre', 'transmettre', 'commettre', 'surprendre', 'entreprendre', 'prévenir', 'intervenir', 'parvenir', 'convenir', 'obtenir', 'appartenir', 'contenir', 'maintenir', 'soutenir', 'retenir', 'entretenir', 'revoir', 'prévoir',
  'décrire', 'inscrire', 'relire', 'élire', 'reconnaître', 'disparaître', 'apparaître', 'poursuivre', 'survivre', 'repartir', 'ressentir', 'consentir', 'endormir', 'couvrir', 'découvrir', 'souffrir', 'satisfaire', 'défaire', 'refaire', 'interdire',
  'contredire', 'prédire', 'combattre', 'débattre', 'interrompre', 'rompre', 'taire', 'cuire', 'réduire', 'introduire', 'séduire', 'instruire', 'nuire', 'coudre', 'déplaire', 'redire', 'percevoir', 'renvoyer',
  'accepter', 'aider', 'ajouter', 'amener', 'annoncer', 'apporter', 'arrêter', 'arranger', 'assurer', 'avancer', 'changer', 'chanter', 'coucher', 'compter', 'continuer', 'couper', 'crier', 'danser', 'décider', 'déjeuner',
  'dépenser', 'dessiner', 'discuter', 'douter', 'durer', 'échanger', 'emmener', 'emporter', 'enseigner', 'espérer', 'expliquer', 'fermer', 'fêter', 'frapper', 'fumer', 'gagner', 'garder', 'goûter', 'inviter', 'juger',
  'lancer', 'louer', 'manquer', 'mener', 'monter', 'nager', 'noter', 'occuper', 'organiser', 'oser', 'participer', 'partager', 'pleurer', 'poser', 'pousser', 'préparer', 'présenter', 'prêter', 'profiter', 'proposer',
  'quitter', 'raconter', 'ranger', 'rappeler', 'remercier', 'rencontrer', 'rentrer', 'retourner', 'retrouver', 'réveiller', 'rêver', 'saluer', 'sauter', 'sauver', 'signer', 'skier', 'sonner', 'souhaiter', 'téléphoner', 'tirer',
  'toucher', 'traverser', 'trembler', 'tromper', 'utiliser', 'visiter', 'voler', 'voyager', 'adorer', 'détester', 'apprécier', 'bavarder', 'brosser', 'cacher', 'casser', 'chauffer', 'coller', 'commander', 'comparer', 'conseiller',
  'corriger', 'coûter', 'créer', 'cuisiner', 'dépasser', 'déranger', 'développer', 'deviner', 'dîner', 'diriger', 'effacer', 'embrasser', 'emprunter', 'enlever', 'entourer', 'exagérer', 'exprimer', 'féliciter', 'fonder', 'former',
  'glisser', 'griller', 'habiller', 'hésiter', 'ignorer', 'imaginer', 'imprimer', 'indiquer', 'installer', 'intéresser', 'laver', 'libérer', 'livrer', 'marquer', 'mélanger', 'mesurer', 'négliger', 'obliger', 'observer', 'ordonner',
  'oublier_', 'pardonner', 'peser', 'placer', 'plonger', 'posséder', 'préciser', 'presser', 'prier', 'promener', 'prononcer', 'protéger', 'prouver', 'ramasser', 'ranger_', 'réaliser', 'reculer', 'refuser', 'regretter', 'remarquer',
  'remplacer', 'rencontrer_', 'renoncer', 'réparer', 'répéter', 'respecter', 'rouler', 'sécher', 'semer', 'séparer', 'serrer', 'soigner', 'songer', 'souligner', 'supposer', 'tâcher', 'tenter', 'terminer', 'tester', 'trier',
  'varier', 'vérifier', 'verser', 'voter', 'agir', 'réagir', 'bâtir', 'réfléchir', 'remplir', 'grandir', 'grossir', 'maigrir', 'vieillir', 'rougir', 'obéir', 'punir', 'nourrir', 'guérir', 'ralentir', 'avertir',
  'établir', 'applaudir', 'atterrir', 'investir', 'fournir', 'définir', 'saisir', 'unir', 'réunir', 'franchir', 'accomplir', 'approfondir', 'garantir', 'élargir', 'enrichir', 'envahir', 'fleurir', 'mûrir', 'pâlir', 'trahir',
  'abolir', 'jouir', 'éclaircir', 'blanchir', 'noircir', 'vomir', 'vendre', 'descendre', 'défendre', 'dépendre', 'prétendre', 'étendre', 'tendre', 'fondre', 'confondre', 'correspondre', 'mordre', 'tordre', 'pendre', 'suspendre', 'répandre',
  'répondre_', 'perdre_', 'entendre_', 'attendre_', 'rendre_', 'geler', 'achever', 'élever', 'relever', 'soulever', 'congeler', 'balayer', 'effrayer', 'ennuyer', 'appuyer', 'essuyer', 'employer', 'envoyer_', 'aboyer', 'noyer', 'projeter', 'rejeter', 'feuilleter', 'épeler', 'renouveler',
].filter(v => !v.endsWith('_'));

finish(data, ['être', 'avoir', 'aller', 'faire', 'pouvoir', 'vouloir', 'savoir', 'venir', 'voir', 'prendre', 'devoir', 'dire', 'mettre', 'partir', 'sortir']);
