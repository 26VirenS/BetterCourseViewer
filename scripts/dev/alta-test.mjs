// Simpl for Mac 1.3.17: the Knewton Alta frame's page script (mac/Simpl/Shell/AltaSkin.swift, AltaHook.source), run in
// a stand-in page. It must pass the app the assignment, its objectives and their mastery from Alta's content answer;
// never the answer key or who the student is; leave Alta's own requests and answers as they were; and do nothing at all
// on any page but Alta's.
//   node scripts/dev/alta-test.mjs
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const swift = readFileSync(new URL('../../mac/Simpl/Shell/AltaSkin.swift', import.meta.url), 'utf8');
const m = /static let source = #"""\n([\s\S]*?)\n\s*"""#/.exec(swift);
if (!m) { console.error('AltaHook.source not found in AltaSkin.swift'); process.exit(1); }
const source = m[1];

let failed = 0;
const check = (ok, what) => { console.log(`  ${ok ? '✓' : '✗'} ${what}`); if (!ok) failed++; };

// Alta's content answer, shaped as captured — with an answer key and the student's ids in it on purpose
const CONTENT = {
  assignmentId: 'a1',
  states: [{
    atom: {
      name: 'Graph piecewise functions', purpose: 'ASSESSES', dataType: 'LEARNOSITY_GENERIC_QUESTION', learningObjectiveId: 'lo2',
      data: { question: '<p>Q</p>', content: { type: 'custom', custom_type: 'desmos_blank_graph_question', correct_answer: { a: 3, s_1: 1 }, success_condition: 'a=3' } },
      learningObjective: { description: 'Graph piecewise functions', estimatedQuestionsLow: 4, estimatedQuestionsHigh: 10 },
    },
    compoundInstance: { state: 'SHOWN', type: 'ASSESS', source: 'PRACTICE', userId: 'student-secret-id', registrationId: 'reg-secret' },
  }],
  enrollment: {
    path: { name: 'Linear, Polynomial, and Piecewise Functions', type: 'ADAPTIVE', masteryThreshold: 100, ended: false,
      pathLearningObjectives: [{ learningObjectiveId: 'lo1', description: 'Identify linear functions' }, { learningObjectiveId: 'lo2' }, { learningObjectiveId: 'lo3' }] },
    startedAt: 1, completed: false, dueDate: { effectiveDueDate: 1760000000000, lateSubmissionEnabled: true },
    ltiEnrollment: { resultSourcedId: 'lti-secret', returnUrl: 'https://school.example/return' },
  },
  history: { sequences: [{ numCorrectResponses: 1, numIncorrectResponses: 0 }, { numCorrectResponses: 0, numIncorrectResponses: 1 }] },
  analytics: { percentComplete: 62, statusAndProgress: { status: 'in_progress', progress: 0.62, targets: [{ target_id: 'lref-lo1', progress: 1, status: 'complete' }, { target_id: 'lref-lo2', progress: 0.55 }] } },
  stuckLo: null,
};
const SECRETS = ['student-secret-id', 'reg-secret', 'lti-secret', 'school.example', 'correct_answer', 'success_condition', 'a=3', 's_1'];

/** A page at `href`, the script run in it, Alta's content answer fetched (and asked for by XHR too). What it posted. */
async function page(href) {
  const url = new URL(href);
  const posted = [];
  const body = JSON.stringify(CONTENT);
  const realFetch = async (u) => new Response(body, { headers: { 'content-type': 'application/json' } });
  Object.defineProperty(realFetch, 'name', { value: 'realFetch' });
  class XHR {
    constructor() { this.listeners = {}; this.responseType = ''; }
    open(method, u) { this.u = u; }
    addEventListener(k, f) { (this.listeners[k] ||= []).push(f); }
    send() { this.responseText = body; this.responseURL = new URL(this.u, url).href; queueMicrotask(() => (this.listeners.load || []).forEach((f) => f())); }
  }
  const history = { pushState() {}, replaceState() {} };
  const win = {
    location: { hostname: url.hostname, pathname: url.pathname },
    fetch: realFetch, XMLHttpRequest: XHR, history, Response, JSON, setTimeout, queueMicrotask,
    addEventListener() {},
    webkit: { messageHandlers: { simplAlta: { postMessage: (s) => posted.push(s) } } },
  };
  win.window = win;
  vm.createContext(win);
  vm.runInContext(source, win);
  const r = await win.fetch(new URL('/learn/api/content?x=1', url).href);
  const fromAlta = await r.json(); // (what Alta's own page reads from its answer)
  const x = new win.XMLHttpRequest();
  x.open('GET', '/learn/api/content?x=2');
  x.send();
  await new Promise((done) => setTimeout(done, 20));
  return { posted, fromAlta, fetchSwapped: win.fetch !== realFetch };
}

console.log('the Alta frame’s page script');
{
  const { posted, fromAlta } = await page('https://www.knewtonalta.com/learn/course/c1/assignment/a1/practice');
  const content = posted.map((s) => JSON.parse(s)).filter((p) => p.kind === 'content');
  const where = posted.map((s) => JSON.parse(s)).find((p) => p.kind === 'page');
  check(where?.assignment === true, 'on an Alta assignment it says the page is an assignment’s player');
  check(content.length === 2, `Alta’s content answer is read, by fetch and by XHR alike (${content.length})`);
  const c = content[0] || {};
  check(c.name === 'Linear, Polynomial, and Piecewise Functions' && c.threshold === 100 && c.percent === 62 && c.status === 'in_progress', 'it passes the assignment, its threshold and its mastery');
  check(c.objectives?.length === 3 && c.objectives[0].name === 'Identify linear functions' && c.targets?.[1]?.id === 'lo2' && c.targets?.[1]?.progress === 0.55, 'and each objective, with its mastery (lref- taken off the target’s id)');
  check(c.current?.id === 'lo2' && c.current?.name === 'Graph piecewise functions' && c.current?.low === 4 && c.current?.high === 10 && c.current?.source === 'PRACTICE' && c.current?.item === 'desmos_blank_graph_question', 'and the objective on screen, its estimate, practice mode, and the item’s kind');
  check(c.history?.length === 2 && c.history[0].right === 1 && c.history[1].wrong === 1, 'and how the last answers went');
  const all = posted.join('\n');
  const leaked = SECRETS.filter((s) => all.includes(s));
  check(!leaked.length, `never the answer key, the student’s ids or the school’s launch data (${leaked.join(', ') || 'none'})`);
  check(JSON.stringify(fromAlta) === JSON.stringify(CONTENT), 'Alta’s own page reads its answer whole, as it was sent');
}
{
  const { posted, fetchSwapped } = await page('https://school.instructure.com/courses/1/assignments/2');
  check(posted.length === 0 && !fetchSwapped, 'on any other site it does nothing: nothing passed, fetch left as it was');
}
{
  const { posted } = await page('http://localhost:8795/mock-alta/learn/course/c1/assignment/a1/practice');
  check(posted.some((s) => JSON.parse(s).kind === 'content'), 'the mock Alta page the screenshots use counts as Alta');
}

console.log(failed ? `\n${failed} failed` : '\nAll checks passed.');
process.exit(failed ? 1 : 0);
