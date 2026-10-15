// Portuguese (European pronouns; the main-tense forms are the same in Brazil except pretérito nós -ámos → -amos) — generator for pt.json
import { derive, finish } from './common.mjs';

const FUT = ['ei', 'ás', 'á', 'emos', 'eis', 'ão'];
const COND = ['ia', 'ias', 'ia', 'íamos', 'íeis', 'iam'];
const fut = s => FUT.map(e => s + e);
const cond = s => COND.map(e => s + e);
const SUBJ_A = ['a', 'as', 'a', 'amos', 'ais', 'am'];
const subjFromYo = yo => SUBJ_A.map(e => yo.replace(/o$/, '') + e);
/** strong preterite: tiv/teve → tive tiveste teve tivemos tivestes tiveram */
const pretStrong = (s, s3) => [s + 'e', s + 'este', s3, s + 'emos', s + 'estes', s + 'eram'];
const ACUTE = { a: 'á', e: 'é', i: 'í', o: 'ó', u: 'ú' };
const acute = s => { for (let i = s.length - 1; i >= 0; i--) if (ACUTE[s[i]]) return s.slice(0, i) + ACUTE[s[i]] + s.slice(i + 1); return s; };
/** imperfeito do conjuntivo from the 3rd-plural preterite (tiveram → tivesse … tivéssemos) */
const impSubjFrom = (p5) => { const s = p5.replace(/ram$/, ''); return [s + 'sse', s + 'sses', s + 'sse', acute(s) + 'ssemos', acute(s) + 'sseis', s + 'ssem']; };

const data = {
  language: 'pt',
  name: 'Portuguese',
  pronouns: ['eu', 'tu', 'ele/ela/você', 'nós', 'vós', 'eles/elas/vocês'],
  imperativePronouns: ['—', 'tu', 'você', 'nós', 'vós', 'vocês'],
  tenses: [
    { id: 'presente', name: 'Presente', group: 'indicativo' },
    { id: 'preteritoPerfeito', name: 'Pretérito perfeito', group: 'indicativo' },
    { id: 'imperfeito', name: 'Pretérito imperfeito', group: 'indicativo' },
    { id: 'futuro', name: 'Futuro', group: 'indicativo' },
    { id: 'condicional', name: 'Condicional', group: 'condicional' },
    { id: 'presenteSubj', name: 'Presente do conjuntivo', group: 'subjuntivo' },
    { id: 'imperfeitoSubj', name: 'Imperfeito do conjuntivo', group: 'subjuntivo' },
    { id: 'imperativo', name: 'Imperativo', group: 'imperativo' },
    { id: 'participio', name: 'Particípio', group: 'nonfinite', single: true },
    { id: 'gerundio', name: 'Gerúndio', group: 'nonfinite', single: true },
  ],
  groupNames: { indicativo: 'Indicativo', subjuntivo: 'Conjuntivo', imperativo: 'Imperativo', condicional: 'Condicional', nonfinite: 'Formas nominais' },
  stemKinds: [
    { kind: 'infinitiveStem', description: 'The infinitive minus -ar/-er/-ir (falar → fal). Default. Stem changes apply only to this kind.' },
    { kind: 'infinitive', description: 'The whole infinitive (falar + ei = falarei; futuro and condicional).' },
    { kind: 'form', description: 'Another conjugated form of the same verb with a suffix stripped: imperfeitoSubj uses preteritoPerfeito[5] minus "ram" (falaram → fala + sse).' },
    { kind: 'accent-marker', description: 'An ending that starts with ´ (U+00B4) accents the last vowel of the stem via rules.accentMap (a→á, e→ê, i→í): fala+´ssemos → falássemos, come+´ssemos → comêssemos. Strong preterites take é instead (tivéssemos) and are explicit in irregulars.' },
    { kind: 'strip-s', description: 'The vós imperative is presente[4] minus its final s (falais → falai, comeis → comei, partis → parti).' },
  ],
  rules: {
    classes: [
      { id: 'ar', name: '-ar', match: 'ar$', strip: 2 },
      { id: 'er', name: '-er', match: 'er$', strip: 2 },
      { id: 'ir', name: '-ir', match: 'ir$', strip: 2 },
    ],
    endings: {
      ar: {
        presente: ['o', 'as', 'a', 'amos', 'ais', 'am'],
        preteritoPerfeito: ['ei', 'aste', 'ou', 'ámos', 'astes', 'aram'],
        imperfeito: ['ava', 'avas', 'ava', 'ávamos', 'áveis', 'avam'],
        futuro: FUT, condicional: COND,
        presenteSubj: ['e', 'es', 'e', 'emos', 'eis', 'em'],
        imperfeitoSubj: ['sse', 'sses', 'sse', '´ssemos', '´sseis', 'ssem'],
        participio: 'ado', gerundio: 'ando',
      },
      er: {
        presente: ['o', 'es', 'e', 'emos', 'eis', 'em'],
        preteritoPerfeito: ['i', 'este', 'eu', 'emos', 'estes', 'eram'],
        imperfeito: ['ia', 'ias', 'ia', 'íamos', 'íeis', 'iam'],
        futuro: FUT, condicional: COND,
        presenteSubj: SUBJ_A,
        imperfeitoSubj: ['sse', 'sses', 'sse', '´ssemos', '´sseis', 'ssem'],
        participio: 'ido', gerundio: 'endo',
      },
      ir: {
        presente: ['o', 'es', 'e', 'imos', 'is', 'em'],
        preteritoPerfeito: ['i', 'iste', 'iu', 'imos', 'istes', 'iram'],
        imperfeito: ['ia', 'ias', 'ia', 'íamos', 'íeis', 'iam'],
        futuro: FUT, condicional: COND,
        presenteSubj: SUBJ_A,
        imperfeitoSubj: ['sse', 'sses', 'sse', '´ssemos', '´sseis', 'ssem'],
        participio: 'ido', gerundio: 'indo',
      },
    },
    stems: {
      futuro: { kind: 'infinitive' },
      condicional: { kind: 'infinitive' },
      imperfeitoSubj: { kind: 'form', tense: 'preteritoPerfeito', person: 5, strip: 'ram' },
    },
    compose: {
      imperativo: [
        { literal: '—' },
        { form: 'presente', person: 2 },
        { form: 'presenteSubj', person: 2 },
        { form: 'presenteSubj', person: 3 },
        { form: 'presente', person: 4, stripIf: 's$' },
        { form: 'presenteSubj', person: 5 },
      ],
    },
    compound: {},
    accentMap: { a: 'á', e: 'ê', i: 'í', o: 'ô', u: 'ú' },
    spelling: [
      { id: 'car->qu', when: 'stem ends in c (-car) before e (ficar → fiquei, fique)', classes: ['ar'], match: 'c$', tenses: { preteritoPerfeito: [0], presenteSubj: 'all' }, do: { stem: { from: 'c$', to: 'qu' } } },
      { id: 'gar->gu', when: 'stem ends in g (-gar) before e (chegar → cheguei, chegue)', classes: ['ar'], match: 'g$', tenses: { preteritoPerfeito: [0], presenteSubj: 'all' }, do: { stem: { from: 'g$', to: 'gu' } } },
      { id: 'çar->c', when: 'stem ends in ç (-çar) before e (começar → comecei, comece)', classes: ['ar'], match: 'ç$', tenses: { preteritoPerfeito: [0], presenteSubj: 'all' }, do: { stem: { from: 'ç$', to: 'c' } } },
      { id: 'cer/cir->ç', when: '-cer/-cir before o/a (conhecer → conheço, conheça)', classes: ['er', 'ir'], match: 'c$', tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'c$', to: 'ç' } } },
      { id: 'ger/gir->j', when: '-ger/-gir before o/a (dirigir → dirijo, dirija)', classes: ['er', 'ir'], match: 'g$', tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'g$', to: 'j' } } },
      { id: 'guer/guir->g', when: '-guer/-guir before o/a (seguir → sigo, distinguir → distingo, erguer → ergo)', classes: ['er', 'ir'], match: 'gu$', tenses: { presente: [0], presenteSubj: 'all' }, do: { stem: { from: 'gu$', to: 'g' } } },
      { id: 'uir->ui', when: '-uir verbs (possuir, incluir): tu/ele presente end in -uis/-ui, not -ues/-ue', classes: ['ir'], match: '(^|[^gq])u$', tenses: { presente: [1, 2] }, do: { ending: { from: '^e', to: 'i' } } },
      { id: 'vowel+i->í', when: 'stem ends in a vowel (possuir, cair): the i of the ending is stressed and accented (possuímos, possuí, possuíste, possuía, possuído)', classes: ['er', 'ir'], match: '(^|[^gq])[aeiou]$', tenses: { presente: [3, 4], preteritoPerfeito: [0, 1, 3, 4, 5], imperfeito: 'all', participio: [0] }, do: { ending: { from: '^i', to: 'í' } } },
    ],
  },
  stemChangeKinds: {
    'e>i': { from: 'e', to: 'i', description: '-ir verbs: last e of the stem → i in the eu form and the whole presente do conjuntivo (servir → sirvo, sirva, sirvamos; seguir → sigo, siga)', persons: { presente: [0], presenteSubj: 'all' } },
    'o>u': { from: 'o', to: 'u', description: '-ir verbs: last o → u in the eu form and the whole presente do conjuntivo (dormir → durmo, durma, durmamos)', persons: { presente: [0], presenteSubj: 'all' } },
    'u>o': { from: 'u', to: 'o', description: '-ir verbs: u → o in tu/ele/eles of the presente (subir → sobes, sobe, sobem)', persons: { presente: [1, 2, 5] } },
    'e>ei': { from: 'e', to: 'ei', description: '-ear verbs: e → ei in the boot persons of presente and presente do conjuntivo (passear → passeio, passeie; passeamos, passeemos)', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
    'i>ei': { from: 'i', to: 'ei', description: 'odiar-type -iar verbs: i → ei in the boot persons (odiar → odeio, odeie)', persons: { presente: [0, 1, 2, 5], presenteSubj: [0, 1, 2, 5] } },
  },
  stemChanging: {},
  separable: [],
  irregulars: {},
  commonVerbs: [],
};

const sc = (kind, verbs) => { for (const v of verbs) data.stemChanging[v] = { kind }; };
sc('e>i', ['servir', 'sentir', 'mentir', 'preferir', 'repetir', 'vestir', 'ferir', 'sugerir', 'divertir', 'referir', 'conferir', 'competir', 'despir', 'seguir', 'conseguir', 'perseguir', 'prosseguir', 'consentir', 'pressentir', 'ressentir', 'digerir', 'inserir', 'aderir', 'advertir', 'convertir', 'desmentir', 'investir', 'revestir', 'transferir', 'interferir', 'diferir', 'adquirir_']);
delete data.stemChanging['adquirir_'];
sc('o>u', ['dormir', 'cobrir', 'descobrir', 'encobrir', 'tossir', 'engolir']);
sc('u>o', ['subir', 'sumir', 'fugir', 'cuspir', 'acudir', 'sacudir', 'consumir', 'bulir']);
sc('e>ei', ['passear', 'recear', 'nomear', 'pentear', 'cear', 'frear', 'bloquear', 'chatear', 'bronzear', 'delinear', 'estrear', 'golear', 'saborear', 'sortear', 'tatear', 'volear', 'presentear']);
sc('i>ei', ['odiar', 'incendiar', 'ansiar', 'mediar', 'remediar', 'intermediar']);

const I = data.irregulars;
/** compact definition → partial explicit entry */
function mk(v, d) {
  const e = {};
  if (d.pres) e.presente = d.pres;
  if (d.pret) {
    e.preteritoPerfeito = d.pret;
    e.imperfeitoSubj = d.impSubj ?? impSubjFrom(d.pret[5]);
  }
  if (d.impf) e.imperfeito = d.impf;
  if (d.fut) { e.futuro = fut(d.fut); e.condicional = cond(d.fut); }
  e.presenteSubj = d.subj ?? (d.pres ? subjFromYo(d.pres[0]) : undefined);
  if (!e.presenteSubj) delete e.presenteSubj;
  if (d.imp6) e.imperativo = d.imp6;
  if (d.pp) e.participio = d.pp;
  if (d.ger) e.gerundio = d.ger;
  I[v] = e;
}

mk('ser', { pres: ['sou', 'és', 'é', 'somos', 'sois', 'são'], pret: ['fui', 'foste', 'foi', 'fomos', 'fostes', 'foram'], impf: ['era', 'eras', 'era', 'éramos', 'éreis', 'eram'],
  subj: ['seja', 'sejas', 'seja', 'sejamos', 'sejais', 'sejam'], impSubj: ['fosse', 'fosses', 'fosse', 'fôssemos', 'fôsseis', 'fossem'],
  imp6: ['—', 'sê', 'seja', 'sejamos', 'sede', 'sejam'], pp: 'sido', ger: 'sendo' });
mk('estar', { pres: ['estou', 'estás', 'está', 'estamos', 'estais', 'estão'], pret: pretStrong('estiv', 'esteve'),
  subj: ['esteja', 'estejas', 'esteja', 'estejamos', 'estejais', 'estejam'], imp6: ['—', 'está', 'esteja', 'estejamos', 'estai', 'estejam'] });
mk('ter', { pres: ['tenho', 'tens', 'tem', 'temos', 'tendes', 'têm'], pret: pretStrong('tiv', 'teve'), impf: ['tinha', 'tinhas', 'tinha', 'tínhamos', 'tínheis', 'tinham'],
  imp6: ['—', 'tem', 'tenha', 'tenhamos', 'tende', 'tenham'] });
mk('ir', { pres: ['vou', 'vais', 'vai', 'vamos', 'ides', 'vão'], pret: ['fui', 'foste', 'foi', 'fomos', 'fostes', 'foram'],
  subj: ['vá', 'vás', 'vá', 'vamos', 'vades', 'vão'], impSubj: ['fosse', 'fosses', 'fosse', 'fôssemos', 'fôsseis', 'fossem'],
  imp6: ['—', 'vai', 'vá', 'vamos', 'ide', 'vão'], pp: 'ido', ger: 'indo' });
mk('fazer', { pres: ['faço', 'fazes', 'faz', 'fazemos', 'fazeis', 'fazem'], pret: pretStrong('fiz', 'fez'), fut: 'far', imp6: ['—', 'faz', 'faça', 'façamos', 'fazei', 'façam'], pp: 'feito' });
mk('poder', { pres: ['posso', 'podes', 'pode', 'podemos', 'podeis', 'podem'], pret: pretStrong('pud', 'pôde'), imp6: ['—', 'pode', 'possa', 'possamos', 'podei', 'possam'] });
mk('dizer', { pres: ['digo', 'dizes', 'diz', 'dizemos', 'dizeis', 'dizem'], pret: pretStrong('diss', 'disse'), fut: 'dir', imp6: ['—', 'diz', 'diga', 'digamos', 'dizei', 'digam'], pp: 'dito' });
mk('ver', { pres: ['vejo', 'vês', 'vê', 'vemos', 'vedes', 'veem'], pret: ['vi', 'viste', 'viu', 'vimos', 'vistes', 'viram'],
  impSubj: ['visse', 'visses', 'visse', 'víssemos', 'vísseis', 'vissem'], imp6: ['—', 'vê', 'veja', 'vejamos', 'vede', 'vejam'], pp: 'visto' });
mk('vir', { pres: ['venho', 'vens', 'vem', 'vimos', 'vindes', 'vêm'], pret: ['vim', 'vieste', 'veio', 'viemos', 'viestes', 'vieram'], impf: ['vinha', 'vinhas', 'vinha', 'vínhamos', 'vínheis', 'vinham'],
  imp6: ['—', 'vem', 'venha', 'venhamos', 'vinde', 'venham'], pp: 'vindo', ger: 'vindo' });
mk('dar', { pres: ['dou', 'dás', 'dá', 'damos', 'dais', 'dão'], pret: ['dei', 'deste', 'deu', 'demos', 'destes', 'deram'],
  subj: ['dê', 'dês', 'dê', 'demos', 'deis', 'deem'], imp6: ['—', 'dá', 'dê', 'demos', 'dai', 'deem'] });
mk('saber', { pres: ['sei', 'sabes', 'sabe', 'sabemos', 'sabeis', 'sabem'], pret: pretStrong('soub', 'soube'),
  subj: ['saiba', 'saibas', 'saiba', 'saibamos', 'saibais', 'saibam'], imp6: ['—', 'sabe', 'saiba', 'saibamos', 'sabei', 'saibam'] });
mk('querer', { pres: ['quero', 'queres', 'quer', 'queremos', 'quereis', 'querem'], pret: pretStrong('quis', 'quis'),
  subj: ['queira', 'queiras', 'queira', 'queiramos', 'queirais', 'queiram'], imp6: ['—', 'quer', 'queira', 'queiramos', 'querei', 'queiram'] });
mk('pôr', { pres: ['ponho', 'pões', 'põe', 'pomos', 'pondes', 'põem'], pret: pretStrong('pus', 'pôs'), impf: ['punha', 'punhas', 'punha', 'púnhamos', 'púnheis', 'punham'], fut: 'por',
  imp6: ['—', 'põe', 'ponha', 'ponhamos', 'ponde', 'ponham'], pp: 'posto', ger: 'pondo' });
mk('trazer', { pres: ['trago', 'trazes', 'traz', 'trazemos', 'trazeis', 'trazem'], pret: pretStrong('troux', 'trouxe'), fut: 'trar', imp6: ['—', 'traz', 'traga', 'tragamos', 'trazei', 'tragam'] });
mk('haver', { pres: ['hei', 'hás', 'há', 'havemos', 'haveis', 'hão'], pret: pretStrong('houv', 'houve'), imp6: ['—', 'há', 'haja', 'hajamos', 'havei', 'hajam'] });
mk('caber', { pres: ['caibo', 'cabes', 'cabe', 'cabemos', 'cabeis', 'cabem'], pret: pretStrong('coub', 'coube') });
mk('ler', { pres: ['leio', 'lês', 'lê', 'lemos', 'ledes', 'leem'], pret: ['li', 'leste', 'leu', 'lemos', 'lestes', 'leram'], impSubj: ['lesse', 'lesses', 'lesse', 'lêssemos', 'lêsseis', 'lessem'],
  imp6: ['—', 'lê', 'leia', 'leiamos', 'lede', 'leiam'] });
mk('crer', { pres: ['creio', 'crês', 'crê', 'cremos', 'credes', 'creem'], pret: ['cri', 'creste', 'creu', 'cremos', 'crestes', 'creram'], impSubj: ['cresse', 'cresses', 'cresse', 'crêssemos', 'crêsseis', 'cressem'],
  imp6: ['—', 'crê', 'creia', 'creiamos', 'crede', 'creiam'] });
mk('rir', { pres: ['rio', 'ris', 'ri', 'rimos', 'rides', 'riem'], pret: ['ri', 'riste', 'riu', 'rimos', 'ristes', 'riram'], impSubj: ['risse', 'risses', 'risse', 'ríssemos', 'rísseis', 'rissem'],
  imp6: ['—', 'ri', 'ria', 'riamos', 'ride', 'riam'] });
for (const v of ['sair', 'cair', 'trair', 'atrair', 'distrair', 'contrair', 'subtrair', 'extrair']) {
  const s = v.slice(0, -2); // sa
  mk(v, { pres: [s + 'io', s + 'is', s + 'i', s + 'ímos', s + 'ís', s + 'em'], pret: [s + 'í', s + 'íste', s + 'iu', s + 'ímos', s + 'ístes', s + 'íram'],
    impf: ['ía', 'ías', 'ía', 'íamos', 'íeis', 'íam'].map(x => s + x), impSubj: [s + 'ísse', s + 'ísses', s + 'ísse', s + 'íssemos', s + 'ísseis', s + 'íssem'], pp: s + 'ído' });
}
mk('ouvir', { pres: ['ouço', 'ouves', 'ouve', 'ouvimos', 'ouvis', 'ouvem'] });
mk('doer', { pres: ['doo', 'dóis', 'dói', 'doemos', 'doeis', 'doem'] });
mk('moer', { pres: ['moo', 'móis', 'mói', 'moemos', 'moeis', 'moem'] });
mk('roer', { pres: ['roo', 'róis', 'rói', 'roemos', 'roeis', 'roem'] });
mk('pedir', { pres: ['peço', 'pedes', 'pede', 'pedimos', 'pedis', 'pedem'] });
mk('medir', { pres: ['meço', 'medes', 'mede', 'medimos', 'medis', 'medem'] });
mk('perder', { pres: ['perco', 'perdes', 'perde', 'perdemos', 'perdeis', 'perdem'] });
mk('valer', { pres: ['valho', 'vales', 'vale', 'valemos', 'valeis', 'valem'] });
mk('prevenir', { pres: ['previno', 'prevines', 'previne', 'prevenimos', 'prevenis', 'previnem'] });
mk('agredir', { pres: ['agrido', 'agrides', 'agride', 'agredimos', 'agredis', 'agridem'] });
mk('progredir', { pres: ['progrido', 'progrides', 'progride', 'progredimos', 'progredis', 'progridem'] });
for (const v of ['construir', 'destruir', 'instruir', 'reconstruir', 'obstruir']) {
  const s = v.slice(0, -3); // constr
  mk(v, { pres: [s + 'uo', s + 'óis', s + 'ói', s + 'uímos', s + 'uís', s + 'oem'], subj: subjFromYo(s + 'uo') });
}
for (const [v, pp] of [['abrir', 'aberto'], ['escrever', 'escrito'], ['morrer', 'morto'], ['gastar', 'gasto'], ['ganhar', 'ganho'], ['pagar', 'pago'], ['cobrir', 'coberto'], ['descobrir', 'descoberto'], ['encobrir', 'encoberto'], ['descrever', 'descrito'], ['inscrever', 'inscrito'], ['reabrir', 'reaberto']]) {
  I[v] = { participio: pp };
}

const D = (list, base, fix) => { for (const [v, p] of list) { derive(I, data, v, base, p); if (fix) fix(v, p); } };
D([['manter', 'man'], ['obter', 'ob'], ['conter', 'con'], ['deter', 'de'], ['reter', 're'], ['entreter', 'entre'], ['suster', 'sus']], 'ter', (v, p) => {
  I[v].presente = [p + 'tenho', p + 'téns', p + 'tém', p + 'temos', p + 'tendes', p + 'têm'];
  I[v].imperativo[1] = p + 'tém';
});
D([['compor', 'com'], ['supor', 'su'], ['propor', 'pro'], ['dispor', 'dis'], ['expor', 'ex'], ['opor', 'o'], ['impor', 'im'], ['depor', 'de'], ['repor', 're'], ['apor', 'a'], ['decompor', 'decom'], ['pressupor', 'pressu'], ['transpor', 'trans']], 'pôr');
D([['convir', 'con'], ['intervir', 'inter'], ['provir', 'pro'], ['advir', 'ad'], ['sobrevir', 'sobre']], 'vir', (v, p) => {
  I[v].presente = [p + 'venho', p + 'véns', p + 'vém', p + 'vimos', p + 'vindes', p + 'vêm'];
  I[v].imperativo[1] = p + 'vém';
});
D([['desfazer', 'des'], ['satisfazer', 'satis'], ['refazer', 're'], ['perfazer', 'per']], 'fazer');
D([['prever', 'pre'], ['rever', 're'], ['antever', 'ante']], 'ver');
D([['contradizer', 'contra'], ['predizer', 'pre'], ['condizer', 'con'], ['desdizer', 'des'], ['bendizer', 'ben'], ['maldizer', 'mal']], 'dizer');
D([['reler', 're']], 'ler');
D([['sorrir', 'sor']], 'rir');
D([['despedir', 'des'], ['impedir', 'im'], ['expedir', 'ex']], 'pedir');
D([['equivaler', 'equi']], 'valer');
D([['desaparecer_', 'desa']], 'ter'); delete I['desaparecer_'];

data.commonVerbs = [
  'ser', 'estar', 'ter', 'haver', 'fazer', 'ir', 'poder', 'dizer', 'dar', 'ver', 'saber', 'querer', 'ficar', 'vir', 'falar', 'chegar', 'passar', 'dever', 'deixar', 'encontrar',
  'levar', 'começar', 'parecer', 'pensar', 'conhecer', 'sentir', 'achar', 'trabalhar', 'viver', 'olhar', 'tornar', 'tomar', 'existir', 'entrar', 'ouvir', 'pedir', 'pôr', 'sair', 'andar', 'precisar',
  'esperar', 'perder', 'gostar', 'procurar', 'usar', 'receber', 'escrever', 'acontecer', 'contar', 'seguir', 'voltar', 'abrir', 'conseguir', 'perguntar', 'ler', 'correr', 'continuar', 'morrer', 'nascer', 'trazer',
  'aprender', 'ganhar', 'acabar', 'criar', 'lembrar', 'esquecer', 'mostrar', 'manter', 'considerar', 'servir', 'apresentar', 'cair', 'comer', 'beber', 'dormir', 'jogar', 'cantar', 'dançar', 'nadar', 'caminhar',
  'viajar', 'visitar', 'morar', 'cozinhar', 'preparar', 'limpar', 'lavar', 'vestir', 'acordar', 'levantar', 'sentar', 'telefonar', 'convidar', 'agradecer', 'ajudar', 'amar', 'odiar', 'desejar', 'preferir', 'temer',
  'rir', 'sorrir', 'chorar', 'gritar', 'escutar', 'quebrar', 'vencer', 'acender', 'apagar', 'discutir', 'decidir', 'dividir', 'partir', 'repetir', 'subir', 'descer', 'fugir', 'mentir', 'cobrir', 'descobrir',
  'compor', 'supor', 'propor', 'dispor', 'expor', 'opor', 'impor', 'obter', 'conter', 'deter', 'reter', 'entreter', 'convir', 'intervir', 'desfazer', 'satisfazer', 'refazer', 'prever', 'rever', 'contradizer',
  'predizer', 'reler', 'despedir', 'impedir', 'medir', 'valer', 'caber', 'crer', 'trair', 'atrair', 'distrair', 'contrair', 'extrair', 'construir', 'destruir', 'instruir', 'possuir', 'incluir', 'concluir', 'excluir',
  'contribuir', 'atribuir', 'diminuir', 'influir', 'distinguir', 'erguer', 'prevenir', 'agredir', 'progredir', 'gastar', 'pagar', 'passear', 'recear', 'nomear', 'pentear', 'frear', 'bloquear', 'chatear', 'estrear', 'sortear',
  'incendiar', 'ansiar', 'mediar', 'remediar', 'sugerir', 'divertir', 'referir', 'conferir', 'competir', 'despir', 'perseguir', 'prosseguir', 'consentir', 'digerir', 'inserir', 'aderir', 'advertir', 'convertir', 'investir', 'transferir',
  'interferir', 'tossir', 'engolir', 'sumir', 'cuspir', 'acudir', 'sacudir', 'consumir', 'aceitar', 'acompanhar', 'acreditar', 'adorar', 'afirmar', 'alugar', 'alcançar', 'almoçar', 'apanhar', 'aproveitar', 'arranjar', 'arrumar',
  'assinar', 'assistir', 'atender', 'atravessar', 'avisar', 'bater', 'beijar', 'brincar', 'buscar', 'calar', 'cansar', 'carregar', 'casar', 'chamar', 'chover', 'colocar', 'combinar', 'comprar', 'comunicar', 'concordar',
  'conquistar', 'consertar', 'conversar', 'convencer', 'cortar', 'costumar', 'crescer', 'cuidar', 'cumprir', 'curar', 'custar', 'dedicar', 'defender', 'demorar', 'depender', 'descansar', 'desenhar', 'desenvolver', 'desistir', 'devolver',
  'dirigir', 'doer', 'durar', 'educar', 'eleger', 'emprestar', 'empurrar', 'enganar', 'ensinar', 'entender', 'entregar', 'enviar', 'escolher', 'esconder', 'estudar', 'evitar', 'exigir', 'explicar', 'faltar', 'fechar',
  'fingir', 'formar', 'fumar', 'funcionar', 'guardar', 'imaginar', 'importar', 'indicar', 'insistir', 'interessar', 'jantar', 'juntar', 'jurar', 'lamentar', 'lançar', 'ligar', 'lutar', 'marcar', 'matar', 'melhorar',
  'merecer', 'meter', 'mexer', 'mudar', 'notar', 'obedecer', 'observar', 'obrigar', 'ocorrer', 'oferecer', 'organizar', 'parar', 'participar', 'partilhar', 'permitir', 'pertencer', 'pescar', 'pintar', 'planear', 'poupar',
  'praticar', 'prender', 'proibir', 'prometer', 'proteger', 'provar', 'puxar', 'reagir', 'realizar', 'reclamar', 'recomendar', 'reconhecer', 'recordar', 'reduzir', 'representar', 'resolver', 'respeitar', 'responder', 'resultar', 'reunir',
  'roubar', 'saltar', 'salvar', 'secar', 'segurar', 'sofrer', 'sonhar', 'sujar', 'surgir', 'tentar', 'terminar', 'tirar', 'tocar', 'tratar', 'trocar', 'unir', 'utilizar', 'varrer', 'vender', 'verificar',
  'virar', 'voar', 'zangar', 'abraçar', 'adormecer', 'agir', 'aguentar', 'amanhecer', 'anoitecer', 'apetecer', 'aquecer', 'arrefecer', 'aparecer', 'desaparecer', 'envelhecer', 'esclarecer', 'estabelecer', 'favorecer', 'fortalecer', 'padecer',
  'permanecer', 'prevalecer', 'abastecer', 'acolher', 'aprovar', 'assustar', 'atrasar', 'avançar', 'baixar', 'brilhar', 'cheirar', 'coçar', 'conduzir', 'corrigir', 'cumprimentar', 'derrubar', 'despertar', 'detestar', 'disparar', 'emagrecer',
  'empregar', 'encher', 'enfrentar', 'engordar', 'enxergar', 'errar', 'espalhar', 'esticar', 'estragar', 'fabricar', 'ferver', 'fritar', 'girar', 'gravar', 'imprimir', 'inventar', 'investigar', 'julgar', 'largar', 'louvar',
  'machucar', 'mandar', 'namorar', 'negar', 'obrigar_', 'pensar_', 'pesar', 'piorar', 'pular', 'queimar', 'rasgar', 'regressar', 'remar', 'rezar', 'rolar', 'sangrar', 'sobrar', 'soltar', 'sorrir_', 'suar',
  'temperar', 'torcer', 'traduzir', 'tremer', 'vacinar', 'vigiar', 'votar', 'zelar', 'produzir', 'introduzir', 'seduzir', 'deduzir', 'ocupar', 'ganhar_', 'escrever_', 'descrever', 'inscrever', 'reabrir', 'perfazer', 'antever',
].filter(v => !v.endsWith('_'));

finish(data, ['ser', 'estar', 'ter', 'ir', 'fazer', 'poder', 'dizer', 'ver', 'vir', 'dar', 'saber', 'querer', 'pôr', 'trazer', 'haver']);
