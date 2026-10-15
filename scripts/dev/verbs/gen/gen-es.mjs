// Spanish (Spain, with vosotros) — generator for es.json
import { derive, finish } from './common.mjs';

const T = {
  presente: 0, preterito: 0, imperfecto: 0, futuro: 0, condicional: 0, presenteSubj: 0, imperfectoSubj: 0, imperativo: 0, participio: 0, gerundio: 0,
};

const FUT = ['é', 'ás', 'á', 'emos', 'éis', 'án'];
const COND = ['ía', 'ías', 'ía', 'íamos', 'íais', 'ían'];
const fut = s => FUT.map(e => s + e);
const cond = s => COND.map(e => s + e);
// strong preterite: tuv- → tuve tuviste tuvo tuvimos tuvisteis tuvieron ; j-stems: dij- → dijeron
const strong = (s, j = false) => [s + 'e', s + 'iste', s + 'o', s + 'imos', s + 'isteis', s + (j ? 'eron' : 'ieron')];
const subjA = s => ['a', 'as', 'a', 'amos', 'áis', 'an'].map(e => s + e); // -er/-ir subjunctive from a uniform stem
const imp = (tu, s) => ['—', tu, s + 'a', s + 'amos', null, s + 'an']; // null → engine (vosotros)

const data = {
  language: 'es',
  name: 'Spanish',
  pronouns: ['yo', 'tú', 'él/ella/usted', 'nosotros', 'vosotros', 'ellos/ellas/ustedes'],
  imperativePronouns: ['—', 'tú', 'usted', 'nosotros', 'vosotros', 'ustedes'],
  tenses: [
    { id: 'presente', name: 'Presente', group: 'indicativo' },
    { id: 'preterito', name: 'Pretérito indefinido', group: 'indicativo' },
    { id: 'imperfecto', name: 'Pretérito imperfecto', group: 'indicativo' },
    { id: 'futuro', name: 'Futuro', group: 'indicativo' },
    { id: 'condicional', name: 'Condicional', group: 'condicional' },
    { id: 'presenteSubj', name: 'Presente de subjuntivo', group: 'subjuntivo' },
    { id: 'imperfectoSubj', name: 'Imperfecto de subjuntivo', group: 'subjuntivo' },
    { id: 'imperativo', name: 'Imperativo', group: 'imperativo' },
    { id: 'participio', name: 'Participio', group: 'nonfinite', single: true },
    { id: 'gerundio', name: 'Gerundio', group: 'nonfinite', single: true },
  ],
  groupNames: { indicativo: 'Indicativo', subjuntivo: 'Subjuntivo', imperativo: 'Imperativo', condicional: 'Condicional', nonfinite: 'Formas no personales' },
  stemKinds: [
    { kind: 'infinitiveStem', description: 'The infinitive minus its class ending (hablar → habl). Default when a tense lists no stem. Stem changes apply only to this kind.' },
    { kind: 'infinitive', description: 'The whole infinitive (hablar → hablar + é = hablaré).' },
    { kind: 'infinitiveMinusR', description: 'The infinitive without its final r (hablar → habla + d = hablad).' },
    { kind: 'form', description: 'Another (already conjugated, irregular-aware) form of the same verb with a suffix stripped: tense + person + strip. E.g. imperfectoSubj uses preterito[5] minus "ron" (tuvieron → tuvie + ra).' },
    { kind: 'accent-marker', description: 'An ending that starts with ´ (U+00B4) puts an acute accent on the last vowel of the stem before being appended (tuvie + ´ramos → tuviéramos), using rules.accentMap.' },
  ],
  rules: {
    classes: [
      { id: 'ar', name: '-ar', match: 'ar$', strip: 2 },
      { id: 'er', name: '-er', match: 'er$', strip: 2 },
      { id: 'ir', name: '-ir', match: '[ií]r$', strip: 2 },
    ],
    endings: {
      ar: {
        presente: ['o', 'as', 'a', 'amos', 'áis', 'an'],
        preterito: ['é', 'aste', 'ó', 'amos', 'asteis', 'aron'],
        imperfecto: ['aba', 'abas', 'aba', 'ábamos', 'abais', 'aban'],
        futuro: FUT, condicional: COND,
        presenteSubj: ['e', 'es', 'e', 'emos', 'éis', 'en'],
        imperfectoSubj: ['ra', 'ras', 'ra', '´ramos', 'rais', 'ran'],
        participio: 'ado', gerundio: 'ando',
      },
      er: {
        presente: ['o', 'es', 'e', 'emos', 'éis', 'en'],
        preterito: ['í', 'iste', 'ió', 'imos', 'isteis', 'ieron'],
        imperfecto: ['ía', 'ías', 'ía', 'íamos', 'íais', 'ían'],
        futuro: FUT, condicional: COND,
        presenteSubj: ['a', 'as', 'a', 'amos', 'áis', 'an'],
        imperfectoSubj: ['ra', 'ras', 'ra', '´ramos', 'rais', 'ran'],
        participio: 'ido', gerundio: 'iendo',
      },
      ir: {
        presente: ['o', 'es', 'e', 'imos', 'ís', 'en'],
        preterito: ['í', 'iste', 'ió', 'imos', 'isteis', 'ieron'],
        imperfecto: ['ía', 'ías', 'ía', 'íamos', 'íais', 'ían'],
        futuro: FUT, condicional: COND,
        presenteSubj: ['a', 'as', 'a', 'amos', 'áis', 'an'],
        imperfectoSubj: ['ra', 'ras', 'ra', '´ramos', 'rais', 'ran'],
        participio: 'ido', gerundio: 'iendo',
      },
    },
    stems: {
      futuro: { kind: 'infinitive' },
      condicional: { kind: 'infinitive' },
      imperfectoSubj: { kind: 'form', tense: 'preterito', person: 5, strip: 'ron' },
    },
    compose: {
      imperativo: [
        { literal: '—' },
        { form: 'presente', person: 2 },
        { form: 'presenteSubj', person: 2 },
        { form: 'presenteSubj', person: 3 },
        { stem: 'infinitiveMinusR', ending: 'd' },
        { form: 'presenteSubj', person: 5 },
      ],
    },
    compound: {},
    accentMap: { a: 'á', e: 'é', i: 'í', o: 'ó', u: 'ú' },
    raisedStemPersons: { presenteSubj: [3, 4], preterito: [2, 5], gerundio: [0] },
    spelling: [
      { id: 'car->qu', when: 'stem ends in c (-car) and the ending starts with e', classes: ['ar'], match: 'c$', tenses: { preterito: [0], presenteSubj: 'all' }, do: { stem: { from: 'c$', to: 'qu' } } },
      { id: 'gar->gu', when: 'stem ends in g (-gar) and the ending starts with e', classes: ['ar'], match: 'g$', tenses: { preterito: [0], presenteSubj: 'all' }, do: { stem: { from: 'g$', to: 'gu' } } },
      { id: 'zar->c', when: 'stem ends in z (-zar) and the ending starts with e', classes: ['ar'], match: 'z$', tenses: { preterito: [0], presenteSubj: 'all' }, do: { stem: { from: 'z$', to: 'c' } } },
      { id: 'guar->gü', when: 'stem ends in gu (-guar) and the ending starts with e', classes: ['ar'], match: 'gu$', tenses: { preterito: [0], presenteSubj: 'all' }, do: { stem: { from: 'gu$', to: 'gü' } } },
      { id: 'vowel+cer->zc', when: '-cer/-cir after a vowel before o/a (conocer → conozco)', classes: ['er', 'ir'], match: '[aeiou]c$', except: ['cocer', 'mecer', 'escocer', 'hacer', 'decir'], tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'c$', to: 'zc' } } },
      { id: 'cons+cer->z', when: '-cer/-cir after a consonant before o/a (vencer → venzo)', classes: ['er', 'ir'], match: '[^aeiou]c$', tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'c$', to: 'z' } } },
      { id: 'cocer->z', when: 'cocer/mecer family: c → z before o/a', classes: ['er'], verbs: ['cocer', 'mecer', 'escocer'], match: 'c$', tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'c$', to: 'z' } } },
      { id: 'ger->j', when: '-ger/-gir before o/a (coger → cojo)', classes: ['er', 'ir'], match: 'g$', tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'g$', to: 'j' } } },
      { id: 'guir->g', when: '-guir before o/a (seguir → sigo)', classes: ['ir'], match: 'gu$', tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'gu$', to: 'g' } } },
      { id: 'uir->uy', when: '-uir inserts y before o/e/a (construir → construyo)', classes: ['ir'], match: '[^gq]u$', tenses: { presente: [0, 1, 2, 5], presenteSubj: 'all' }, do: { stem: { from: 'u$', to: 'uy' } } },
      { id: 'i->y', when: 'stem ends in a vowel: unstressed i between vowels becomes y (leer → leyó, leyendo)', classes: ['er', 'ir'], match: '(^|[^gq])[aeiou]$', tenses: { preterito: [2, 5], gerundio: [0] }, do: { ending: { from: '^i', to: 'y' } } },
      { id: 'ír->ir', when: '-ír infinitives lose the accent when the futuro/condicional endings are attached (oír → oiré, reír → reiría)', classes: ['ir'], match: 'ír$', tenses: { futuro: 'all', condicional: 'all' }, do: { stem: { from: 'ír$', to: 'ir' } } },
      { id: 'i->í', when: 'stem ends in a/e/o: i of the ending takes an accent (leer → leíste, leído)', classes: ['er', 'ir'], match: '[aeo]$', tenses: { preterito: [1, 3, 4], participio: [0] }, do: { ending: { from: '^i', to: 'í' } } },
    ],
  },
  stemChangeKinds: {
    'e>ie': { from: 'e', to: 'ie', description: 'last e of the stem → ie in the boot persons (yo, tú, él, ellos) of presente and presente de subjuntivo', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
    'o>ue': { from: 'o', to: 'ue', description: 'last o of the stem → ue in the boot persons', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
    'u>ue': { from: 'u', to: 'ue', description: 'u → ue in the boot persons (jugar)', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
    'e>i': { from: 'e', to: 'i', description: 'last e → i in the boot persons (-ir verbs; the raisedStem covers the other persons)', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
    'i>ie': { from: 'i', to: 'ie', description: 'i → ie in the boot persons (adquirir)', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
    'i>í': { from: 'i', to: 'í', description: 'stressed i takes an accent in the boot persons (enviar → envío)', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
    'u>ú': { from: 'u', to: 'ú', description: 'stressed u takes an accent in the boot persons (continuar → continúo, reunir → reúno)', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
  },
  stemChanging: {},
  separable: [],
  irregulars: {},
  commonVerbs: [],
};

// ── stem-changing verbs ──
const sc = (kind, verbs, raised) => { for (const v of verbs) data.stemChanging[v] = raised ? { kind, raisedStem: raised[v] } : { kind }; };
sc('e>ie', ['pensar', 'empezar', 'comenzar', 'cerrar', 'entender', 'perder', 'despertar', 'sentar', 'recomendar', 'calentar', 'confesar', 'gobernar', 'negar', 'nevar', 'temblar', 'tropezar', 'atravesar', 'encender', 'extender', 'ascender', 'descender', 'tender', 'atender', 'defender', 'querer', 'manifestar', 'apretar', 'helar', 'merendar', 'quebrar', 'regar', 'sembrar', 'acertar', 'encerrar', 'fregar', 'plegar', 'enterrar', 'desterrar', 'comentar_']);
delete data.stemChanging['comentar_'];
delete data.stemChanging['querer']; // fully explicit below
sc('e>ie', ['sentir', 'mentir', 'preferir', 'divertir', 'convertir', 'advertir', 'herir', 'hervir', 'referir', 'sugerir', 'transferir', 'consentir', 'presentir', 'arrepentir', 'diferir', 'inferir', 'digerir', 'adherir'],
  { sentir: 'sint', mentir: 'mint', preferir: 'prefir', divertir: 'divirt', convertir: 'convirt', advertir: 'advirt', herir: 'hir', hervir: 'hirv', referir: 'refir', sugerir: 'sugir', transferir: 'transfir', consentir: 'consint', presentir: 'presint', arrepentir: 'arrepint', diferir: 'difir', inferir: 'infir', digerir: 'digir', adherir: 'adhir' });
sc('o>ue', ['contar', 'encontrar', 'recordar', 'mostrar', 'acostar', 'almorzar', 'volar', 'soñar', 'sonar', 'probar', 'aprobar', 'comprobar', 'volver', 'devolver', 'resolver', 'envolver', 'mover', 'llover', 'morder', 'doler', 'soler', 'colgar', 'rogar', 'forzar', 'costar', 'acordar', 'rodar', 'demostrar', 'renovar', 'torcer', 'cocer', 'poder_', 'absolver', 'disolver', 'revolver', 'conmover', 'promover', 'remover', 'tostar', 'apostar', 'esforzar', 'reforzar', 'avergonzar', 'consolar', 'contar_', 'descontar', 'recontar', 'sobrevolar', 'oler_']);
for (const k of ['poder_', 'contar_', 'oler_']) delete data.stemChanging[k];
sc('o>ue', ['dormir', 'morir'], { dormir: 'durm', morir: 'mur' });
sc('u>ue', ['jugar']);
sc('e>i', ['pedir', 'servir', 'seguir', 'conseguir', 'perseguir', 'repetir', 'vestir', 'medir', 'despedir', 'impedir', 'elegir', 'corregir', 'competir', 'rendir', 'gemir', 'concebir', 'derretir', 'expedir', 'investir', 'desvestir', 'proseguir'],
  { pedir: 'pid', servir: 'sirv', seguir: 'sigu', conseguir: 'consigu', perseguir: 'persigu', repetir: 'repit', vestir: 'vist', medir: 'mid', despedir: 'despid', impedir: 'impid', elegir: 'elig', corregir: 'corrig', competir: 'compit', rendir: 'rind', gemir: 'gim', concebir: 'concib', derretir: 'derrit', expedir: 'expid', investir: 'invist', desvestir: 'desvist', proseguir: 'prosigu' });
sc('i>ie', ['adquirir', 'inquirir']);
sc('i>í', ['enviar', 'confiar', 'guiar', 'variar', 'criar', 'fiar', 'esquiar', 'enfriar', 'vaciar', 'espiar', 'ampliar', 'desviar', 'prohibir', 'reunir_']);
delete data.stemChanging['reunir_'];
sc('u>ú', ['continuar', 'actuar', 'evaluar', 'graduar', 'situar', 'acentuar', 'reunir', 'efectuar', 'habituar', 'insinuar', 'valuar']);

// ── irregular verbs (explicit pieces; the rest is filled by the engine and pruned) ──
const I = data.irregulars;
I.ser = {
  presente: ['soy', 'eres', 'es', 'somos', 'sois', 'son'],
  preterito: ['fui', 'fuiste', 'fue', 'fuimos', 'fuisteis', 'fueron'],
  imperfecto: ['era', 'eras', 'era', 'éramos', 'erais', 'eran'],
  presenteSubj: ['sea', 'seas', 'sea', 'seamos', 'seáis', 'sean'],
  imperativo: ['—', 'sé', 'sea', 'seamos', 'sed', 'sean'],
};
I.estar = {
  presente: ['estoy', 'estás', 'está', 'estamos', 'estáis', 'están'],
  preterito: strong('estuv'),
  presenteSubj: ['esté', 'estés', 'esté', 'estemos', 'estéis', 'estén'],
  imperativo: ['—', 'está', 'esté', 'estemos', 'estad', 'estén'],
};
I.ir = {
  presente: ['voy', 'vas', 'va', 'vamos', 'vais', 'van'],
  preterito: ['fui', 'fuiste', 'fue', 'fuimos', 'fuisteis', 'fueron'],
  imperfecto: ['iba', 'ibas', 'iba', 'íbamos', 'ibais', 'iban'],
  presenteSubj: ['vaya', 'vayas', 'vaya', 'vayamos', 'vayáis', 'vayan'],
  imperativo: ['—', 've', 'vaya', 'vamos', 'id', 'vayan'],
  gerundio: 'yendo',
};
I.haber = {
  presente: ['he', 'has', 'ha', 'hemos', 'habéis', 'han'],
  preterito: strong('hub'), futuro: fut('habr'), condicional: cond('habr'),
  presenteSubj: ['haya', 'hayas', 'haya', 'hayamos', 'hayáis', 'hayan'],
  imperativo: ['—', 'he', 'haya', 'hayamos', 'habed', 'hayan'],
};
I.tener = {
  presente: ['tengo', 'tienes', 'tiene', 'tenemos', 'tenéis', 'tienen'],
  preterito: strong('tuv'), futuro: fut('tendr'), condicional: cond('tendr'),
  presenteSubj: subjA('teng'), imperativo: ['—', 'ten', 'tenga', 'tengamos', 'tened', 'tengan'],
};
I.hacer = {
  presente: ['hago', 'haces', 'hace', 'hacemos', 'hacéis', 'hacen'],
  preterito: ['hice', 'hiciste', 'hizo', 'hicimos', 'hicisteis', 'hicieron'],
  futuro: fut('har'), condicional: cond('har'),
  presenteSubj: subjA('hag'), imperativo: ['—', 'haz', 'haga', 'hagamos', 'haced', 'hagan'], participio: 'hecho',
};
I.poder = {
  presente: ['puedo', 'puedes', 'puede', 'podemos', 'podéis', 'pueden'],
  preterito: strong('pud'), futuro: fut('podr'), condicional: cond('podr'),
  presenteSubj: ['pueda', 'puedas', 'pueda', 'podamos', 'podáis', 'puedan'],
  imperativo: ['—', 'puede', 'pueda', 'podamos', 'poded', 'puedan'], gerundio: 'pudiendo',
};
I.decir = {
  presente: ['digo', 'dices', 'dice', 'decimos', 'decís', 'dicen'],
  preterito: strong('dij', true), futuro: fut('dir'), condicional: cond('dir'),
  presenteSubj: subjA('dig'), imperativo: ['—', 'di', 'diga', 'digamos', 'decid', 'digan'], participio: 'dicho', gerundio: 'diciendo',
};
I.querer = {
  presente: ['quiero', 'quieres', 'quiere', 'queremos', 'queréis', 'quieren'],
  preterito: strong('quis'), futuro: fut('querr'), condicional: cond('querr'),
  presenteSubj: ['quiera', 'quieras', 'quiera', 'queramos', 'queráis', 'quieran'],
  imperativo: ['—', 'quiere', 'quiera', 'queramos', 'quered', 'quieran'],
};
I.saber = {
  presente: ['sé', 'sabes', 'sabe', 'sabemos', 'sabéis', 'saben'],
  preterito: strong('sup'), futuro: fut('sabr'), condicional: cond('sabr'),
  presenteSubj: ['sepa', 'sepas', 'sepa', 'sepamos', 'sepáis', 'sepan'],
  imperativo: ['—', 'sabe', 'sepa', 'sepamos', 'sabed', 'sepan'],
};
I.poner = {
  presente: ['pongo', 'pones', 'pone', 'ponemos', 'ponéis', 'ponen'],
  preterito: strong('pus'), futuro: fut('pondr'), condicional: cond('pondr'),
  presenteSubj: subjA('pong'), imperativo: ['—', 'pon', 'ponga', 'pongamos', 'poned', 'pongan'], participio: 'puesto',
};
I.venir = {
  presente: ['vengo', 'vienes', 'viene', 'venimos', 'venís', 'vienen'],
  preterito: strong('vin'), futuro: fut('vendr'), condicional: cond('vendr'),
  presenteSubj: subjA('veng'), imperativo: ['—', 'ven', 'venga', 'vengamos', 'venid', 'vengan'], gerundio: 'viniendo',
};
I.ver = {
  presente: ['veo', 'ves', 've', 'vemos', 'veis', 'ven'],
  preterito: ['vi', 'viste', 'vio', 'vimos', 'visteis', 'vieron'],
  imperfecto: ['veía', 'veías', 'veía', 'veíamos', 'veíais', 'veían'],
  presenteSubj: subjA('ve'), imperativo: ['—', 've', 'vea', 'veamos', 'ved', 'vean'], participio: 'visto',
};
I.dar = {
  presente: ['doy', 'das', 'da', 'damos', 'dais', 'dan'],
  preterito: ['di', 'diste', 'dio', 'dimos', 'disteis', 'dieron'],
  presenteSubj: ['dé', 'des', 'dé', 'demos', 'deis', 'den'],
  imperativo: ['—', 'da', 'dé', 'demos', 'dad', 'den'],
};
I.salir = {
  presente: ['salgo', 'sales', 'sale', 'salimos', 'salís', 'salen'],
  futuro: fut('saldr'), condicional: cond('saldr'),
  presenteSubj: subjA('salg'), imperativo: ['—', 'sal', 'salga', 'salgamos', 'salid', 'salgan'],
};
I.andar = { preterito: strong('anduv') };
I.caber = {
  presente: ['quepo', 'cabes', 'cabe', 'cabemos', 'cabéis', 'caben'],
  preterito: strong('cup'), futuro: fut('cabr'), condicional: cond('cabr'),
  presenteSubj: subjA('quep'), imperativo: ['—', 'cabe', 'quepa', 'quepamos', 'cabed', 'quepan'],
};
I.caer = {
  presente: ['caigo', 'caes', 'cae', 'caemos', 'caéis', 'caen'],
  presenteSubj: subjA('caig'), imperativo: ['—', 'cae', 'caiga', 'caigamos', 'caed', 'caigan'],
};
for (const v of ['conducir', 'traducir', 'producir', 'reducir', 'introducir', 'deducir', 'seducir']) {
  I[v] = { preterito: strong(v.slice(0, -4) + 'uj', true) };
}
I.traer = {
  presente: ['traigo', 'traes', 'trae', 'traemos', 'traéis', 'traen'],
  preterito: strong('traj', true),
  presenteSubj: subjA('traig'), imperativo: ['—', 'trae', 'traiga', 'traigamos', 'traed', 'traigan'],
};
I['oír'] = {
  presente: ['oigo', 'oyes', 'oye', 'oímos', 'oís', 'oyen'],
  presenteSubj: subjA('oig'), imperativo: ['—', 'oye', 'oiga', 'oigamos', 'oíd', 'oigan'],
};
I.valer = {
  presente: ['valgo', 'vales', 'vale', 'valemos', 'valéis', 'valen'],
  futuro: fut('valdr'), condicional: cond('valdr'),
  presenteSubj: subjA('valg'), imperativo: ['—', 'vale', 'valga', 'valgamos', 'valed', 'valgan'],
};
for (const [v, p] of [['abrir', 'abierto'], ['cubrir', 'cubierto'], ['descubrir', 'descubierto'], ['escribir', 'escrito'], ['describir', 'descrito'], ['romper', 'roto'],
  ['volver', 'vuelto'], ['devolver', 'devuelto'], ['resolver', 'resuelto'], ['envolver', 'envuelto'], ['morir', 'muerto'], ['absolver', 'absuelto'], ['disolver', 'disuelto'],
  ['revolver', 'revuelto'], ['inscribir', 'inscrito'], ['prescribir', 'prescrito'], ['suscribir', 'suscrito'], ['pudrir', 'podrido'], ['encubrir', 'encubierto'], ['entreabrir', 'entreabierto']]) {
  I[v] = { participio: p };
}
I['reír'] = {
  presente: ['río', 'ríes', 'ríe', 'reímos', 'reís', 'ríen'],
  preterito: ['reí', 'reíste', 'rio', 'reímos', 'reísteis', 'rieron'],
  presenteSubj: ['ría', 'rías', 'ría', 'riamos', 'riais', 'rían'],
  imperativo: ['—', 'ríe', 'ría', 'riamos', 'reíd', 'rían'],
  participio: 'reído', gerundio: 'riendo',
};
I['freír'] = {
  presente: ['frío', 'fríes', 'fríe', 'freímos', 'freís', 'fríen'],
  preterito: ['freí', 'freíste', 'frio', 'freímos', 'freísteis', 'frieron'],
  presenteSubj: ['fría', 'frías', 'fría', 'friamos', 'friais', 'frían'],
  imperativo: ['—', 'fríe', 'fría', 'friamos', 'freíd', 'frían'],
  participio: 'frito', gerundio: 'friendo',
};
derive(I, data, 'sonreír', 'reír', 'son', {
  over: {
    preterito: ['sonreí', 'sonreíste', 'sonrió', 'sonreímos', 'sonreísteis', 'sonrieron'],
    presenteSubj: ['sonría', 'sonrías', 'sonría', 'sonriamos', 'sonriáis', 'sonrían'],
    imperativo: ['—', 'sonríe', 'sonría', 'sonriamos', 'sonreíd', 'sonrían'],
  },
});
I.oler = {
  presente: ['huelo', 'hueles', 'huele', 'olemos', 'oléis', 'huelen'],
  presenteSubj: ['huela', 'huelas', 'huela', 'olamos', 'oláis', 'huelan'],
  imperativo: ['—', 'huele', 'huela', 'olamos', 'oled', 'huelan'],
};
I.errar = {
  presente: ['yerro', 'yerras', 'yerra', 'erramos', 'erráis', 'yerran'],
  presenteSubj: ['yerre', 'yerres', 'yerre', 'erremos', 'erréis', 'yerren'],
  imperativo: ['—', 'yerra', 'yerre', 'erremos', 'errad', 'yerren'],
};
// prefixed derivatives
// prefixed tú imperatives carry a written accent (mantén, compón, prevén)
const deriveAccent = (v, base, p, tu) => { derive(I, data, v, base, p); I[v].imperativo[1] = p + tu; };
for (const [v, p] of [['deshacer', 'des'], ['rehacer', 're']]) derive(I, data, v, 'hacer', p);
derive(I, data, 'satisfacer', 'hacer', 'satisf', { over: { participio: 'satisfecho' } });
for (const [v, p] of [['componer', 'com'], ['proponer', 'pro'], ['suponer', 'su'], ['disponer', 'dis'], ['exponer', 'ex'], ['imponer', 'im'], ['oponer', 'o'], ['reponer', 're']]) deriveAccent(v, 'poner', p, 'pón');
for (const [v, p] of [['mantener', 'man'], ['contener', 'con'], ['obtener', 'ob'], ['sostener', 'sos'], ['detener', 'de'], ['entretener', 'entre'], ['retener', 're']]) deriveAccent(v, 'tener', p, 'tén');
for (const [v, p] of [['prevenir', 'pre'], ['convenir', 'con'], ['intervenir', 'inter']]) deriveAccent(v, 'venir', p, 'vén');
for (const [v, p] of [['atraer', 'a'], ['distraer', 'dis'], ['contraer', 'con'], ['extraer', 'ex']]) derive(I, data, v, 'traer', p);
derive(I, data, 'bendecir', 'decir', 'ben', {
  over: { futuro: fut('bendecir'), condicional: cond('bendecir'), participio: 'bendecido', imperativo: ['—', 'bendice', 'bendiga', 'bendigamos', 'bendecid', 'bendigan'] },
});
derive(I, data, 'maldecir', 'decir', 'mal', {
  over: { futuro: fut('maldecir'), condicional: cond('maldecir'), participio: 'maldecido', imperativo: ['—', 'maldice', 'maldiga', 'maldigamos', 'maldecid', 'maldigan'] },
});
// fill the null (vosotros) slots of imp() users — none used; keep helper for clarity
void imp; void T;

data.commonVerbs = [
  'ser', 'estar', 'tener', 'hacer', 'poder', 'decir', 'ir', 'ver', 'dar', 'saber', 'querer', 'llegar', 'pasar', 'deber', 'poner', 'parecer', 'quedar', 'creer', 'hablar', 'llevar',
  'dejar', 'seguir', 'encontrar', 'llamar', 'venir', 'pensar', 'salir', 'volver', 'tomar', 'conocer', 'vivir', 'sentir', 'tratar', 'mirar', 'contar', 'empezar', 'esperar', 'buscar', 'existir', 'entrar',
  'trabajar', 'escribir', 'perder', 'producir', 'ocurrir', 'entender', 'pedir', 'recibir', 'recordar', 'terminar', 'permitir', 'aparecer', 'conseguir', 'comenzar', 'servir', 'sacar', 'necesitar', 'mantener', 'resultar', 'leer',
  'caer', 'cambiar', 'presentar', 'crear', 'abrir', 'considerar', 'oír', 'acabar', 'convertir', 'ganar', 'formar', 'traer', 'partir', 'morir', 'aceptar', 'realizar', 'suponer', 'comprender', 'lograr', 'explicar',
  'preguntar', 'tocar', 'reconocer', 'estudiar', 'alcanzar', 'nacer', 'dirigir', 'correr', 'utilizar', 'pagar', 'ayudar', 'gustar', 'jugar', 'escuchar', 'cumplir', 'ofrecer', 'descubrir', 'levantar', 'intentar', 'usar',
  'decidir', 'repetir', 'olvidar', 'valer', 'comer', 'mostrar', 'ocupar', 'mover', 'continuar', 'suceder', 'fijar', 'referir', 'acercar', 'dedicar', 'aprender', 'comprar', 'subir', 'evitar', 'interesar', 'cerrar',
  'echar', 'responder', 'sufrir', 'importar', 'obtener', 'observar', 'indicar', 'imaginar', 'soler', 'detener', 'desarrollar', 'señalar', 'elegir', 'preparar', 'proponer', 'demostrar', 'significar', 'reunir', 'faltar', 'bajar',
  'matar', 'representar', 'establecer', 'guardar', 'iniciar', 'resolver', 'cortar', 'caber', 'dormir', 'construir', 'andar', 'asegurar', 'pretender', 'romper', 'participar', 'vender', 'bastar', 'incluir', 'extender', 'comunicar',
  'aprovechar', 'crecer', 'cruzar', 'desear', 'escoger', 'marcar', 'mandar', 'beber', 'aumentar', 'aplicar', 'cubrir', 'soportar', 'pertenecer', 'sentar', 'cocinar', 'viajar', 'bailar', 'cantar', 'caminar', 'nadar',
  'limpiar', 'lavar', 'dibujar', 'pintar', 'enseñar', 'contestar', 'saludar', 'visitar', 'invitar', 'celebrar', 'regresar', 'descansar', 'despertar', 'acostar', 'vestir', 'duchar', 'bañar', 'casar', 'reír', 'sonreír',
  'llorar', 'gritar', 'soñar', 'volar', 'conducir', 'manejar', 'aparcar', 'arreglar', 'funcionar', 'practicar', 'mejorar', 'empeorar', 'organizar', 'planear', 'probar', 'costar', 'gastar', 'ahorrar', 'prestar', 'devolver',
  'enviar', 'recoger', 'coger', 'agarrar', 'soltar', 'tirar', 'empujar', 'apagar', 'encender', 'abrazar', 'besar', 'odiar', 'amar', 'confiar', 'mentir', 'prometer', 'exigir', 'protestar', 'discutir', 'defender',
  'atacar', 'proteger', 'cuidar', 'curar', 'doler', 'nevar', 'llover', 'calentar', 'enfriar', 'congelar', 'hervir', 'freír', 'cocer', 'mezclar', 'añadir', 'oler', 'sonar', 'parar', 'girar', 'torcer',
  'doblar', 'llenar', 'vaciar', 'medir', 'pesar', 'sumar', 'restar', 'multiplicar', 'dividir', 'calcular', 'dudar', 'negar', 'afirmar', 'insistir', 'convencer', 'adivinar', 'apostar', 'apoyar', 'impedir', 'obligar',
  'prohibir', 'recomendar', 'sugerir', 'advertir', 'avisar', 'anunciar', 'publicar', 'imprimir', 'traducir', 'describir', 'definir', 'inventar', 'despedir', 'contratar', 'nombrar', 'votar', 'gobernar', 'obedecer', 'castigar', 'perdonar',
  'disculpar', 'agradecer', 'felicitar', 'actuar', 'graduar', 'situar', 'evaluar', 'variar', 'guiar', 'criar', 'esquiar', 'adquirir', 'huir', 'concluir', 'distribuir', 'contribuir', 'influir', 'destruir', 'sustituir', 'atribuir',
  'poseer', 'corregir', 'competir', 'perseguir', 'colgar', 'rogar', 'forzar', 'almorzar', 'acordar', 'aprobar', 'comprobar', 'morder', 'rodar', 'renovar', 'confesar', 'temblar', 'tropezar', 'atravesar', 'ascender', 'descender',
  'tender', 'atender', 'preferir', 'divertir', 'herir', 'transferir', 'satisfacer', 'deshacer', 'componer', 'disponer', 'exponer', 'imponer', 'oponer', 'contener', 'sostener', 'entretener', 'prevenir', 'convenir', 'intervenir', 'atraer',
  'distraer', 'contraer', 'extraer', 'reducir', 'introducir', 'deducir', 'envolver', 'escapar', 'saltar', 'lanzar', 'golpear', 'romper_', 'quemar', 'ensuciar', 'mojar', 'secar', 'brillar', 'crecer_', 'desaparecer', 'merecer', 'padecer',
  'favorecer', 'enriquecer', 'envejecer', 'fortalecer', 'permanecer', 'agradar', 'molestar', 'aburrir', 'asustar', 'sorprender', 'preocupar', 'alegrar', 'enfadar', 'enojar', 'cansar', 'relajar', 'divorciar', 'nacer_', 'bendecir', 'maldecir',
  'averiguar', 'errar', 'exagerar', 'firmar', 'borrar', 'copiar', 'pegar', 'grabar', 'cargar', 'descargar', 'instalar', 'conectar', 'navegar', 'chatear', 'buscar_', 'investigar', 'analizar', 'comparar', 'clasificar', 'identificar',
  'justificar', 'modificar', 'verificar', 'fabricar', 'colocar', 'provocar', 'equivocar', 'explotar', 'acabar_', 'alquilar', 'reservar', 'cancelar', 'confirmar', 'esperar_', 'tardar', 'durar', 'ocurrir_', 'surgir', 'fingir', 'elegir_',
  'vencer', 'ejercer', 'cosechar', 'sembrar', 'regar', 'cultivar', 'cazar', 'pescar', 'rezar', 'jurar', 'robar', 'engañar', 'esconder', 'huir_', 'liberar', 'rescatar', 'salvar', 'sobrevivir', 'luchar', 'pelear',
].filter(v => !v.endsWith('_'));

finish(data, ['ser', 'estar', 'ir', 'haber', 'tener', 'hacer', 'poder', 'decir', 'querer', 'saber', 'poner', 'venir', 'ver', 'dar', 'salir']);
