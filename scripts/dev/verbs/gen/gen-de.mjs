// German — generator for de.json
import { derive, finish } from './common.mjs';
import { conjugate } from '../check.mjs';

const DASH6 = ['—', '—', '—', '—', '—', '—'];
const E_INS = /(t|d|[^aeiouäöülrmnh][mn]|ch[mn])$/;
const K2 = ['e', 'est', 'e', 'en', 'et', 'en'];
const WEAK_PRAT = ['te', 'test', 'te', 'ten', 'tet', 'ten'];

const SEIN_VERBS = ['sein', 'werden', 'gehen', 'kommen', 'fahren', 'laufen', 'fallen', 'bleiben', 'sterben', 'wachsen', 'steigen', 'fliegen', 'fließen', 'reisen', 'passieren',
  'geschehen', 'gelingen', 'misslingen', 'folgen', 'rennen', 'schwimmen', 'springen', 'sinken', 'wandern', 'landen', 'starten', 'verschwinden', 'begegnen', 'erscheinen', 'entstehen',
  'geraten', 'treten', 'reiten', 'erschrecken', 'schmelzen', 'aufwachen', 'einschlafen', 'aufstehen', 'umziehen', 'zurückkehren', 'klettern', 'stolpern', 'rutschen', 'stürzen',
  'explodieren', 'gelangen', 'erfolgen', 'auftauchen', 'segeln', 'joggen', 'fliehen', 'verreisen', 'auswandern', 'einwandern', 'eintreffen', 'vergehen', 'verunglücken'];

const data = {
  language: 'de',
  name: 'German',
  pronouns: ['ich', 'du', 'er/sie/es', 'wir', 'ihr', 'sie/Sie'],
  imperativePronouns: ['—', 'du', '—', 'wir', 'ihr', 'Sie'],
  tenses: [
    { id: 'praesens', name: 'Präsens', group: 'indicativo' },
    { id: 'praeteritum', name: 'Präteritum', group: 'indicativo' },
    { id: 'perfekt', name: 'Perfekt', group: 'indicativo' },
    { id: 'futur1', name: 'Futur I', group: 'indicativo' },
    { id: 'konjunktiv2', name: 'Konjunktiv II', group: 'subjuntivo' },
    { id: 'imperativ', name: 'Imperativ', group: 'imperativo' },
    { id: 'partizip2', name: 'Partizip II', group: 'nonfinite', single: true },
  ],
  groupNames: { indicativo: 'Indikativ', subjuntivo: 'Konjunktiv', imperativo: 'Imperativ', condicional: 'Konditional', nonfinite: 'Partizip' },
  stemKinds: [
    { kind: 'infinitiveStem', description: 'The infinitive minus -en (lernen → lern) or minus -n for -eln/-ern verbs (lächeln → lächel). Default.' },
    { kind: 'infinitive', description: 'The whole infinitive (used by the wir/Sie imperative: "lernen wir", "lernen Sie", and by Futur I / Konjunktiv II after werden/würde).' },
    { kind: 'ending-template', description: 'An ending containing % is a template around the stem: Partizip II "ge%t" → ge+lern+t. Spelling rules may strip the ge- (prefix verbs, -ieren).' },
    { kind: 'separable', description: 'A verb in "separable" is split into prefix + base (anfangen → an + fangen). Finite forms conjugate the base and append the prefix ("fange an"); Partizip II is prefix + base participle ("angefangen"); compound tenses use the whole infinitive ("werde anfangen").' },
  ],
  rules: {
    classes: [
      { id: 'eln', name: '-eln / -ern', match: '(el|er)n$', strip: 1 },
      { id: 'en', name: '-en (weak)', match: 'en$', strip: 2 },
    ],
    endings: {
      en: { praesens: ['e', 'st', 't', 'en', 't', 'en'], praeteritum: WEAK_PRAT, partizip2: 'ge%t' },
      eln: { praesens: ['e', 'st', 't', 'n', 't', 'n'], praeteritum: WEAK_PRAT, partizip2: 'ge%t' },
    },
    stems: {},
    compose: {
      imperativ: [
        { literal: '—' },
        { stem: 'infinitiveStem', ending: '' },
        { literal: '—' },
        { stem: 'infinitive', ending: ' wir' },
        { form: 'praesens', person: 4 },
        { stem: 'infinitive', ending: ' Sie' },
      ],
    },
    compound: {
      perfekt: { aux: 'haben|sein', auxTense: 'praesens', with: 'partizip2', auxOf: { sein: SEIN_VERBS }, note: 'sein for verbs of movement / change of state (list); a separable verb inherits its base verb unless listed itself' },
      futur1: { aux: 'werden', auxTense: 'praesens', with: 'infinitive' },
      konjunktiv2: { aux: 'werden', auxTense: 'konjunktiv2', with: 'infinitive', note: 'würde + infinitive for regular verbs; sein/haben/werden, the modals, wissen and common strong verbs carry their own forms in irregulars' },
    },
    separablePrefixes: ['ab', 'an', 'auf', 'aus', 'bei', 'ein', 'fern', 'fest', 'fort', 'frei', 'her', 'hin', 'los', 'mit', 'nach', 'statt', 'teil', 'vor', 'weg', 'weiter', 'zu', 'zurück', 'zusammen',
      'heraus', 'herein', 'hinaus', 'hinein', 'herum', 'herunter', 'hinunter', 'hoch', 'um', 'durch', 'wieder', 'kennen', 'spazieren'],
    accentMap: {},
    spelling: [
      { id: 'e-insertion', when: 'stem ends in -t/-d, or in -m/-n after a consonant other than l/r/m/n/h (arbeiten → arbeitest, arbeitete; atmen, rechnen): an e is inserted before -st/-t/-te', classes: ['en'], match: E_INS.source, tenses: { praesens: [1, 2, 4], praeteritum: 'all' }, do: { ending: { from: '^(st|t)', to: 'e$1' } } },
      { id: 'e-insertion-partizip', when: 'same stems: Partizip II ends in -et (gearbeitet)', classes: ['en'], match: E_INS.source, tenses: { partizip2: [0] }, do: { ending: { from: 't$', to: 'et' } } },
      { id: 'e-insertion-imperativ', when: 'same stems: du-imperative keeps the -e (arbeite!)', classes: ['en'], match: E_INS.source, tenses: { imperativ: [1] }, do: { ending: { from: '^$', to: 'e' } } },
      { id: 'ieren/igen-imperativ', when: '-ieren and -igen verbs keep the -e in the du-imperative (studiere!, entschuldige!)', classes: ['en'], verbMatch: '(ieren|igen)$', tenses: { imperativ: [1] }, do: { ending: { from: '^$', to: 'e' } } },
      { id: 's-stem', when: 'stem ends in s/ß/z/x: du-form takes -t instead of -st (heißen → du heißt, tanzen → du tanzt)', classes: ['en'], match: '(s|ß|z|x)$', tenses: { praesens: [1] }, do: { ending: { from: '^st$', to: 't' } } },
      { id: 'eln-ich', when: '-eln verbs drop the e of the stem in the ich-form (lächeln → ich lächle)', classes: ['eln'], match: 'el$', tenses: { praesens: [0] }, do: { stem: { from: 'el$', to: 'l' } } },
      { id: 'eln-imperativ', when: '-eln verbs: du-imperative lächle!', classes: ['eln'], match: 'el$', tenses: { imperativ: [1] }, do: { stem: { from: 'el$', to: 'l' }, ending: { from: '^$', to: 'e' } } },
      { id: 'ern-imperativ', when: '-ern verbs: du-imperative wandere!', classes: ['eln'], match: 'er$', tenses: { imperativ: [1] }, do: { ending: { from: '^$', to: 'e' } } },
      { id: 'no-ge', when: 'inseparable prefix (be-, emp-, ent-, er-, ge-, miss-, ver-, zer-, über-, unter-, um-, durch-, wieder-, hinter-, wider-) or -ieren: Partizip II has no ge- (besucht, studiert)', verbMatch: '^(be|emp|ent|er|ge|miss|ver|zer|über|unter|um|durch|wieder|hinter|wider)[a-zäöüß]|ieren$', except: ['beten', 'betteln', 'bellen', 'bergen', 'enden', 'erben', 'ernten', 'entern', 'zerren', 'ekeln', 'eifern', 'gerben', 'verben', 'erden', 'geigen', 'wiegen', 'umarmen_'], tenses: { partizip2: [0] }, do: { ending: { from: '^ge', to: '' } } },
    ],
  },
  stemChangeKinds: {},
  stemChanging: {},
  separable: ['anfangen', 'aufstehen', 'ankommen', 'aufhören', 'einkaufen', 'aufmachen', 'zumachen', 'mitkommen', 'mitnehmen', 'mitbringen', 'abfahren', 'abholen', 'anrufen', 'ausgehen', 'aussehen',
    'aufwachen', 'einschlafen', 'zurückkommen', 'zurückgeben', 'teilnehmen', 'vorschlagen', 'vorbereiten', 'vorstellen', 'einladen', 'einsteigen', 'aussteigen', 'umsteigen', 'umziehen', 'anziehen', 'ausziehen',
    'fernsehen', 'vorlesen', 'ausgeben', 'zugeben', 'abgeben', 'angeben', 'stattfinden', 'weggehen', 'weitergehen', 'wegfahren', 'zuhören', 'zusehen', 'anmachen', 'ausmachen', 'einschalten', 'ausschalten',
    'ausfüllen', 'abschließen', 'anbieten', 'anhalten', 'aufpassen', 'aufräumen', 'aufwachsen', 'ausruhen', 'ausprobieren', 'einpacken', 'auspacken', 'nachdenken', 'nachschlagen', 'herstellen', 'feststellen',
    'festhalten', 'loslassen', 'losgehen', 'hinfallen', 'herkommen', 'hochladen', 'herunterladen', 'kennenlernen', 'spazierengehen', 'zurückfahren', 'zusammenarbeiten', 'einführen', 'durchführen', 'fortsetzen',
    'mitmachen', 'mitspielen', 'nachfragen', 'wegwerfen', 'abnehmen', 'zunehmen', 'annehmen', 'aufnehmen', 'aufbauen', 'abbauen', 'anbauen', 'einbauen', 'anschauen', 'ansehen', 'aufgeben', 'abbrechen',
    'aufschreiben', 'abschreiben', 'ankündigen', 'zurückkehren', 'auftauchen', 'auswandern', 'einwandern', 'eintreffen', 'auftreten', 'eintreten', 'abstürzen', 'ausfallen', 'einfallen', 'umfallen',
    'abreisen', 'aufbrechen', 'einziehen', 'beibringen', 'vorziehen', 'anerkennen', 'anfassen', 'anhaben', 'anklicken', 'anlegen', 'anmelden', 'abmelden', 'ausdrucken', 'ausleihen', 'ausschlafen',
    'aussprechen', 'austauschen', 'beitragen', 'einsetzen', 'einziehen_', 'festlegen', 'freilassen', 'hingehen', 'hinlegen', 'hinsetzen', 'mitteilen', 'nachgeben', 'nachholen', 'umdrehen', 'vorbeikommen',
    'vorhaben', 'vorkommen', 'vorlegen', 'wegnehmen', 'weitermachen', 'zubereiten', 'zugehen', 'zurücklegen', 'zusammenfassen', 'zusammenleben', 'zuschauen', 'durchfallen', 'wiedersehen'].filter(v => !v.endsWith('_')),
  irregulars: {},
  commonVerbs: [],
};
data.rules.spelling.find(r => r.id === 'no-ge').except = data.rules.spelling.find(r => r.id === 'no-ge').except.filter(v => !v.endsWith('_'));

const I = data.irregulars;

/** strong / mixed verb from a compact definition */
function S(v, d) {
  const stem = v.replace(/e?n$/, '');
  const e = {};
  if (d.pres) e.praesens = d.pres;
  else if (d.chg) {
    const c = d.chg;
    const sz = /(s|ß|z|x)$/.test(c);
    const t = /t$/.test(c);
    e.praesens = [stem + 'e', c + (sz ? 't' : 'st'), t ? c : c + 't', v, stem + (E_INS.test(stem) ? 'et' : 't'), v];
  }
  if (d.prat) {
    const p = d.prat;
    const en = p.endsWith('e') ? 'n' : 'en'; // schrie → schrien
    e.praeteritum = d.prat6 ?? [p, p + (/(d|t|s|ß|z|x)$/.test(p) ? 'est' : 'st'), p, p + en, p + (/(d|t)$/.test(p) ? 'et' : 't'), p + en];
  }
  if (d.prW) e.praeteritum = WEAK_PRAT.map(x => d.prW + x);
  if (d.prat6) e.praeteritum = d.prat6;
  e.partizip2 = d.pp;
  if (d.k2) e.konjunktiv2 = K2.map(x => d.k2 + x);
  if (d.k2full) e.konjunktiv2 = d.k2full;
  if (d.imp6) e.imperativ = d.imp6;
  else {
    const ihr = e.praesens ? e.praesens[4] : conjugate(data, v, 'praesens')[4];
    const du = d.imp ?? (stem + (E_INS.test(stem) ? 'e' : ''));
    e.imperativ = ['—', du, '—', v + ' wir', ihr, v + ' Sie'];
  }
  for (const k of ['perfekt', 'futur1']) if (d[k]) e[k] = d[k];
  I[v] = e;
}

S('sein', { pres: ['bin', 'bist', 'ist', 'sind', 'seid', 'sind'], prat6: ['war', 'warst', 'war', 'waren', 'wart', 'waren'], pp: 'gewesen',
  k2full: ['wäre', 'wärst', 'wäre', 'wären', 'wärt', 'wären'], imp6: ['—', 'sei', '—', 'seien wir', 'seid', 'seien Sie'] });
S('haben', { pres: ['habe', 'hast', 'hat', 'haben', 'habt', 'haben'], prW: 'hat', pp: 'gehabt', k2: 'hätt', imp6: ['—', 'hab', '—', 'haben wir', 'habt', 'haben Sie'] });
S('werden', { pres: ['werde', 'wirst', 'wird', 'werden', 'werdet', 'werden'], prat6: ['wurde', 'wurdest', 'wurde', 'wurden', 'wurdet', 'wurden'], pp: 'geworden', k2: 'würd',
  imp6: ['—', 'werde', '—', 'werden wir', 'werdet', 'werden Sie'] });
S('können', { pres: ['kann', 'kannst', 'kann', 'können', 'könnt', 'können'], prW: 'konn', pp: 'gekonnt', k2: 'könnt', imp6: DASH6 });
S('müssen', { pres: ['muss', 'musst', 'muss', 'müssen', 'müsst', 'müssen'], prW: 'muss', pp: 'gemusst', k2: 'müsst', imp6: DASH6 });
S('wollen', { pres: ['will', 'willst', 'will', 'wollen', 'wollt', 'wollen'], prW: 'woll', pp: 'gewollt', k2: 'wollt', imp6: DASH6 });
S('sollen', { pres: ['soll', 'sollst', 'soll', 'sollen', 'sollt', 'sollen'], prW: 'soll', pp: 'gesollt', k2: 'sollt', imp6: DASH6 });
S('dürfen', { pres: ['darf', 'darfst', 'darf', 'dürfen', 'dürft', 'dürfen'], prW: 'durf', pp: 'gedurft', k2: 'dürft', imp6: DASH6 });
S('mögen', { pres: ['mag', 'magst', 'mag', 'mögen', 'mögt', 'mögen'], prW: 'moch', pp: 'gemocht', k2: 'möcht', imp6: DASH6 });
S('wissen', { pres: ['weiß', 'weißt', 'weiß', 'wissen', 'wisst', 'wissen'], prW: 'wuss', pp: 'gewusst', k2: 'wüsst', imp6: ['—', 'wisse', '—', 'wissen wir', 'wisst', 'wissen Sie'] });
S('gehen', { prat: 'ging', pp: 'gegangen', k2: 'ging' });
S('kommen', { prat: 'kam', pp: 'gekommen', k2: 'käm' });
S('sehen', { chg: 'sieh', prat: 'sah', pp: 'gesehen', k2: 'säh', imp: 'sieh' });
S('geben', { chg: 'gib', prat: 'gab', pp: 'gegeben', k2: 'gäb', imp: 'gib' });
S('nehmen', { chg: 'nimm', prat: 'nahm', pp: 'genommen', k2: 'nähm', imp: 'nimm' });
S('tun', { pres: ['tue', 'tust', 'tut', 'tun', 'tut', 'tun'], prat: 'tat', pp: 'getan', k2: 'tät', imp6: ['—', 'tu', '—', 'tun wir', 'tut', 'tun Sie'] });
S('fahren', { chg: 'fähr', prat: 'fuhr', pp: 'gefahren', k2: 'führ' });
S('laufen', { chg: 'läuf', prat: 'lief', pp: 'gelaufen', k2: 'lief' });
S('lesen', { chg: 'lies', prat: 'las', pp: 'gelesen', k2: 'läs', imp: 'lies' });
S('essen', { chg: 'iss', prat: 'aß', pp: 'gegessen', k2: 'äß', imp: 'iss' });
S('trinken', { prat: 'trank', pp: 'getrunken', k2: 'tränk' });
S('finden', { prat: 'fand', pp: 'gefunden', k2: 'fänd' });
S('binden', { prat: 'band', pp: 'gebunden' });
S('sprechen', { chg: 'sprich', prat: 'sprach', pp: 'gesprochen', k2: 'spräch', imp: 'sprich' });
S('helfen', { chg: 'hilf', prat: 'half', pp: 'geholfen', imp: 'hilf' });
S('schlafen', { chg: 'schläf', prat: 'schlief', pp: 'geschlafen', k2: 'schlief' });
S('fallen', { chg: 'fäll', prat: 'fiel', pp: 'gefallen', k2: 'fiel' });
S('halten', { chg: 'hält', prat: 'hielt', pp: 'gehalten', k2: 'hielt' });
S('lassen', { chg: 'läss', prat: 'ließ', pp: 'gelassen', k2: 'ließ', imp: 'lass' });
S('tragen', { chg: 'träg', prat: 'trug', pp: 'getragen', k2: 'trüg' });
S('treffen', { chg: 'triff', prat: 'traf', pp: 'getroffen', k2: 'träf', imp: 'triff' });
S('bleiben', { prat: 'blieb', pp: 'geblieben', k2: 'blieb' });
S('schreiben', { prat: 'schrieb', pp: 'geschrieben', k2: 'schrieb' });
S('bringen', { prW: 'brach', pp: 'gebracht', k2: 'brächt' });
S('denken', { prW: 'dach', pp: 'gedacht', k2: 'dächt' });
S('kennen', { prW: 'kann', pp: 'gekannt' });
S('nennen', { prW: 'nann', pp: 'genannt' });
S('rennen', { prW: 'rann', pp: 'gerannt' });
S('brennen', { prW: 'brann', pp: 'gebrannt' });
S('stehen', { prat: 'stand', pp: 'gestanden', k2: 'stünd' });
S('sitzen', { prat: 'saß', pp: 'gesessen', k2: 'säß' });
S('liegen', { prat: 'lag', pp: 'gelegen', k2: 'läg' });
S('bitten', { prat: 'bat', pp: 'gebeten', k2: 'bät' });
S('beginnen', { prat: 'begann', pp: 'begonnen' });
S('gewinnen', { prat: 'gewann', pp: 'gewonnen' });
S('schwimmen', { prat: 'schwamm', pp: 'geschwommen' });
S('singen', { prat: 'sang', pp: 'gesungen' });
S('springen', { prat: 'sprang', pp: 'gesprungen' });
S('sinken', { prat: 'sank', pp: 'gesunken' });
S('klingen', { prat: 'klang', pp: 'geklungen' });
S('zwingen', { prat: 'zwang', pp: 'gezwungen' });
S('gelingen', { pres: ['—', '—', 'gelingt', '—', '—', '—'], prat6: ['—', '—', 'gelang', '—', '—', '—'], pp: 'gelungen', k2full: ['—', '—', 'gelänge', '—', '—', '—'], imp6: DASH6,
  perfekt: ['—', '—', 'ist gelungen', '—', '—', '—'], futur1: ['—', '—', 'wird gelingen', '—', '—', '—'] });
S('geschehen', { pres: ['—', '—', 'geschieht', '—', '—', '—'], prat6: ['—', '—', 'geschah', '—', '—', '—'], pp: 'geschehen', k2full: ['—', '—', 'geschähe', '—', '—', '—'], imp6: DASH6,
  perfekt: ['—', '—', 'ist geschehen', '—', '—', '—'], futur1: ['—', '—', 'wird geschehen', '—', '—', '—'] });
S('fliegen', { prat: 'flog', pp: 'geflogen' });
S('fliehen', { prat: 'floh', pp: 'geflohen' });
S('fließen', { prat: 'floss', pp: 'geflossen' });
S('ziehen', { prat: 'zog', pp: 'gezogen' });
S('bieten', { prat: 'bot', pp: 'geboten' });
S('verlieren', { prat: 'verlor', pp: 'verloren' });
S('schließen', { prat: 'schloss', pp: 'geschlossen' });
S('schieben', { prat: 'schob', pp: 'geschoben' });
S('genießen', { prat: 'genoss', pp: 'genossen' });
S('gießen', { prat: 'goss', pp: 'gegossen' });
S('frieren', { prat: 'fror', pp: 'gefroren' });
S('wiegen', { prat: 'wog', pp: 'gewogen' });
S('biegen', { prat: 'bog', pp: 'gebogen' });
S('lügen', { prat: 'log', pp: 'gelogen' });
S('betrügen', { prat: 'betrog', pp: 'betrogen' });
S('heben', { prat: 'hob', pp: 'gehoben' });
S('heißen', { prat: 'hieß', pp: 'geheißen', k2: 'hieß' });
S('rufen', { prat: 'rief', pp: 'gerufen', k2: 'rief' });
S('fangen', { chg: 'fäng', prat: 'fing', pp: 'gefangen', k2: 'fing' });
S('waschen', { chg: 'wäsch', prat: 'wusch', pp: 'gewaschen' });
S('wachsen', { chg: 'wächs', prat: 'wuchs', pp: 'gewachsen' });
S('schlagen', { chg: 'schläg', prat: 'schlug', pp: 'geschlagen', k2: 'schlüg' });
S('laden', { chg: 'läd', prat: 'lud', pp: 'geladen' });
S('raten', { chg: 'rät', prat: 'riet', pp: 'geraten' });
S('braten', { chg: 'brät', prat: 'briet', pp: 'gebraten' });
S('blasen', { chg: 'bläs', prat: 'blies', pp: 'geblasen' });
S('graben', { chg: 'gräb', prat: 'grub', pp: 'gegraben' });
S('stoßen', { chg: 'stöß', prat: 'stieß', pp: 'gestoßen' });
S('treten', { chg: 'tritt', prat: 'trat', pp: 'getreten', imp: 'tritt' });
S('vergessen', { chg: 'vergiss', prat: 'vergaß', pp: 'vergessen', imp: 'vergiss' });
S('messen', { chg: 'miss', prat: 'maß', pp: 'gemessen', imp: 'miss' });
S('werfen', { chg: 'wirf', prat: 'warf', pp: 'geworfen', imp: 'wirf' });
S('sterben', { chg: 'stirb', prat: 'starb', pp: 'gestorben', imp: 'stirb' });
S('brechen', { chg: 'brich', prat: 'brach', pp: 'gebrochen', imp: 'brich' });
S('empfehlen', { chg: 'empfiehl', prat: 'empfahl', pp: 'empfohlen', imp: 'empfiehl' });
S('stehlen', { chg: 'stiehl', prat: 'stahl', pp: 'gestohlen', imp: 'stiehl' });
S('befehlen', { chg: 'befiehl', prat: 'befahl', pp: 'befohlen', imp: 'befiehl' });
S('gelten', { chg: 'gilt', prat: 'galt', pp: 'gegolten', imp: 'gilt' });
S('stechen', { chg: 'stich', prat: 'stach', pp: 'gestochen', imp: 'stich' });
S('erschrecken', { chg: 'erschrick', prat: 'erschrak', pp: 'erschrocken', imp: 'erschrick' });
S('verderben', { chg: 'verdirb', prat: 'verdarb', pp: 'verdorben', imp: 'verdirb' });
S('werben', { chg: 'wirb', prat: 'warb', pp: 'geworben', imp: 'wirb' });
S('schmelzen', { chg: 'schmilz', prat: 'schmolz', pp: 'geschmolzen', imp: 'schmilz' });
S('steigen', { prat: 'stieg', pp: 'gestiegen' });
S('scheinen', { prat: 'schien', pp: 'geschienen' });
S('scheiden', { prat: 'schied', pp: 'geschieden' });
S('schneiden', { prat: 'schnitt', pp: 'geschnitten' });
S('leiden', { prat: 'litt', pp: 'gelitten' });
S('reiten', { prat: 'ritt', pp: 'geritten' });
S('greifen', { prat: 'griff', pp: 'gegriffen' });
S('streiten', { prat: 'stritt', pp: 'gestritten' });
S('pfeifen', { prat: 'pfiff', pp: 'gepfiffen' });
S('treiben', { prat: 'trieb', pp: 'getrieben' });
S('schreien', { prat: 'schrie', pp: 'geschrien' });
S('leihen', { prat: 'lieh', pp: 'geliehen' });
S('verzeihen', { prat: 'verzieh', pp: 'verziehen' });
S('meiden', { prat: 'mied', pp: 'gemieden' });
S('weisen', { prat: 'wies', pp: 'gewiesen' });
S('reißen', { prat: 'riss', pp: 'gerissen' });
S('beißen', { prat: 'biss', pp: 'gebissen' });
S('gleichen', { prat: 'glich', pp: 'geglichen' });
S('schweigen', { prat: 'schwieg', pp: 'geschwiegen' });
S('schwinden', { prat: 'schwand', pp: 'geschwunden' });
S('gefallen', { chg: 'gefäll', prat: 'gefiel', pp: 'gefallen', k2: 'gefiel' });
S('misslingen', { pres: ['—', '—', 'misslingt', '—', '—', '—'], prat6: ['—', '—', 'misslang', '—', '—', '—'], pp: 'misslungen', k2full: ['—', '—', 'misslänge', '—', '—', '—'], imp6: DASH6,
  perfekt: ['—', '—', 'ist misslungen', '—', '—', '—'], futur1: ['—', '—', 'wird misslingen', '—', '—', '—'] });

// inseparable-prefix derivatives (no ge- in the participle)
const D = (list, base) => { for (const [v, p] of list) derive(I, data, v, base, p, { noGe: true }); };
D([['verstehen', 'ver'], ['entstehen', 'ent'], ['bestehen', 'be'], ['gestehen', 'ge']], 'stehen');
D([['bekommen', 'be']], 'kommen');
D([['verlassen', 'ver'], ['entlassen', 'ent'], ['überlassen', 'über']], 'lassen');
D([['erhalten', 'er'], ['behalten', 'be'], ['enthalten', 'ent'], ['unterhalten', 'unter']], 'halten');
D([['entscheiden', 'ent'], ['unterscheiden', 'unter']], 'scheiden');
D([['beschreiben', 'be'], ['unterschreiben', 'unter']], 'schreiben');
D([['besitzen', 'be']], 'sitzen');
D([['verbieten', 'ver']], 'bieten');
D([['vergleichen', 'ver']], 'gleichen');
D([['erkennen', 'er']], 'kennen');
D([['verschwinden', 'ver']], 'schwinden');
D([['erscheinen', 'er']], 'scheinen');
D([['erziehen', 'er'], ['beziehen', 'be']], 'ziehen');
D([['empfangen', 'emp']], 'fangen');
D([['unternehmen', 'unter'], ['übernehmen', 'über']], 'nehmen');
D([['erfahren', 'er']], 'fahren');
D([['verbringen', 'ver']], 'bringen');
D([['versprechen', 'ver'], ['besprechen', 'be'], ['entsprechen', 'ent'], ['widersprechen', 'wider']], 'sprechen');
D([['beschließen', 'be']], 'schließen');
D([['empfinden', 'emp'], ['erfinden', 'er'], ['befinden', 'be']], 'finden');
D([['verbinden', 'ver']], 'binden');
D([['ergreifen', 'er'], ['begreifen', 'be']], 'greifen');
D([['betreffen', 'be']], 'treffen');
D([['beweisen', 'be']], 'weisen');
D([['bewerben', 'be']], 'werben');
D([['geraten', 'ge'], ['beraten', 'be'], ['verraten', 'ver']], 'raten');
D([['verschlafen', 'ver']], 'schlafen');
D([['vertreten', 'ver'], ['betreten', 'be']], 'treten');
D([['übertragen', 'über']], 'tragen');
D([['ergeben', 'er'], ['vergeben', 'ver'], ['umgeben', 'um']], 'geben');
D([['verhalten', 'ver']], 'halten');
D([['unterliegen', 'unter'], ['erliegen', 'er']], 'liegen');
D([['vergehen', 'ver'], ['begehen', 'be'], ['umgehen', 'um']], 'gehen');
D([['versinken', 'ver']], 'sinken');
D([['erheben', 'er']], 'heben');
D([['verwerfen', 'ver'], ['entwerfen', 'ent']], 'werfen');
D([['verschließen', 'ver']], 'schließen');

data.commonVerbs = [
  'sein', 'haben', 'werden', 'können', 'müssen', 'sagen', 'machen', 'geben', 'kommen', 'sollen', 'wollen', 'gehen', 'wissen', 'sehen', 'lassen', 'stehen', 'finden', 'bleiben', 'liegen', 'heißen',
  'denken', 'nehmen', 'tun', 'dürfen', 'glauben', 'halten', 'nennen', 'mögen', 'zeigen', 'führen', 'sprechen', 'bringen', 'leben', 'fahren', 'meinen', 'fragen', 'kennen', 'gelten', 'stellen', 'spielen',
  'arbeiten', 'brauchen', 'folgen', 'lernen', 'bestehen', 'verstehen', 'setzen', 'bekommen', 'beginnen', 'erzählen', 'versuchen', 'schreiben', 'laufen', 'erklären', 'entsprechen', 'sitzen', 'ziehen', 'scheinen', 'fallen', 'gehören',
  'entstehen', 'erhalten', 'treffen', 'suchen', 'legen', 'vorstellen', 'handeln', 'erreichen', 'tragen', 'schaffen', 'lesen', 'verlieren', 'erkennen', 'entwickeln', 'reden', 'aussehen', 'erscheinen', 'bilden', 'anfangen', 'erwarten',
  'wohnen', 'betreffen', 'warten', 'vergehen', 'helfen', 'gewinnen', 'schließen', 'fühlen', 'bieten', 'interessieren', 'erinnern', 'ergeben', 'anbieten', 'studieren', 'verbinden', 'ansehen', 'fehlen', 'bedeuten', 'vergleichen', 'verlassen',
  'ausgehen', 'erfahren', 'kaufen', 'trinken', 'essen', 'schlafen', 'hören', 'zahlen', 'bezahlen', 'öffnen', 'antworten', 'lieben', 'kosten', 'lachen', 'weinen', 'singen', 'tanzen', 'reisen', 'fliegen', 'schwimmen',
  'rennen', 'springen', 'steigen', 'einsteigen', 'aussteigen', 'umsteigen', 'ankommen', 'abfahren', 'aufstehen', 'einschlafen', 'aufwachen', 'duschen', 'waschen', 'anziehen', 'ausziehen', 'frühstücken', 'kochen', 'schneiden', 'schmecken', 'probieren',
  'bestellen', 'holen', 'abholen', 'mitbringen', 'einkaufen', 'besuchen', 'einladen', 'feiern', 'gratulieren', 'schenken', 'danken', 'bitten', 'wünschen', 'hoffen', 'träumen', 'vergessen', 'üben', 'unterrichten', 'wiederholen', 'verbessern',
  'korrigieren', 'prüfen', 'wählen', 'entscheiden', 'planen', 'organisieren', 'vorbereiten', 'teilnehmen', 'stattfinden', 'aufhören', 'enden', 'dauern', 'kennenlernen', 'heiraten', 'umziehen', 'mieten', 'vermieten', 'bauen', 'renovieren', 'putzen',
  'aufräumen', 'reparieren', 'benutzen', 'verwenden', 'verkaufen', 'sparen', 'verdienen', 'ausgeben', 'leihen', 'bewerben', 'kündigen', 'leiten', 'verwalten', 'verhandeln', 'liefern', 'produzieren', 'herstellen', 'forschen', 'erfinden', 'entdecken',
  'messen', 'zählen', 'rechnen', 'berechnen', 'schätzen', 'vermuten', 'begreifen', 'bemerken', 'beobachten', 'betrachten', 'anschauen', 'zuhören', 'zusehen', 'fernsehen', 'vorlesen', 'beschreiben', 'berichten', 'melden', 'informieren', 'mitteilen',
  'diskutieren', 'streiten', 'lügen', 'schweigen', 'rufen', 'anrufen', 'schreien', 'flüstern', 'befehlen', 'erlauben', 'verbieten', 'empfehlen', 'raten', 'warnen', 'drohen', 'versprechen', 'behaupten', 'zugeben', 'leugnen', 'bestätigen',
  'ablehnen', 'zustimmen', 'widersprechen', 'überzeugen', 'beeinflussen', 'ändern', 'verändern', 'wechseln', 'verschwinden', 'verstecken', 'kämpfen', 'siegen', 'schlagen', 'werfen', 'fangen', 'heben', 'hängen', 'schieben', 'drücken', 'drehen',
  'brechen', 'zerstören', 'gelingen', 'scheitern', 'passieren', 'geschehen', 'enthalten', 'benötigen', 'reichen', 'genügen', 'passen', 'stimmen', 'klingen', 'riechen', 'spüren', 'leiden', 'schmerzen', 'bluten', 'heilen', 'sterben',
  'töten', 'retten', 'schützen', 'pflegen', 'sorgen', 'kümmern', 'unterstützen', 'fördern', 'hindern', 'stören', 'ärgern', 'freuen', 'wundern', 'fürchten', 'erschrecken', 'beruhigen', 'entspannen', 'genießen', 'lächeln', 'grüßen',
  'verabschieden', 'bedanken', 'entschuldigen', 'verzeihen', 'vertrauen', 'zweifeln', 'überlegen', 'nachdenken', 'merken', 'lehren', 'übersetzen', 'beweisen', 'ergänzen', 'trennen', 'teilen', 'mischen', 'sammeln', 'ordnen', 'sortieren', 'speichern',
  'löschen', 'drucken', 'kopieren', 'schicken', 'empfangen', 'kriegen', 'zurückgeben', 'tauschen', 'verteilen', 'binden', 'biegen', 'blasen', 'braten', 'graben', 'stoßen', 'treten', 'stehlen', 'stechen', 'verderben',
  'werben', 'schmelzen', 'scheiden', 'reiten', 'greifen', 'pfeifen', 'treiben', 'meiden', 'weisen', 'reißen', 'beißen', 'gleichen', 'schwinden', 'gefallen', 'misslingen', 'fliehen', 'fließen', 'gießen', 'frieren', 'wiegen',
  'betrügen', 'zwingen', 'sinken', 'brennen', 'bestehen_', 'gestehen', 'entlassen', 'überlassen', 'behalten', 'unterhalten', 'unterscheiden', 'unterschreiben', 'besitzen', 'erziehen', 'beziehen', 'unternehmen', 'übernehmen', 'verbringen', 'besprechen', 'beschließen',
  'empfinden', 'befinden', 'ergreifen', 'beraten', 'verraten', 'verschlafen', 'vertreten', 'betreten', 'übertragen', 'vergeben', 'umgeben', 'verhalten', 'unterliegen', 'erliegen', 'begehen', 'umgehen', 'versinken', 'erheben', 'verwerfen', 'verschließen',
  'entwerfen', 'geraten', 'mitnehmen', 'mitkommen', 'zumachen', 'aufmachen', 'zurückkommen', 'vorschlagen', 'weggehen', 'weitergehen', 'wegfahren', 'anmachen', 'ausmachen', 'einschalten', 'ausschalten', 'ausfüllen', 'abschließen', 'anhalten', 'aufpassen', 'aufwachsen',
  'ausruhen', 'ausprobieren', 'einpacken', 'auspacken', 'nachschlagen', 'feststellen', 'festhalten', 'loslassen', 'losgehen', 'hinfallen', 'herkommen', 'hochladen', 'herunterladen', 'spazierengehen', 'zurückfahren', 'zusammenarbeiten', 'einführen', 'durchführen', 'fortsetzen', 'mitmachen',
  'mitspielen', 'nachfragen', 'wegwerfen', 'abnehmen', 'zunehmen', 'annehmen', 'aufnehmen', 'aufbauen', 'abbauen', 'anbauen', 'einbauen', 'aufgeben', 'abbrechen', 'aufschreiben', 'abschreiben', 'ankündigen', 'zurückkehren', 'auftauchen', 'auswandern', 'einwandern',
  'eintreffen', 'auftreten', 'eintreten', 'abstürzen', 'ausfallen', 'einfallen', 'umfallen', 'abreisen', 'aufbrechen', 'einziehen', 'beibringen', 'vorziehen', 'anerkennen', 'anfassen', 'anhaben', 'anklicken', 'anlegen', 'anmelden', 'abmelden', 'ausdrucken',
  'ausleihen', 'ausschlafen', 'aussprechen', 'austauschen', 'beitragen', 'einsetzen', 'festlegen', 'freilassen', 'hingehen', 'hinlegen', 'hinsetzen', 'nachgeben', 'nachholen', 'umdrehen', 'vorbeikommen', 'vorhaben', 'vorkommen', 'vorlegen', 'wegnehmen', 'weitermachen',
  'zubereiten', 'zugehen', 'zurücklegen', 'zusammenfassen', 'zusammenleben', 'zuschauen', 'durchfallen', 'wiedersehen', 'abgeben', 'angeben', 'atmen', 'baden', 'begegnen', 'begleiten', 'behandeln', 'beobachten_', 'beschäftigen', 'bestimmen', 'beten', 'bewegen',
  'bezeichnen', 'bremsen', 'buchen', 'danken_', 'decken', 'dienen', 'drohen_', 'eilen', 'erben', 'ernten', 'erzeugen', 'fassen', 'fehlen_', 'fertigen', 'fischen', 'fixen_', 'flüchten', 'füllen', 'fürchten_', 'gähnen',
  'gründen', 'gucken', 'handeln_', 'hassen', 'hüten', 'impfen', 'irren', 'jagen', 'jubeln', 'kleben', 'klettern', 'klingeln', 'klopfen', 'kratzen', 'küssen', 'lagern', 'landen', 'lauschen', 'lehnen',
  'leisten', 'lenken', 'loben', 'lohnen', 'malen', 'mahlen_', 'mangeln', 'meistern', 'mixen', 'nähen', 'nicken', 'nützen', 'packen', 'parken', 'pfeifen_', 'pflanzen', 'plaudern', 'prägen', 'quälen', 'rasieren',
  'rauchen', 'räumen', 'regnen', 'reiben_', 'reinigen', 'reizen', 'retten_', 'richten', 'rollen', 'rühren', 'rutschen', 'sägen', 'schaden', 'schalten', 'schämen', 'schauen', 'schimpfen', 'schmücken', 'schütteln', 'schwitzen',
  'segeln', 'seufzen', 'sichern', 'siezen', 'sparen_', 'spülen', 'starten', 'staunen', 'stecken', 'stolpern', 'stürzen', 'surfen', 'tanken', 'tasten', 'tippen', 'trainieren', 'trauen', 'trocknen', 'üben_', 'verletzen',
  'verpassen', 'verreisen', 'wachen', 'wagen', 'wandern', 'wecken', 'wehren', 'weihen', 'widmen', 'wirken', 'wohnen_', 'zahlen_', 'zeichnen', 'zelten', 'zögern', 'zucken', 'zweifeln_', 'explodieren', 'gelangen', 'erfolgen',
  'joggen', 'verunglücken', 'erlesen_',
].filter(v => !v.endsWith('_'));

finish(data, ['sein', 'haben', 'werden', 'können', 'müssen', 'wollen', 'sollen', 'dürfen', 'mögen', 'wissen', 'gehen', 'kommen', 'sehen', 'geben', 'nehmen']);
