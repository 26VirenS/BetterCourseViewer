// Italian — generator for it.json
import { derive, finish } from './common.mjs';

const FUT = ['ò', 'ai', 'à', 'emo', 'ete', 'anno'];
const COND = ['ei', 'esti', 'ebbe', 'emmo', 'este', 'ebbero'];
const fut = s => FUT.map(e => s + e);
const cond = s => COND.map(e => s + e);
const DASH6 = ['—', '—', '—', '—', '—', '—'];
/** strong passato remoto: irregular stem in persons 1/3/6, regular stem r with -ere ('e') or -ire ('i') endings elsewhere */
const prStrong = (irr, r, t = 'e') => [irr + 'i', r + t + 'sti', irr + 'e', r + (t === 'e' ? 'emmo' : 'immo'), r + t + 'ste', irr + 'ero'];
const subjA = s => ['a', 'a', 'a', 'iamo', 'iate', 'ano'].map(e => s + e); // -ere/-ire subjunctive from one stem

const ISC = ['capire', 'finire', 'preferire', 'pulire', 'costruire', 'spedire', 'restituire', 'unire', 'riunire', 'obbedire', 'proibire', 'colpire', 'stabilire', 'fornire', 'garantire',
  'guarire', 'impedire', 'inserire', 'punire', 'riferire', 'sostituire', 'suggerire', 'chiarire', 'definire', 'distribuire', 'agire', 'reagire', 'ferire', 'gradire', 'favorire',
  'contribuire', 'digerire', 'arrossire', 'tradire', 'ubbidire', 'diminuire', 'attribuire', 'trasferire', 'sparire', 'approfondire', 'condire', 'dimagrire', 'ingrandire', 'intuire',
  'progredire', 'istruire', 'costituire', 'percepire', 'rapire', 'scolpire', 'smarrire', 'starnutire', 'stupire', 'svanire', 'subire', 'zittire', 'esaurire', 'abolire', 'ammonire',
  'attribuire_', 'avvertire_', 'esibire', 'fallire', 'fiorire', 'impazzire', 'incuriosire', 'indebolire', 'inghiottire', 'nutrire', 'partorire', 'patire', 'preferire_', 'ripulire', 'sbalordire',
  'schiarire', 'seppellire', 'snellire', 'tossire', 'trasgredire', 'ubriacare_'].filter(v => !v.endsWith('_'));

const data = {
  language: 'it',
  name: 'Italian',
  pronouns: ['io', 'tu', 'lui/lei', 'noi', 'voi', 'loro'],
  imperativePronouns: ['—', 'tu', 'Lei', 'noi', 'voi', 'Loro'],
  tenses: [
    { id: 'presente', name: 'Presente', group: 'indicativo' },
    { id: 'passatoRemoto', name: 'Passato remoto', group: 'indicativo' },
    { id: 'imperfetto', name: 'Imperfetto', group: 'indicativo' },
    { id: 'futuro', name: 'Futuro semplice', group: 'indicativo' },
    { id: 'condizionale', name: 'Condizionale presente', group: 'condicional' },
    { id: 'congiuntivoPresente', name: 'Congiuntivo presente', group: 'subjuntivo' },
    { id: 'congiuntivoImperfetto', name: 'Congiuntivo imperfetto', group: 'subjuntivo' },
    { id: 'imperativo', name: 'Imperativo', group: 'imperativo' },
    { id: 'participio', name: 'Participio passato', group: 'nonfinite', single: true },
    { id: 'gerundio', name: 'Gerundio', group: 'nonfinite', single: true },
  ],
  groupNames: { indicativo: 'Indicativo', subjuntivo: 'Congiuntivo', imperativo: 'Imperativo', condicional: 'Condizionale', nonfinite: 'Forme indefinite' },
  stemKinds: [
    { kind: 'infinitiveStem', description: 'The infinitive minus -are/-ere/-ire (parlare → parl). Default.' },
    { kind: 'infinitiveStem+suffix', description: 'infinitiveStem plus a per-class vowel+r for futuro/condizionale (parl+er-ò, cred+er-ò, dorm+ir-ò), added after spelling (cerch+er-ò, cominc+er-ò).' },
    { kind: 'class-list', description: 'The -isc- class ("isc") is lexical: a verb belongs to it only if it is in that class\'s "verbs" list; other tenses inherit the -ire endings ("inherits").' },
  ],
  rules: {
    classes: [
      { id: 'isc', name: '-ire (-isc- type)', match: 'ire$', strip: 3, verbs: ISC, inherits: 'ire' },
      { id: 'are', name: '-are', match: 'are$', strip: 3 },
      { id: 'ere', name: '-ere', match: 'ere$', strip: 3 },
      { id: 'ire', name: '-ire', match: 'ire$', strip: 3 },
    ],
    endings: {
      are: {
        presente: ['o', 'i', 'a', 'iamo', 'ate', 'ano'],
        passatoRemoto: ['ai', 'asti', 'ò', 'ammo', 'aste', 'arono'],
        imperfetto: ['avo', 'avi', 'ava', 'avamo', 'avate', 'avano'],
        futuro: FUT, condizionale: COND,
        congiuntivoPresente: ['i', 'i', 'i', 'iamo', 'iate', 'ino'],
        congiuntivoImperfetto: ['assi', 'assi', 'asse', 'assimo', 'aste', 'assero'],
        participio: 'ato', gerundio: 'ando',
      },
      ere: {
        presente: ['o', 'i', 'e', 'iamo', 'ete', 'ono'],
        passatoRemoto: ['ei', 'esti', 'é', 'emmo', 'este', 'erono'],
        imperfetto: ['evo', 'evi', 'eva', 'evamo', 'evate', 'evano'],
        futuro: FUT, condizionale: COND,
        congiuntivoPresente: ['a', 'a', 'a', 'iamo', 'iate', 'ano'],
        congiuntivoImperfetto: ['essi', 'essi', 'esse', 'essimo', 'este', 'essero'],
        participio: 'uto', gerundio: 'endo',
      },
      ire: {
        presente: ['o', 'i', 'e', 'iamo', 'ite', 'ono'],
        passatoRemoto: ['ii', 'isti', 'ì', 'immo', 'iste', 'irono'],
        imperfetto: ['ivo', 'ivi', 'iva', 'ivamo', 'ivate', 'ivano'],
        futuro: FUT, condizionale: COND,
        congiuntivoPresente: ['a', 'a', 'a', 'iamo', 'iate', 'ano'],
        congiuntivoImperfetto: ['issi', 'issi', 'isse', 'issimo', 'iste', 'issero'],
        participio: 'ito', gerundio: 'endo',
      },
      isc: {
        presente: ['isco', 'isci', 'isce', 'iamo', 'ite', 'iscono'],
        congiuntivoPresente: ['isca', 'isca', 'isca', 'iamo', 'iate', 'iscano'],
      },
    },
    stems: {
      futuro: { kind: 'infinitiveStem', suffix: { are: 'er', ere: 'er', ire: 'ir', isc: 'ir' } },
      condizionale: { kind: 'infinitiveStem', suffix: { are: 'er', ere: 'er', ire: 'ir', isc: 'ir' } },
    },
    compose: {
      imperativo: {
        are: [{ literal: '—' }, { form: 'presente', person: 2 }, { form: 'congiuntivoPresente', person: 2 }, { form: 'presente', person: 3 }, { form: 'presente', person: 4 }, { form: 'congiuntivoPresente', person: 5 }],
        '*': [{ literal: '—' }, { form: 'presente', person: 1 }, { form: 'congiuntivoPresente', person: 2 }, { form: 'presente', person: 3 }, { form: 'presente', person: 4 }, { form: 'congiuntivoPresente', person: 5 }],
      },
    },
    compound: {},
    accentMap: {},
    spelling: [
      { id: 'care/gare->h', when: 'stem ends in c/g (-care/-gare): h is inserted before e/i to keep the hard sound (cercare → cerchi, cercherò; pagare → paghiamo)', classes: ['are'], match: '[cg]$', tenses: { presente: [1, 3], congiuntivoPresente: 'all', futuro: 'all', condizionale: 'all' }, do: { stem: { from: '([cg])$', to: '$1h' } } },
      { id: 'iare->i', when: 'stem ends in i (-ciare/-giare/-sciare/-gliare/-iare): the i is dropped before an ending starting with i (cominciare → cominci, cominciamo; studiare → studi)', classes: ['are'], match: 'i$', tenses: { presente: [1, 3], congiuntivoPresente: 'all' }, do: { stem: { from: 'i$', to: '' } } },
      { id: 'ciare/giare-futuro', when: '-ciare/-giare drop the i before the -er- of futuro/condizionale (cominciare → comincerò, mangiare → mangerei); other -iare verbs keep it (studierò)', classes: ['are'], match: '[cg]i$', except: ['sciare'], tenses: { futuro: 'all', condizionale: 'all' }, do: { stem: { from: 'i$', to: '' } } },
    ],
  },
  stemChangeKinds: {},
  stemChanging: {},
  separable: [],
  irregulars: {},
  commonVerbs: [],
};

const I = data.irregulars;
const E_IMPF = ['evo', 'evi', 'eva', 'evamo', 'evate', 'evano'];
const E_CIMP = ['essi', 'essi', 'esse', 'essimo', 'este', 'essero'];

/** compact definition → partial explicit entry. ibase = stem for imperfetto / cong. imperfetto / gerundio (fare → fac) */
function mk(v, d) {
  const e = {};
  if (d.pres) e.presente = d.pres;
  if (d.pr) e.passatoRemoto = d.pr;
  if (d.ibase) {
    e.imperfetto = E_IMPF.map(x => d.ibase + x);
    e.congiuntivoImperfetto = E_CIMP.map(x => d.ibase + x);
    e.gerundio = d.ibase + 'endo';
  }
  if (d.impf) e.imperfetto = d.impf;
  if (d.cimp) e.congiuntivoImperfetto = d.cimp;
  if (d.fut) { e.futuro = fut(d.fut); e.condizionale = cond(d.fut); }
  if (d.subj) e.congiuntivoPresente = d.subj;
  if (d.imp6) e.imperativo = d.imp6;
  if (d.pp) e.participio = d.pp;
  if (d.ger) e.gerundio = d.ger;
  I[v] = e;
}

mk('essere', { pres: ['sono', 'sei', 'è', 'siamo', 'siete', 'sono'], pr: ['fui', 'fosti', 'fu', 'fummo', 'foste', 'furono'], impf: ['ero', 'eri', 'era', 'eravamo', 'eravate', 'erano'],
  fut: 'sar', subj: ['sia', 'sia', 'sia', 'siamo', 'siate', 'siano'], cimp: ['fossi', 'fossi', 'fosse', 'fossimo', 'foste', 'fossero'],
  imp6: ['—', 'sii', 'sia', 'siamo', 'siate', 'siano'], pp: 'stato', ger: 'essendo' });
mk('avere', { pres: ['ho', 'hai', 'ha', 'abbiamo', 'avete', 'hanno'], pr: prStrong('ebb', 'av'), fut: 'avr', subj: ['abbia', 'abbia', 'abbia', 'abbiamo', 'abbiate', 'abbiano'],
  imp6: ['—', 'abbi', 'abbia', 'abbiamo', 'abbiate', 'abbiano'], pp: 'avuto' });
mk('fare', { pres: ['faccio', 'fai', 'fa', 'facciamo', 'fate', 'fanno'], pr: prStrong('fec', 'fac'), ibase: 'fac', fut: 'far',
  subj: ['faccia', 'faccia', 'faccia', 'facciamo', 'facciate', 'facciano'], imp6: ['—', 'fai', 'faccia', 'facciamo', 'fate', 'facciano'], pp: 'fatto' });
mk('andare', { pres: ['vado', 'vai', 'va', 'andiamo', 'andate', 'vanno'], fut: 'andr', subj: ['vada', 'vada', 'vada', 'andiamo', 'andiate', 'vadano'],
  imp6: ['—', 'vai', 'vada', 'andiamo', 'andate', 'vadano'] });
mk('potere', { pres: ['posso', 'puoi', 'può', 'possiamo', 'potete', 'possono'], fut: 'potr', subj: ['possa', 'possa', 'possa', 'possiamo', 'possiate', 'possano'], imp6: DASH6 });
mk('volere', { pres: ['voglio', 'vuoi', 'vuole', 'vogliamo', 'volete', 'vogliono'], pr: prStrong('voll', 'vol'), fut: 'vorr',
  subj: ['voglia', 'voglia', 'voglia', 'vogliamo', 'vogliate', 'vogliano'], imp6: ['—', 'vogli', 'voglia', 'vogliamo', 'vogliate', 'vogliano'] });
mk('dovere', { pres: ['devo', 'devi', 'deve', 'dobbiamo', 'dovete', 'devono'], pr: ['dovetti', 'dovesti', 'dovette', 'dovemmo', 'doveste', 'dovettero'], fut: 'dovr',
  subj: ['debba', 'debba', 'debba', 'dobbiamo', 'dobbiate', 'debbano'], imp6: DASH6 });
mk('sapere', { pres: ['so', 'sai', 'sa', 'sappiamo', 'sapete', 'sanno'], pr: prStrong('sepp', 'sap'), fut: 'sapr',
  subj: ['sappia', 'sappia', 'sappia', 'sappiamo', 'sappiate', 'sappiano'], imp6: ['—', 'sappi', 'sappia', 'sappiamo', 'sappiate', 'sappiano'] });
mk('dire', { pres: ['dico', 'dici', 'dice', 'diciamo', 'dite', 'dicono'], pr: prStrong('diss', 'dic'), ibase: 'dic', fut: 'dir',
  subj: ['dica', 'dica', 'dica', 'diciamo', 'diciate', 'dicano'], imp6: ['—', "di'", 'dica', 'diciamo', 'dite', 'dicano'], pp: 'detto' });
mk('venire', { pres: ['vengo', 'vieni', 'viene', 'veniamo', 'venite', 'vengono'], pr: prStrong('venn', 'ven', 'i'), fut: 'verr',
  subj: ['venga', 'venga', 'venga', 'veniamo', 'veniate', 'vengano'], pp: 'venuto' });
mk('stare', { pres: ['sto', 'stai', 'sta', 'stiamo', 'state', 'stanno'], pr: ['stetti', 'stesti', 'stette', 'stemmo', 'steste', 'stettero'], fut: 'star',
  subj: ['stia', 'stia', 'stia', 'stiamo', 'stiate', 'stiano'], cimp: ['stessi', 'stessi', 'stesse', 'stessimo', 'steste', 'stessero'],
  imp6: ['—', 'stai', 'stia', 'stiamo', 'state', 'stiano'] });
mk('dare', { pres: ['do', 'dai', 'dà', 'diamo', 'date', 'danno'], pr: ['diedi', 'desti', 'diede', 'demmo', 'deste', 'diedero'], fut: 'dar',
  subj: ['dia', 'dia', 'dia', 'diamo', 'diate', 'diano'], cimp: ['dessi', 'dessi', 'desse', 'dessimo', 'deste', 'dessero'],
  imp6: ['—', 'dai', 'dia', 'diamo', 'date', 'diano'] });
mk('vedere', { pr: prStrong('vid', 'ved'), fut: 'vedr', pp: 'visto' });
mk('uscire', { pres: ['esco', 'esci', 'esce', 'usciamo', 'uscite', 'escono'], subj: ['esca', 'esca', 'esca', 'usciamo', 'usciate', 'escano'] });
mk('bere', { pres: ['bevo', 'bevi', 'beve', 'beviamo', 'bevete', 'bevono'], pr: prStrong('bevv', 'bev'), ibase: 'bev', fut: 'berr', subj: subjA('bev'), pp: 'bevuto' });
mk('tenere', { pres: ['tengo', 'tieni', 'tiene', 'teniamo', 'tenete', 'tengono'], pr: prStrong('tenn', 'ten'), fut: 'terr', subj: ['tenga', 'tenga', 'tenga', 'teniamo', 'teniate', 'tengano'] });
mk('rimanere', { pres: ['rimango', 'rimani', 'rimane', 'rimaniamo', 'rimanete', 'rimangono'], pr: prStrong('rimas', 'riman'), fut: 'rimarr',
  subj: ['rimanga', 'rimanga', 'rimanga', 'rimaniamo', 'rimaniate', 'rimangano'], pp: 'rimasto' });
mk('salire', { pres: ['salgo', 'sali', 'sale', 'saliamo', 'salite', 'salgono'], subj: ['salga', 'salga', 'salga', 'saliamo', 'saliate', 'salgano'] });
mk('morire', { pres: ['muoio', 'muori', 'muore', 'moriamo', 'morite', 'muoiono'], subj: ['muoia', 'muoia', 'muoia', 'moriamo', 'moriate', 'muoiano'], pp: 'morto' });
mk('sedere', { pres: ['siedo', 'siedi', 'siede', 'sediamo', 'sedete', 'siedono'], subj: ['sieda', 'sieda', 'sieda', 'sediamo', 'sediate', 'siedano'] });
mk('piacere', { pres: ['piaccio', 'piaci', 'piace', 'piacciamo', 'piacete', 'piacciono'], pr: prStrong('piacqu', 'piac'), subj: ['piaccia', 'piaccia', 'piaccia', 'piacciamo', 'piacciate', 'piacciano'], pp: 'piaciuto' });
mk('tacere', { pres: ['taccio', 'taci', 'tace', 'tacciamo', 'tacete', 'tacciono'], pr: prStrong('tacqu', 'tac'), subj: ['taccia', 'taccia', 'taccia', 'tacciamo', 'tacciate', 'tacciano'], pp: 'taciuto' });
mk('giacere', { pres: ['giaccio', 'giaci', 'giace', 'giacciamo', 'giacete', 'giacciono'], pr: prStrong('giacqu', 'giac'), subj: ['giaccia', 'giaccia', 'giaccia', 'giacciamo', 'giacciate', 'giacciano'], pp: 'giaciuto' });
mk('cadere', { pr: prStrong('cadd', 'cad'), fut: 'cadr' });
mk('parere', { pres: ['paio', 'pari', 'pare', 'paiamo', 'parete', 'paiono'], pr: prStrong('parv', 'par'), fut: 'parr', subj: ['paia', 'paia', 'paia', 'paiamo', 'paiate', 'paiano'], imp6: DASH6, pp: 'parso' });
mk('valere', { pres: ['valgo', 'vali', 'vale', 'valiamo', 'valete', 'valgono'], pr: prStrong('vals', 'val'), fut: 'varr', subj: ['valga', 'valga', 'valga', 'valiamo', 'valiate', 'valgano'], pp: 'valso' });
mk('vivere', { pr: prStrong('viss', 'viv'), fut: 'vivr', pp: 'vissuto' });
mk('conoscere', { pr: prStrong('conobb', 'conosc'), pp: 'conosciuto' });
mk('nascere', { pr: prStrong('nacqu', 'nasc'), pp: 'nato' });
mk('crescere', { pr: prStrong('crebb', 'cresc'), pp: 'cresciuto' });
mk('cuocere', { pres: ['cuocio', 'cuoci', 'cuoce', 'cuociamo', 'cuocete', 'cuociono'], pr: prStrong('coss', 'cuoc'), subj: ['cuocia', 'cuocia', 'cuocia', 'cuociamo', 'cuociate', 'cuociano'], pp: 'cotto' });
mk('udire', { pres: ['odo', 'odi', 'ode', 'udiamo', 'udite', 'odono'], subj: ['oda', 'oda', 'oda', 'udiamo', 'udiate', 'odano'] });
mk('apparire', { pres: ['appaio', 'appari', 'appare', 'appariamo', 'apparite', 'appaiono'], pr: prStrong('apparv', 'appar', 'i'), subj: ['appaia', 'appaia', 'appaia', 'appariamo', 'appariate', 'appaiano'], pp: 'apparso' });
mk('spegnere', { pres: ['spengo', 'spegni', 'spegne', 'spegniamo', 'spegnete', 'spengono'], pr: prStrong('spens', 'spegn'), subj: ['spenga', 'spenga', 'spenga', 'spegniamo', 'spegniate', 'spengano'], pp: 'spento' });
// -ere verbs: strong passato remoto + irregular participle, everything else regular
for (const [v, irr, pp] of [
  ['prendere', 'pres', 'preso'], ['mettere', 'mis', 'messo'], ['leggere', 'less', 'letto'], ['scrivere', 'scriss', 'scritto'], ['chiedere', 'chies', 'chiesto'],
  ['rispondere', 'rispos', 'risposto'], ['decidere', 'decis', 'deciso'], ['chiudere', 'chius', 'chiuso'], ['perdere', 'pers', 'perso'], ['correre', 'cors', 'corso'],
  ['rompere', 'rupp', 'rotto'], ['vincere', 'vins', 'vinto'], ['piangere', 'pians', 'pianto'], ['ridere', 'ris', 'riso'], ['muovere', 'moss', 'mosso'],
  ['giungere', 'giuns', 'giunto'], ['accendere', 'acces', 'acceso'], ['scendere', 'sces', 'sceso'], ['discutere', 'discuss', 'discusso'], ['distinguere', 'distins', 'distinto'],
  ['dirigere', 'diress', 'diretto'], ['esprimere', 'espress', 'espresso'], ['nascondere', 'nascos', 'nascosto'], ['succedere', 'success', 'successo'], ['dipingere', 'dipins', 'dipinto'],
  ['spingere', 'spins', 'spinto'], ['stringere', 'strins', 'stretto'], ['volgere', 'vols', 'volto'], ['raggiungere', 'raggiuns', 'raggiunto'], ['aggiungere', 'aggiuns', 'aggiunto'],
  ['assumere', 'assuns', 'assunto'], ['dividere', 'divis', 'diviso'], ['uccidere', 'uccis', 'ucciso'], ['concludere', 'conclus', 'concluso'], ['escludere', 'esclus', 'escluso'],
  ['includere', 'inclus', 'incluso'], ['persuadere', 'persuas', 'persuaso'], ['spendere', 'spes', 'speso'], ['difendere', 'difes', 'difeso'], ['offendere', 'offes', 'offeso'],
  ['attendere', 'attes', 'atteso'], ['pretendere', 'pretes', 'preteso'], ['intendere', 'intes', 'inteso'], ['rendere', 'res', 'reso'], ['dipendere', 'dipes', 'dipeso'],
  ['sospendere', 'sospes', 'sospeso'], ['proteggere', 'protess', 'protetto'], ['friggere', 'friss', 'fritto'], ['cogliere_', 'cols', 'colto'], ['fingere', 'fins', 'finto'],
  ['tingere', 'tins', 'tinto'], ['sconfiggere', 'sconfiss', 'sconfitto'], ['ammettere', 'ammis', 'ammesso'], ['permettere', 'permis', 'permesso'], ['promettere', 'promis', 'promesso'],
  ['smettere', 'smis', 'smesso'], ['trasmettere', 'trasmis', 'trasmesso'], ['commettere', 'commis', 'commesso'], ['scommettere', 'scommis', 'scommesso'], ['comprendere', 'compres', 'compreso'],
  ['apprendere', 'appres', 'appreso'], ['riprendere', 'ripres', 'ripreso'], ['sorprendere', 'sorpres', 'sorpreso'], ['occorrere', 'occors', 'occorso'], ['trascorrere', 'trascors', 'trascorso'],
  ['percorrere', 'percors', 'percorso'], ['rivolgere', 'rivols', 'rivolto'], ['svolgere', 'svols', 'svolto'], ['coinvolgere', 'coinvols', 'coinvolto'], ['descrivere', 'descriss', 'descritto'],
  ['iscrivere', 'iscriss', 'iscritto'], ['eleggere', 'eless', 'eletto'], ['sorridere', 'sorris', 'sorriso'], ['sopravvivere_', 'sopravviss', 'sopravvissuto'], ['riconoscere', 'riconobb', 'riconosciuto'],
  ['rinascere_', 'rinacqu', 'rinato'], ['interrompere', 'interrupp', 'interrotto'], ['corrompere', 'corrupp', 'corrotto'], ['convincere', 'convins', 'convinto'], ['condividere', 'condivis', 'condiviso'],
  ['riscuotere', 'riscoss', 'riscosso'], ['scuotere', 'scoss', 'scosso'], ['ridurre_', 'riduss', 'ridotto'], ['esistere_', 'x', 'x'], ['invadere', 'invas', 'invaso'], ['ardere', 'ars', 'arso'],
  ['emergere', 'emers', 'emerso'], ['immergere', 'immers', 'immerso'], ['espellere', 'espuls', 'espulso'], ['appendere', 'appes', 'appeso'], ['tendere', 'tes', 'teso'],
  ['estendere', 'estes', 'esteso'], ['mordere', 'mors', 'morso'], ['porgere', 'pors', 'porto'], ['sorgere', 'sors', 'sorto'], ['accorgersi_', 'x', 'x'], ['spargere', 'spars', 'sparso'],
  ['fondere', 'fus', 'fuso'], ['confondere', 'confus', 'confuso'], ['diffondere', 'diffus', 'diffuso'], ['assistere', 'assistett', 'assistito'], ['insistere', 'insistett', 'insistito'],
  ['esistere', 'esistett', 'esistito'], ['resistere', 'resistett', 'resistito'], ['consistere', 'consistett', 'consistito'],
]) {
  if (v.endsWith('_')) continue;
  mk(v, { pr: prStrong(irr, v.slice(0, -3)), pp });
}
// -gliere family
for (const [v, s] of [['cogliere', 'col'], ['scegliere', 'scel'], ['togliere', 'tol'], ['raccogliere', 'raccol'], ['accogliere', 'accol'], ['sciogliere', 'sciol']]) {
  const r = v.slice(0, -3); // cogli
  mk(v, { pres: [s + 'go', r, r + 'e', r + 'amo', r + 'ete', s + 'gono'], pr: prStrong(s + 's', r), subj: [s + 'ga', s + 'ga', s + 'ga', r + 'amo', r + 'ate', s + 'gano'], pp: s + 'to' });
}
// -urre (tradurre → traduc-), -orre (porre → pon-), -arre (trarre → tra-): no regular class → every tense explicit
for (const v of ['tradurre', 'condurre', 'produrre', 'ridurre', 'introdurre', 'sedurre']) {
  const b = v.slice(0, -4) + 'uc'; // traduc
  mk(v, { pres: [b + 'o', b + 'i', b + 'e', b + 'iamo', b + 'ete', b + 'ono'], pr: prStrong(b.slice(0, -1) + 'ss', b), ibase: b, fut: v.slice(0, -1), subj: subjA(b), pp: b.slice(0, -1) + 'tto' });
  I[v].imperativo = ['—', b + 'i', b + 'a', b + 'iamo', b + 'ete', b + 'ano'];
}
for (const [v, p] of [['porre', ''], ['comporre', 'com'], ['proporre', 'pro'], ['supporre', 'sup'], ['disporre', 'dis'], ['esporre', 'es'], ['imporre', 'im'], ['opporre', 'op']]) {
  const b = p + 'pon';
  mk(v, { pres: [p + 'pongo', b + 'i', b + 'e', b + 'iamo', b + 'ete', p + 'pongono'], pr: prStrong(p + 'pos', b), ibase: b, fut: p + 'porr',
    subj: [p + 'ponga', p + 'ponga', p + 'ponga', b + 'iamo', b + 'iate', p + 'pongano'], pp: p + 'posto' });
  I[v].imperativo = ['—', b + 'i', p + 'ponga', b + 'iamo', b + 'ete', p + 'pongano'];
}
for (const [v, p] of [['trarre', ''], ['attrarre', 'at'], ['distrarre', 'dis'], ['sottrarre', 'sot']]) {
  const b = p + 'tra';
  mk(v, { pres: [b + 'ggo', b + 'i', b + 'e', b + 'iamo', b + 'ete', b + 'ggono'], pr: prStrong(b + 'ss', b), ibase: b, fut: b + 'rr',
    subj: [b + 'gga', b + 'gga', b + 'gga', b + 'iamo', b + 'iate', b + 'ggano'], pp: b + 'tto' });
  I[v].imperativo = ['—', b + 'i', b + 'gga', b + 'iamo', b + 'ete', b + 'ggano'];
}
// irregular participles only
for (const [v, pp] of [['aprire', 'aperto'], ['offrire', 'offerto'], ['soffrire', 'sofferto'], ['coprire', 'coperto'], ['scoprire', 'scoperto'], ['riaprire', 'riaperto']]) mk(v, { pp });
// stressed-i -iare verbs keep the i
for (const v of ['inviare', 'sciare', 'spiare', 'avviare', 'rinviare']) {
  const s = v.slice(0, -3); // invi
  mk(v, { pres: [s + 'o', s + 'i', s + 'a', s + 'amo', s + 'ate', s + 'ano'], subj: [s + 'i', s + 'i', s + 'i', s + 'amo', s + 'ate', s + 'ino'] });
}
// prefixed derivatives
const D = (list, base) => { for (const [v, p] of list) derive(I, data, v, base, p); };
D([['rifare', 'ri'], ['disfare', 'dis']], 'fare');
D([['ridire', 'ri'], ['contraddire', 'contrad'], ['predire', 'pre'], ['benedire', 'bene'], ['maledire', 'male']], 'dire');
for (const v of ['contraddire', 'predire', 'benedire', 'maledire']) I[v].imperativo[1] = v.slice(0, -2) + 'ci'; // benedici (not benedi')
D([['divenire', 'di'], ['intervenire', 'inter'], ['provenire', 'pro'], ['svenire', 's'], ['convenire', 'con'], ['prevenire', 'pre']], 'venire');
D([['ottenere', 'ot'], ['mantenere', 'man'], ['contenere', 'con'], ['appartenere', 'appar'], ['sostenere', 'sos'], ['ritenere', 'ri'], ['trattenere', 'trat']], 'tenere');
D([['rivedere', 'ri'], ['prevedere', 'pre'], ['provvedere', 'prov']], 'vedere');
D([['riuscire', 'ri']], 'uscire');
D([['dispiacere', 'dis'], ['compiacere', 'com']], 'piacere');
D([['possedere', 'pos']], 'sedere');
D([['risalire', 'ri']], 'salire');
D([['scomparire', 'scom'], ['comparire', 'com']], 'apparire');
D([['ricadere', 'ri'], ['accadere', 'ac'], ['decadere', 'de']], 'cadere');
D([['rimettere', 'ri'], ['sottomettere', 'sotto'], ['emettere', 'e']], 'mettere');
D([['riaccendere', 'ri']], 'accendere');
D([['ridare', 'ri']], 'dare');
D([['sopravvivere', 'soprav'], ['rivivere', 'ri'], ['convivere', 'con']], 'vivere');
D([['rinascere', 'ri']], 'nascere');
D([['riprodurre_', 'ri']], 'produrre'); delete I['riprodurre_'];
D([['sostare_', 'so']], 'stare'); delete I['sostare_'];
D([['ricorrere', 'ri'], ['soccorrere', 'soc'], ['concorrere', 'con'], ['incorrere', 'in']], 'correre');
D([['rileggere', 'ri']], 'leggere');
D([['richiedere', 'ri']], 'chiedere');
D([['rinchiudere', 'rin'], ['schiudere', 's'], ['racchiudere', 'rac']], 'chiudere');
D([['sorridere_', 'sor']], 'ridere'); delete I['sorridere_'];
D([['riscrivere', 'ri'], ['sottoscrivere', 'sotto'], ['trascrivere', 'tra']], 'scrivere');
D([['accorrere', 'ac']], 'correre');
D([['rinvenire_', 'rin']], 'venire'); delete I['rinvenire_'];
D([['ricevere_', 'ri']], 'bere'); delete I['ricevere_'];

data.commonVerbs = [
  'essere', 'avere', 'fare', 'dire', 'potere', 'volere', 'sapere', 'stare', 'dovere', 'vedere', 'andare', 'venire', 'dare', 'parlare', 'trovare', 'sentire', 'lasciare', 'prendere', 'guardare', 'mettere',
  'pensare', 'passare', 'credere', 'portare', 'arrivare', 'chiamare', 'tornare', 'sembrare', 'uscire', 'tenere', 'capire', 'chiedere', 'morire', 'restare', 'vivere', 'entrare', 'conoscere', 'cercare', 'rimanere', 'aspettare',
  'cominciare', 'finire', 'diventare', 'ricordare', 'lavorare', 'riuscire', 'scrivere', 'leggere', 'mangiare', 'bere', 'dormire', 'aprire', 'chiudere', 'perdere', 'correre', 'giocare', 'pagare', 'comprare', 'vendere', 'piacere',
  'servire', 'partire', 'salire', 'scendere', 'cadere', 'rispondere', 'decidere', 'bastare', 'nascere', 'crescere', 'offrire', 'succedere', 'muovere', 'seguire', 'continuare', 'provare', 'usare', 'spiegare', 'studiare', 'imparare',
  'insegnare', 'cantare', 'ballare', 'nuotare', 'camminare', 'viaggiare', 'visitare', 'abitare', 'cucinare', 'preparare', 'pulire', 'lavare', 'vestire', 'svegliare', 'alzare', 'sedere', 'telefonare', 'incontrare', 'invitare', 'ringraziare',
  'aiutare', 'amare', 'odiare', 'desiderare', 'preferire', 'sperare', 'temere', 'ridere', 'piangere', 'sorridere', 'gridare', 'ascoltare', 'rompere', 'vincere', 'accendere', 'spegnere', 'discutere', 'distinguere', 'dirigere', 'esprimere',
  'nascondere', 'dipingere', 'spingere', 'stringere', 'volgere', 'raggiungere', 'aggiungere', 'assumere', 'dividere', 'uccidere', 'concludere', 'escludere', 'includere', 'persuadere', 'spendere', 'difendere', 'offendere', 'attendere', 'pretendere', 'intendere',
  'rendere', 'dipendere', 'sospendere', 'proteggere', 'friggere', 'cogliere', 'scegliere', 'togliere', 'raccogliere', 'accogliere', 'sciogliere', 'tradurre', 'condurre', 'produrre', 'ridurre', 'introdurre', 'porre', 'comporre', 'proporre', 'supporre',
  'disporre', 'esporre', 'imporre', 'opporre', 'trarre', 'attrarre', 'distrarre', 'soffrire', 'coprire', 'scoprire', 'inviare', 'sciare', 'rifare', 'contraddire', 'benedire', 'divenire', 'intervenire', 'ottenere', 'mantenere', 'contenere',
  'appartenere', 'sostenere', 'ritenere', 'rivedere', 'prevedere', 'provvedere', 'dispiacere', 'possedere', 'scomparire', 'apparire', 'accadere', 'permettere', 'promettere', 'ammettere', 'smettere', 'trasmettere', 'commettere', 'comprendere', 'apprendere', 'riprendere',
  'sorprendere', 'occorrere', 'trascorrere', 'percorrere', 'rivolgere', 'svolgere', 'coinvolgere', 'descrivere', 'iscrivere', 'eleggere', 'riconoscere', 'interrompere', 'convincere', 'condividere', 'scuotere', 'invadere', 'emergere', 'appendere', 'tendere', 'estendere',
  'mordere', 'sorgere', 'fondere', 'confondere', 'diffondere', 'assistere', 'insistere', 'esistere', 'resistere', 'consistere', 'fingere', 'valere', 'parere', 'tacere', 'cuocere', 'udire', 'spedire', 'restituire', 'unire', 'riunire',
  'obbedire', 'proibire', 'colpire', 'stabilire', 'fornire', 'garantire', 'guarire', 'impedire', 'inserire', 'punire', 'riferire', 'sostituire', 'suggerire', 'chiarire', 'definire', 'distribuire', 'agire', 'reagire', 'ferire', 'gradire',
  'favorire', 'contribuire', 'digerire', 'arrossire', 'tradire', 'diminuire', 'attribuire', 'trasferire', 'sparire', 'approfondire', 'condire', 'dimagrire', 'ingrandire', 'intuire', 'costruire', 'costituire', 'percepire', 'subire', 'fallire', 'fiorire',
  'impazzire', 'nutrire', 'seppellire', 'tossire', 'esaurire', 'abolire', 'accettare', 'accompagnare', 'affittare', 'allontanare', 'amministrare', 'annunciare', 'applicare', 'arrabbiare', 'arredare', 'assaggiare', 'attaccare', 'attraversare', 'aumentare', 'avvisare',
  'baciare', 'bruciare', 'buttare', 'calcolare', 'cambiare', 'caricare', 'causare', 'celebrare', 'cenare', 'cercare_', 'chiacchierare', 'colpire_', 'comandare', 'commentare', 'comunicare', 'confermare', 'conservare', 'consigliare', 'contare', 'controllare',
  'conversare', 'costare', 'creare', 'criticare', 'curare', 'dimenticare', 'dimostrare', 'disegnare', 'domandare', 'donare', 'durare', 'entrare_', 'evitare', 'festeggiare', 'fermare', 'firmare', 'formare', 'fumare', 'funzionare', 'girare',
  'gettare', 'giudicare', 'guadagnare', 'guidare', 'immaginare', 'indicare', 'informare', 'iniziare', 'interessare', 'lamentare', 'lanciare', 'legare', 'liberare', 'limitare', 'litigare', 'mancare', 'mandare', 'meritare', 'misurare', 'mostrare',
  'noleggiare', 'notare', 'occupare', 'ordinare', 'organizzare', 'osservare', 'parcheggiare', 'partecipare', 'pensare_', 'pesare', 'pettinare', 'pranzare', 'pregare', 'prenotare', 'presentare', 'prestare', 'provare_', 'raccontare', 'regalare', 'respirare',
  'ricevere', 'ripetere', 'riposare', 'risparmiare', 'rischiare', 'ritornare', 'salutare', 'saltare', 'salvare', 'sbagliare', 'scappare', 'scherzare', 'scusare', 'significare', 'sognare', 'sperare_', 'spiegare_', 'sposare', 'stampare', 'tagliare',
  'tirare', 'toccare', 'trattare', 'trovare_', 'urlare', 'vietare', 'volare', 'votare', 'battere', 'cedere', 'combattere', 'credere_', 'dibattere', 'godere', 'premere', 'ricevere_', 'ripetere_', 'temere_', 'vendere_', 'abbattere',
  'avvertire', 'bollire', 'convertire', 'cucire', 'divertire', 'fuggire', 'inseguire', 'mentire', 'offrire_', 'partire_', 'pentire', 'scoprire_', 'seguire_', 'sentire_', 'soffrire_', 'vestire_', 'investire', 'conseguire', 'proseguire', 'assaltare',
  'abbracciare', 'allenare', 'annoiare', 'appoggiare', 'asciugare', 'avanzare', 'avvicinare', 'cacciare', 'cercare__', 'consegnare', 'contattare', 'copiare', 'crollare', 'danneggiare', 'dedicare', 'destinare', 'dividere_', 'elencare', 'esagerare', 'esaminare',
].filter(v => !v.includes('_'));

finish(data, ['essere', 'avere', 'fare', 'andare', 'potere', 'volere', 'dovere', 'sapere', 'dire', 'venire', 'stare', 'dare', 'vedere', 'uscire', 'bere']);
