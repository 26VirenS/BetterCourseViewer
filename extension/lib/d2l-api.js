/* Brightspace (D2L), answering in Canvas's language. The interface was written against Canvas's REST API — every
 * screen, the store, the apps' native screens through native-app.js — and on a Brightspace page every request it
 * makes through lib/canvas-api.js comes here instead: the Canvas address and its parameters are read, Brightspace's
 * own API is asked (Valence: /d2l/api/lp/… the platform — who you are, your courses; /d2l/api/le/… the learning
 * tools — assignments (dropbox folders), quizzes, grades, announcements (news), discussions, content, the calendar,
 * the classlist), and the answer is shaped as Canvas would have given it. The screens never know the difference.
 *
 * What Brightspace keeps nothing of — a course's nickname and colour, the student's own tasks, a tick on a piece of
 * work — is kept in the extension's storage, per site, and answered from there as Canvas's planner would.
 *
 * Ids: Canvas puts assignments, quizzes, discussions and announcements in shared id spaces (an assignment has a
 * quiz's, a graded discussion's); Brightspace numbers each tool on its own. So each gets a range of its own here — a
 * dropbox folder is its own id, a quiz 1e9 + its id, a discussion topic 2e9 + its id, an announcement 3e9 + its id,
 * a grade item that is none of those 4e9 + its id — and every address the interface builds from them
 * (/courses/<ou>/assignments/<id>) is read back the same way. Content topics (modules, pages, files) keep theirs. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  if (!BCV.lms?.d2l || BCV.d2l) return;
  const api = BCV.api;
  const MIN = 60e3;

  // ---- the page: who is signed in, and where ---------------------------------------------------------------
  // Every Brightspace page carries them on <html data-global-context="{orgUnitId, orgId, userId}">.
  function pageContext() {
    try { return JSON.parse(document.documentElement.getAttribute('data-global-context') || '{}') || {}; } catch { return {}; }
  }
  const page = pageContext();

  const err = (message, status) => {
    const E = BCV.canvas?.CanvasError;
    return E ? new E(message, status) : Object.assign(new Error(message), { status });
  };

  // ---- requests: a few at once, each with a time limit ----------------------------------------------------
  const MAX_OPEN = 6;
  const TIMEOUT = 25000;
  let open = 0;
  const waiting = [];
  const take = () => (open < MAX_OPEN ? (open++, Promise.resolve()) : new Promise((r) => waiting.push(r)));
  const give = () => { const next = waiting.shift(); if (next) next(); else open--; };

  /** The token Brightspace asks of every write (the X-Csrf-Token header), as its own pages fetch it. */
  let xsrf = null;
  async function token(fresh = false) {
    if (xsrf && !fresh) return xsrf;
    const r = await fetch('/d2l/lp/auth/xsrf-tokens', { credentials: 'same-origin', cache: 'no-store', headers: { accept: 'application/json' } });
    const j = await r.json().catch(() => ({}));
    xsrf = j?.referrerToken || '';
    return xsrf;
  }

  const signedOut = (res) => res.status === 401 || (res.redirected && /\/d2l\/(login|lp\/auth)/i.test(res.url || ''));

  async function once(method, path, { json, form, formType = null, blob = false } = {}) {
    await take();
    let res, text;
    const ac = typeof AbortController === 'function' ? new AbortController() : null;
    const timer = ac ? setTimeout(() => ac.abort(), TIMEOUT) : null;
    try {
      const headers = { accept: 'application/json' };
      let body;
      if (json !== undefined) { headers['content-type'] = 'application/json'; body = JSON.stringify(json); }
      else if (form) { body = form; if (formType) headers['content-type'] = formType; } // (said outright: a Blob's own type is lowercased, its boundary with it)
      if (method !== 'GET') headers['x-csrf-token'] = await token();
      res = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store', headers, body, signal: ac ? ac.signal : undefined });
      if (blob && res.ok) return res;
      text = await res.text();
    } catch (e) {
      if (ac && ac.signal.aborted) throw err('Brightspace did not answer in time', 0);
      self.BCV?.errors?.failed?.('NET');
      throw e;
    } finally {
      clearTimeout(timer);
      give();
    }
    if (signedOut(res)) {
      BCV.canvas?.signedOut?.();
      throw err('Signed out of Brightspace', 401);
    }
    if (!res.ok) {
      let msg = `Brightspace ${res.status}`;
      try { const j = JSON.parse(text); msg = j?.Errors?.[0]?.Message || j?.detail || j?.title || msg; } catch { /* the status, then */ }
      throw err(msg, res.status);
    }
    if (!text || !text.trim()) return null;
    try { return JSON.parse(text); } catch { return text; }
  }
  /** A request to Brightspace; a write refused for its token is tried once more with a fresh one. */
  async function call(method, path, opts = {}) {
    try {
      return await once(method, path, opts);
    } catch (e) {
      if (method !== 'GET' && e.status === 403 && !opts.retried) {
        await token(true);
        return once(method, path, { ...opts, retried: true });
      }
      throw e;
    }
  }

  // ---- versions: the ones this was written against, else the newest the school has ------------------------------
  const PREFER = { lp: '1.50', le: '1.82' };
  let versionsRun = null;
  function versions() {
    return (versionsRun ||= (async () => {
      let list = [];
      try { list = (await once('GET', '/d2l/api/versions/')) || []; } catch { /* the preferred ones */ }
      const pick = (code) => {
        const p = Array.isArray(list) ? list.find((x) => x.ProductCode === code) : null;
        if (!p) return PREFER[code];
        const sup = p.SupportedVersions || [];
        if (sup.includes(PREFER[code])) return PREFER[code];
        return p.LatestVersion || PREFER[code];
      };
      return { lp: pick('lp'), le: pick('le') };
    })());
  }
  const LP = async (p) => `/d2l/api/lp/${(await versions()).lp}${p}`;
  const LE = async (p) => `/d2l/api/le/${(await versions()).le}${p}`;

  /** Every item of a paged answer: a PagedResultSet ({ Items, PagingInfo }) or an ObjectListPage ({ Objects, Next }). */
  async function pages(path, { max = 20 } = {}) {
    const out = [];
    let url = path;
    for (let n = 0; url && n < max; n++) {
      const got = await call('GET', url);
      if (Array.isArray(got)) { out.push(...got); break; }
      if (got && Array.isArray(got.Items)) {
        out.push(...got.Items);
        const b = got.PagingInfo?.HasMoreItems ? got.PagingInfo.Bookmark : null;
        url = b ? `${path}${path.includes('?') ? '&' : '?'}bookmark=${encodeURIComponent(b)}` : null;
        continue;
      }
      if (got && Array.isArray(got.Objects)) { out.push(...got.Objects); url = got.Next || null; continue; }
      break;
    }
    return out;
  }

  // ---- a short memo of Brightspace's answers (a screen asks for the same course's folders several ways) ----------
  const memo = new Map();
  function remember(key, ttl, fn) {
    const hit = memo.get(key);
    if (hit && hit.until > Date.now()) return hit.value;
    const value = fn();
    memo.set(key, { value, until: Date.now() + ttl });
    value.catch(() => memo.delete(key));
    return value;
  }
  const forget = (prefix) => { for (const k of [...memo.keys()]) if (k.startsWith(prefix)) memo.delete(k); };

  // ---- the student's own, kept here (Brightspace has no place for them) ------------------------------------------
  const LOCAL_KEY = `d2l:${location.host}`;
  let localRun = null;
  function local() {
    return (localRun ||= api.storage.local.get(LOCAL_KEY).catch(() => ({})).then((r) => ({
      nicknames: {}, colors: {}, notes: [], overrides: [], read: {}, seq: 1, ...((r && r[LOCAL_KEY]) || {}),
    })));
  }
  async function saveLocal(change) {
    // (read again first: another tab may have written since this one last looked)
    const fresh = await api.storage.local.get(LOCAL_KEY).catch(() => ({}));
    const l = { nicknames: {}, colors: {}, notes: [], overrides: [], read: {}, seq: 1, ...((fresh && fresh[LOCAL_KEY]) || {}) };
    const out = change(l);
    await api.storage.local.set({ [LOCAL_KEY]: l }).catch(() => {});
    localRun = Promise.resolve(l);
    return out;
  }

  // ---- id spaces -----------------------------------------------------------------------------------------------
  const SPACE = { quiz: 1e9, topic: 2e9, news: 3e9, grade: 4e9 };
  const idIn = (space, id) => String(SPACE[space] + Number(id));
  function spaceOf(raw) {
    const v = Number(raw);
    if (!Number.isFinite(v)) return ['none', raw];
    if (v >= SPACE.grade) return ['grade', String(v - SPACE.grade)];
    if (v >= SPACE.news) return ['news', String(v - SPACE.news)];
    if (v >= SPACE.topic) return ['topic', String(v - SPACE.topic)];
    if (v >= SPACE.quiz) return ['quiz', String(v - SPACE.quiz)];
    return ['dropbox', String(v)];
  }

  // ---- small helpers -------------------------------------------------------------------------------------------
  const rich = (r) => (r && typeof r === 'object' ? (r.Html || r.Text || r.Content || '') : r || '');
  const iso = (d) => (d ? new Date(d).toISOString() : null);
  const t = (d) => (d ? new Date(d).getTime() : NaN);
  const num = (v) => (v === null || v === undefined || v === '' || !Number.isFinite(Number(v)) ? null : Number(v));
  const list = (v) => (Array.isArray(v) ? v : v === undefined || v === null ? [] : [v]);
  const includes = (params, what) => list(params?.include || params?.['include[]']).includes(what);
  const contextOf = (code) => { const m = /^(course|group|user)_(\d+)$/.exec(String(code || '')); return m ? { kind: m[1], id: m[2] } : null; };
  const isTeacher = (role) => /instructor|teacher|professor|lecturer|teaching|\bta\b|faculty|administrator|designer/i.test(role || '');
  const scorePct = (g) => {
    if (!g) return null;
    if (num(g.WeightedDenominator) && num(g.WeightedNumerator) !== null) return (g.WeightedNumerator / g.WeightedDenominator) * 100;
    if (num(g.PointsDenominator) && num(g.PointsNumerator) !== null) return (g.PointsNumerator / g.PointsDenominator) * 100;
    const m = /(-?\d+(?:\.\d+)?)\s*%/.exec(g.DisplayedGrade || '');
    return m ? Number(m[1]) : null;
  };
  const letterOf = (g) => {
    const d = String(g?.DisplayedGrade || '').trim();
    return d && !/%|\d+\s*\/\s*\d+/.test(d) ? d : null;
  };

  // ---- Brightspace's answers, kept a little while -------------------------------------------------------------
  const whoami = () => remember('whoami', 30 * MIN, async () => call('GET', await LP('/users/whoami')));
  const profile = () => remember('profile', 30 * MIN, async () => call('GET', await LP('/profile/myProfile')).catch(() => null));
  const orgInfo = () => remember('org', 60 * MIN, async () => call('GET', await LP('/organization/info')).catch(() => null));
  /** The student's course offerings, as Brightspace lists their enrollments (any page, any role). */
  const enrollments = () => remember('enr', 2 * MIN, async () => {
    const all = await pages(await LP('/enrollments/myenrollments/?pageSize=100'));
    return all.filter((e) => e?.OrgUnit && (e.OrgUnit.Type?.Id === 3 || /course offering/i.test(e.OrgUnit.Type?.Code || e.OrgUnit.Type?.Name || '')));
  });
  const courseInfo = (ou) => remember(`course:${ou}`, 6 * 60 * MIN, async () => call('GET', await LP(`/courses/${ou}`)).catch(() => null));
  const folders = (ou) => remember(`folders:${ou}`, 2 * MIN, async () => list(await call('GET', await LE(`/${ou}/dropbox/folders/`)).catch(() => [])));
  const quizzes = (ou) => remember(`quizzes:${ou}`, 2 * MIN, async () => pages(await LE(`/${ou}/quizzes/`)).catch(() => []));
  const gradeObjects = (ou) => remember(`gobj:${ou}`, 5 * MIN, async () => list(await call('GET', await LE(`/${ou}/grades/`)).catch(() => [])));
  const gradeCategories = (ou) => remember(`gcat:${ou}`, 5 * MIN, async () => list(await call('GET', await LE(`/${ou}/grades/categories/`)).catch(() => [])));
  const gradeSetup = (ou) => remember(`gsetup:${ou}`, 60 * MIN, async () => call('GET', await LE(`/${ou}/grades/setup/`)).catch(() => null));
  const myGrades = (ou) => remember(`mygrades:${ou}`, MIN, async () => list(await call('GET', await LE(`/${ou}/grades/values/myGradeValues/`)).catch(() => [])));
  const myFinal = (ou) => remember(`myfinal:${ou}`, MIN, async () => call('GET', await LE(`/${ou}/grades/final/values/myGradeValue`)).catch(() => null));
  const mySubs = (ou, fid) => remember(`mysubs:${ou}:${fid}`, MIN, async () => list(await call('GET', await LE(`/${ou}/dropbox/folders/${fid}/submissions/mysubmissions/`)).catch(() => [])));
  const news = (ou) => remember(`news:${ou}`, 2 * MIN, async () => list(await call('GET', await LE(`/${ou}/news/`)).catch(() => [])));
  const forums = (ou) => remember(`forums:${ou}`, 5 * MIN, async () => list(await call('GET', await LE(`/${ou}/discussions/forums/`)).catch(() => [])));
  const topicsOf = (ou, fid) => remember(`topics:${ou}:${fid}`, 5 * MIN, async () => list(await call('GET', await LE(`/${ou}/discussions/forums/${fid}/topics/`)).catch(() => [])));
  const postsOf = (ou, fid, tid) => remember(`posts:${ou}:${fid}:${tid}`, MIN, async () => list(await call('GET', await LE(`/${ou}/discussions/forums/${fid}/topics/${tid}/posts/`)).catch(() => [])));
  const toc = (ou) => remember(`toc:${ou}`, 5 * MIN, async () => call('GET', await LE(`/${ou}/content/toc`)).catch(() => ({ Modules: [] })));
  const calendarOf = (ou) => remember(`cal:${ou}`, 5 * MIN, async () => list(await call('GET', await LE(`/${ou}/calendar/events/`)).catch(() => [])));
  const classlist = (ou) => remember(`class:${ou}`, 10 * MIN, async () => list(await call('GET', await LE(`/${ou}/classlist/`)).catch(() => [])));

  /** Every discussion topic of a course, each with its forum. */
  const allTopics = (ou) => remember(`alltopics:${ou}`, 5 * MIN, async () => {
    const fs = await forums(ou);
    const per = await Promise.all(fs.map(async (f) => (await topicsOf(ou, f.ForumId)).map((tp) => ({ ...tp, forum: f }))));
    return per.flat();
  });

  // ---- the user ------------------------------------------------------------------------------------------------
  async function userSelf() {
    const [w, p] = await Promise.all([whoami(), profile()]);
    const name = [w.FirstName, w.LastName].filter(Boolean).join(' ') || w.UniqueName || 'Account';
    return {
      id: String(w.Identifier), name, short_name: w.FirstName || name, sortable_name: [w.LastName, w.FirstName].filter(Boolean).join(', ') || name,
      login_id: w.UniqueName || null, email: p?.Email || null, pronouns: w.Pronouns || null,
      avatar_url: p?.ProfileImage?.Url || null, // (the picture's own address when there is one; none drawn as the initials)
      locale: null, effective_locale: 'en',
    };
  }

  // ---- courses -------------------------------------------------------------------------------------------------
  async function shapeCourse(e, { scores = true, info = null, setup = null } = {}) {
    const ou = String(e.OrgUnit.Id);
    const l = await local();
    const role = e.Access?.ClasslistRoleName || '';
    const teaching = isTeacher(role);
    let score = null, grade = null;
    if (scores && !teaching) {
      const f = await myFinal(ou);
      score = scorePct(f);
      grade = letterOf(f);
    }
    const name = e.OrgUnit.Name || `Course ${ou}`;
    const nick = l.nicknames[ou];
    const start = e.Access?.StartDate || info?.StartDate || null;
    const end = e.Access?.EndDate || info?.EndDate || null;
    let image = null;
    try { image = e.OrgUnit.ImageUrl ? new URL(e.OrgUnit.ImageUrl, location.origin).pathname + '?height=300&width=600' : null; } catch { /* none */ }
    return {
      id: ou, uuid: ou, name: nick || name, original_name: nick ? name : undefined, course_code: e.OrgUnit.Code || name,
      workflow_state: 'available', account_id: String(page.orgId || ''), root_account_id: String(page.orgId || ''),
      start_at: start, end_at: end, restrict_enrollments_to_course_dates: !!(start || end),
      created_at: null, enrollment_term_id: String(info?.Semester?.Identifier || '1'),
      term: { id: String(info?.Semester?.Identifier || '1'), name: info?.Semester?.Name || null, start_at: null, end_at: null }, // (Brightspace's semester)
      is_favorite: !!e.PinDate,
      image_download_url: image, course_image: image,
      enrollments: [{
        type: teaching ? 'teacher' : 'student', role: teaching ? 'TeacherEnrollment' : 'StudentEnrollment', enrollment_state: e.Access?.IsActive === false ? 'inactive' : 'active',
        computed_current_score: score, computed_current_grade: grade, computed_final_score: score, computed_final_grade: grade,
      }],
      teachers: [], sections: [], html_url: `/courses/${ou}`,
      access_restricted_by_date: e.Access?.CanAccess === false,
      default_view: 'modules', syllabus_body: null, hide_final_grades: false,
      apply_assignment_group_weights: /weighted/i.test(setup?.GradingSystem || ''), time_zone: null, calendar: { ics: null },
    };
  }
  async function courses(params) {
    const es = await enrollments();
    const live = es.filter((e) => e.Access?.CanAccess !== false);
    // (a course's own record names its semester, Brightspace's term, and its grade setup whether its categories are
    // weighted: each asked once a course, and kept)
    return Promise.all(live.map(async (e) => {
      const ou = String(e.OrgUnit.Id);
      const [info, setup] = await Promise.all([courseInfo(ou), gradeSetup(ou)]);
      return shapeCourse(e, { scores: includes(params, 'total_scores'), info, setup });
    }));
  }
  async function course(ou, params) {
    const es = await enrollments();
    const e = es.find((x) => String(x.OrgUnit.Id) === String(ou));
    if (!e) throw err('That course could not be found.', 404);
    const [info, setup] = await Promise.all([courseInfo(ou), gradeSetup(ou)]);
    const c = await shapeCourse(e, { info, setup });
    if (includes(params, 'teachers')) {
      const people = await classlist(ou);
      c.teachers = people.filter((p) => isTeacher(p.ClasslistRoleDisplayName)).map((p) => ({ id: String(p.Identifier), display_name: [p.FirstName, p.LastName].filter(Boolean).join(' ') || p.DisplayName, avatar_image_url: null }));
    }
    if (includes(params, 'syllabus_body')) c.syllabus_body = rich(info?.Description) || null;
    return c;
  }
  async function dashboardCards() {
    const [cs, l] = await Promise.all([courses(), local()]);
    return cs.filter((c) => c.is_favorite || true).map((c, i) => ({
      id: c.id, shortName: c.name, originalName: c.original_name || c.name, courseCode: c.course_code, assetString: `course_${c.id}`,
      href: c.html_url, term: c.term?.name || null, image: c.image_download_url, color: l.colors[`course_${c.id}`] || null,
      isFavorited: c.is_favorite, isHomeroom: false, enrollmentType: c.enrollments[0].role, position: i, published: true, canManage: false,
    }));
  }
  function tabs(ou) {
    const base = `/courses/${ou}`;
    return [
      ['home', 'Home', base], ['announcements', 'Announcements', `${base}/announcements`], ['assignments', 'Assignments', `${base}/assignments`],
      ['discussions', 'Discussions', `${base}/discussion_topics`], ['grades', 'Grades', `${base}/grades`], ['people', 'Classlist', `${base}/users`],
      ['quizzes', 'Quizzes', `${base}/quizzes`], ['modules', 'Content', `${base}/modules`],
    ].map(([id, label, url], i) => ({ id, label, html_url: url, full_url: location.origin + url, position: i + 1, type: 'internal', hidden: false, visibility: 'public' }));
  }

  // ---- assignments: dropbox folders, quizzes, graded discussions, and grade items of their own -------------------
  /** A course's grade items, with the student's value on each, and which tool's activity each belongs to. */
  async function gradeMap(ou) {
    const [objs, vals] = await Promise.all([gradeObjects(ou), myGrades(ou)]);
    const value = new Map(vals.map((v) => [String(v.GradeObjectIdentifier), v]));
    return { objs, byId: new Map(objs.map((o) => [String(o.Id), o])), value };
  }
  function submissionTypes(f) {
    switch (Number(f.SubmissionType)) {
      case 1: return ['online_text_entry'];
      case 2: case 3: return ['on_paper'];
      case 4: return ['online_upload', 'online_text_entry'];
      default: return ['online_upload'];
    }
  }
  function extensions(f) {
    // (Brightspace: 0 any, 1 annotatable, 2 images, 3 audio, 4 video, 5 custom)
    if (Number(f.AllowableFileType) === 5 && f.CustomAllowableFileTypes) return String(f.CustomAllowableFileTypes).split(/[,;\s]+/).map((x) => x.replace(/^\./, '')).filter(Boolean);
    return [];
  }
  /** Where a piece of work stands, as Canvas says it on its submission. */
  function shapeSubmission({ aid, due, points, value, subs = null, feedback = null, takenWhenGraded = false, as = 'online_upload' }) {
    const me = String(page.userId || '');
    const last = subs ? subs.flatMap((s) => list(s.Submissions)).filter((s) => s?.SubmissionDate).sort((a, b) => t(b.SubmissionDate) - t(a.SubmissionDate))[0] : null;
    const graded = !!(value && (num(value.PointsNumerator) !== null || (value.DisplayedGrade && String(value.DisplayedGrade).trim())));
    // (a quiz or a discussion with a grade was taken, or joined: Brightspace gives a student no list of attempts, so the
    // grade's own time stands for when — a quiz is marked as it is handed in)
    const submittedAt = last?.SubmissionDate || (takenWhenGraded && graded ? value.LastModified || value.ReleasedDate || null : null);
    const submitted = !!submittedAt || !!(subs && subs.some((s) => Number(s.Status) === 1 || Number(s.Status) === 3));
    const late = !!(last?.SubmissionDate && due && t(last.SubmissionDate) > t(due)); // (only a hand-in's own time says late: a grade's says nothing of when the quiz was taken)
    const missing = !submitted && !graded && !!(due && t(due) < Date.now());
    const score = num(value?.PointsNumerator);
    const comments = [];
    const fb = feedback || subs?.find((s) => s?.Feedback)?.Feedback || null;
    if (fb && rich(fb.Feedback)) comments.push({ id: `fb-${aid}`, author_id: null, author_name: 'Instructor', comment: rich(fb.Feedback).replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim(), html_comment: rich(fb.Feedback), created_at: value?.LastModified || null, author: { display_name: 'Instructor' } });
    if (value && rich(value.Comments)) comments.push({ id: `gc-${aid}`, author_id: null, author_name: 'Instructor', comment: rich(value.Comments).replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim(), created_at: value.LastModified || null, author: { display_name: 'Instructor' } });
    return {
      id: `${aid}-${me}`, assignment_id: String(aid), user_id: me,
      workflow_state: graded ? 'graded' : submitted ? 'submitted' : 'unsubmitted',
      submitted_at: submittedAt, score: score ?? (fb && num(fb.Score) !== null ? num(fb.Score) : null),
      grade: value?.DisplayedGrade ? String(value.DisplayedGrade).trim() : score !== null ? String(score) : null,
      entered_score: score, entered_grade: value?.DisplayedGrade || null, posted_at: value?.ReleasedDate || value?.LastModified || null, graded_at: value?.LastModified || null,
      late, missing, excused: false, attempt: submitted ? Math.max(1, subs ? subs.flatMap((s) => list(s.Submissions)).length : 1) : null,
      seconds_late: late ? Math.round((t(last.SubmissionDate) - t(due)) / 1000) : 0,
      submission_type: submitted ? as : null,
      attachments: last ? list(last.Files).map((f) => ({ id: String(f.FileId), display_name: f.FileName, filename: f.FileName, size: f.Size, url: null })) : [],
      submission_comments: comments, points_possible: points ?? null,
    };
  }
  async function folderAssignment(ou, f, gm, { withSub = false } = {}) {
    const go = f.GradeItemId ? gm.byId.get(String(f.GradeItemId)) : null;
    const points = num(f.Assessment?.ScoreDenominator) ?? num(go?.MaxPoints);
    const a = {
      id: String(f.Id), course_id: ou, name: f.Name, description: rich(f.CustomInstructions),
      due_at: iso(f.DueDate), unlock_at: iso(f.Availability?.StartDate), lock_at: iso(f.Availability?.EndDate),
      points_possible: points, grading_type: 'points', submission_types: submissionTypes(f), allowed_extensions: extensions(f),
      assignment_group_id: go?.CategoryId ? String(go.CategoryId) : '0', html_url: `/courses/${ou}/assignments/${f.Id}`,
      published: !f.IsHidden, locked_for_user: false, lock_explanation: null, allowed_attempts: -1, has_submitted_submissions: false,
      omit_from_final_grade: !!go?.ExcludeFromFinalGradeCalculation, grade_item_id: f.GradeItemId ? String(f.GradeItemId) : null,
      d2l: { kind: 'dropbox', id: String(f.Id) },
    };
    if (withSub) a.submission = shapeSubmission({ aid: a.id, due: a.due_at, points, value: go ? gm.value.get(String(go.Id)) : null, subs: await mySubs(ou, f.Id) });
    return a;
  }
  function quizAssignment(ou, q, gm, { withSub = false } = {}) {
    const go = q.GradeItemId ? gm.byId.get(String(q.GradeItemId)) : null;
    const points = num(go?.MaxPoints);
    const a = {
      id: idIn('quiz', q.QuizId), quiz_id: String(q.QuizId), course_id: ou, name: q.Name, description: rich(q.Description?.Text) || rich(q.Instructions?.Text),
      due_at: iso(q.DueDate), unlock_at: iso(q.StartDate), lock_at: iso(q.EndDate), points_possible: points, grading_type: 'points',
      submission_types: ['online_quiz'], assignment_group_id: go?.CategoryId ? String(go.CategoryId) : '0', html_url: `/courses/${ou}/quizzes/${q.QuizId}`,
      published: q.IsActive !== false, locked_for_user: false, allowed_attempts: q.AttemptsAllowed?.IsUnlimited ? -1 : num(q.AttemptsAllowed?.NumberOfAttemptsAllowed) ?? 1,
      omit_from_final_grade: !!go?.ExcludeFromFinalGradeCalculation, grade_item_id: q.GradeItemId ? String(q.GradeItemId) : null,
      d2l: { kind: 'quiz', id: String(q.QuizId) },
    };
    if (withSub) a.submission = shapeSubmission({ aid: a.id, due: a.due_at, points, value: go ? gm.value.get(String(go.Id)) : null, takenWhenGraded: true, as: 'online_quiz' });
    return a;
  }
  function topicAssignment(ou, tp, gm, { withSub = false } = {}) {
    const go = tp.GradeItemId ? gm.byId.get(String(tp.GradeItemId)) : null;
    const points = num(tp.ScoreOutOf) ?? num(go?.MaxPoints);
    const id = idIn('topic', tp.TopicId);
    const a = {
      id, course_id: ou, name: tp.Name, description: rich(tp.Description), due_at: iso(tp.DueDate || tp.EndDate), unlock_at: iso(tp.StartDate), lock_at: iso(tp.EndDate),
      points_possible: points, grading_type: 'points', submission_types: ['discussion_topic'], assignment_group_id: go?.CategoryId ? String(go.CategoryId) : '0',
      html_url: `/courses/${ou}/discussion_topics/${id}`, published: !tp.IsHidden, locked_for_user: !!tp.IsLocked,
      discussion_topic: { id, title: tp.Name, html_url: `/courses/${ou}/discussion_topics/${id}` }, grade_item_id: tp.GradeItemId ? String(tp.GradeItemId) : null,
      d2l: { kind: 'topic', id: String(tp.TopicId), forum: String(tp.forum?.ForumId || tp.ForumId || '') },
    };
    if (withSub) a.submission = shapeSubmission({ aid: a.id, due: a.due_at, points, value: go ? gm.value.get(String(go.Id)) : null, takenWhenGraded: true, as: 'discussion_topic' });
    return a;
  }
  function gradeItemAssignment(ou, go, gm, { withSub = false } = {}) {
    const id = idIn('grade', go.Id);
    const a = {
      id, course_id: ou, name: go.Name, description: rich(go.Description), due_at: null, points_possible: num(go.MaxPoints), grading_type: 'points',
      submission_types: ['none'], assignment_group_id: go.CategoryId ? String(go.CategoryId) : '0', html_url: `/courses/${ou}/grades`,
      published: !go.IsHidden, omit_from_final_grade: !!go.ExcludeFromFinalGradeCalculation, grade_item_id: String(go.Id), d2l: { kind: 'grade', id: String(go.Id) },
    };
    if (withSub) a.submission = shapeSubmission({ aid: id, due: null, points: num(go.MaxPoints), value: gm.value.get(String(go.Id)) });
    return a;
  }
  /** Every piece of work in a course, Canvas's way: one list, each with where it stands when asked. */
  async function assignments(ou, { withSub = false } = {}) {
    const [fs, qs, tps, gm] = await Promise.all([folders(ou), quizzes(ou), allTopics(ou).catch(() => []), gradeMap(ou)]);
    const out = [];
    const linked = new Set();
    for (const f of fs.filter((x) => !x.IsHidden)) { out.push(await folderAssignment(ou, f, gm, { withSub })); if (f.GradeItemId) linked.add(String(f.GradeItemId)); }
    for (const q of qs.filter((x) => x.IsActive !== false)) { out.push(quizAssignment(ou, q, gm, { withSub })); if (q.GradeItemId) linked.add(String(q.GradeItemId)); }
    for (const tp of tps.filter((x) => !x.IsHidden && (x.GradeItemId || x.ScoreOutOf || x.DueDate))) { out.push(topicAssignment(ou, tp, gm, { withSub })); if (tp.GradeItemId) linked.add(String(tp.GradeItemId)); }
    for (const go of gm.objs.filter((o) => !o.IsHidden && !linked.has(String(o.Id)) && o.GradeType !== 'Text' && o.GradeType !== 'Calculated' && o.GradeType !== 'Formula')) {
      const tool = o => o.AssociatedTool && o.AssociatedTool.ToolItemId;
      if (tool(go)) continue; // (a grade item another tool's activity owns, already listed by it)
      out.push(gradeItemAssignment(ou, go, gm, { withSub }));
    }
    return out.sort((a, b) => (t(a.due_at) || Infinity) - (t(b.due_at) || Infinity));
  }
  async function assignment(ou, aid, { withSub = true } = {}) {
    const [space, id] = spaceOf(aid);
    const gm = await gradeMap(ou);
    if (space === 'dropbox') {
      const f = (await folders(ou)).find((x) => String(x.Id) === id);
      if (!f) throw err('That assignment could not be found.', 404);
      const a = await folderAssignment(ou, f, gm, { withSub });
      a.can_submit = !a.locked_for_user;
      return a;
    }
    if (space === 'quiz') {
      const q = (await quizzes(ou)).find((x) => String(x.QuizId) === id);
      if (!q) throw err('That quiz could not be found.', 404);
      return quizAssignment(ou, q, gm, { withSub });
    }
    if (space === 'topic') {
      const tp = (await allTopics(ou)).find((x) => String(x.TopicId) === id);
      if (!tp) throw err('That discussion could not be found.', 404);
      return topicAssignment(ou, tp, gm, { withSub });
    }
    if (space === 'grade') {
      const go = gm.byId.get(id);
      if (!go) throw err('That grade item could not be found.', 404);
      return gradeItemAssignment(ou, go, gm, { withSub });
    }
    throw err('That assignment could not be found.', 404);
  }
  /** The assignment groups (grade categories), each with its work, for the Grades screen. */
  async function assignmentGroups(ou) {
    const [cats, all] = await Promise.all([gradeCategories(ou), assignments(ou, { withSub: true })]);
    const groups = cats.map((c, i) => ({ id: String(c.Id), name: c.Name, position: i + 1, group_weight: num(c.Weight) ?? 0, rules: { drop_lowest: c.NumberOfLowestToDrop || 0, drop_highest: c.NumberOfHighestToDrop || 0 }, assignments: [] }));
    const byId = new Map(groups.map((g) => [g.id, g]));
    let none = null;
    for (const a of all) {
      if (!a.grade_item_id && a.submission_types[0] !== 'none') continue; // (work with no grade item counts in no category)
      const g = byId.get(String(a.assignment_group_id));
      if (g) g.assignments.push(a);
      else (none ||= { id: '0', name: 'Other', position: groups.length + 1, group_weight: 0, rules: {}, assignments: [] }).assignments.push(a);
    }
    return none ? [...groups, none] : groups;
  }

  // ---- handing work in ----------------------------------------------------------------------------------------------
  // Canvas hands a file in three steps (ask for an upload, send the file, hand in its id); Brightspace in one (the
  // comment and the files together, multipart/mixed). The first two steps are answered here — the file is kept
  // until the hand-in names it — and the third sends everything to the folder.
  const pendingFiles = new Map(); // upload id → { name, type, blob }
  let uploadSeq = 1;
  function startUpload(body) {
    const id = `d2l-${Date.now()}-${uploadSeq++}`;
    return { upload_url: `d2l-upload:${id}`, upload_params: {}, file_param: 'file', d2l: id, name: body?.name };
  }
  /** Step 2: the file the form carries is kept, under the upload's id. */
  async function takeUpload(url, form) {
    const id = String(url).replace(/^d2l-upload:/, '');
    const file = form?.get?.('file');
    if (!file) throw err('No file was given to hand in.', 400);
    pendingFiles.set(id, { name: file.name || 'file', type: file.type || 'application/octet-stream', blob: file });
    return { id, display_name: file.name, filename: file.name, size: file.size, 'content-type': file.type };
  }
  function multipart(parts) {
    const boundary = `----simpl-d2l-${Math.random().toString(16).slice(2)}`; // (lowercase: a boundary must match the header's, and a Blob's type is lowercased)
    const pieces = [];
    for (const p of parts) {
      let head = `--${boundary}\r\nContent-Type: ${p.type}\r\n`;
      if (p.filename) head += `Content-Disposition: form-data; name="file"; filename="${String(p.filename).replace(/"/g, '')}"\r\n`;
      pieces.push(head + '\r\n', p.body, '\r\n');
    }
    pieces.push(`--${boundary}--\r\n`);
    return { body: new Blob(pieces), type: `multipart/mixed; boundary=${boundary}` };
  }
  async function submit(ou, aid, body) {
    const [space, id] = spaceOf(aid);
    if (space !== 'dropbox') throw err('This is handed in on its own page in Brightspace.', 400);
    const s = body?.submission || {};
    const comment = body?.comment?.text_comment || '';
    let parts;
    if (s.submission_type === 'online_text_entry') {
      // (a text hand-in: Brightspace takes text as a file of its own — the text, as an HTML page)
      const html = `<!doctype html><meta charset="utf-8"><body>${s.body || ''}</body>`;
      parts = [{ type: 'application/json', body: JSON.stringify({ Text: comment, Html: comment ? `<p>${comment}</p>` : '' }) }, { type: 'text/html', filename: 'Text Submission.html', body: new Blob([html], { type: 'text/html' }) }];
    } else if (s.submission_type === 'online_url') {
      const page = `<!doctype html><meta charset="utf-8"><body><a href="${String(s.url || '').replace(/"/g, '&quot;')}">${String(s.url || '')}</a></body>`;
      parts = [{ type: 'application/json', body: JSON.stringify({ Text: comment || String(s.url || ''), Html: '' }) }, { type: 'text/html', filename: 'Link.html', body: new Blob([page], { type: 'text/html' }) }];
    } else {
      const files = list(s.file_ids).map((fid) => pendingFiles.get(String(fid))).filter(Boolean);
      if (!files.length) throw err('Choose a file to hand in.', 400);
      parts = [{ type: 'application/json', body: JSON.stringify({ Text: comment, Html: '' }) }, ...files.map((f) => ({ type: f.type || 'application/octet-stream', filename: f.name, body: f.blob }))];
    }
    const { body: payload, type } = multipart(parts);
    const path = await LE(`/${ou}/dropbox/folders/${id}/submissions/mysubmissions/`);
    await call('POST', path, { form: new Blob([payload], { type }), formType: type });
    for (const fid of list(s.file_ids)) pendingFiles.delete(String(fid));
    forget(`mysubs:${ou}:${id}`);
    const a = await assignment(ou, aid, { withSub: true });
    return a.submission;
  }

  // ---- the planner: what is due, Canvas's way ------------------------------------------------------------------------
  const plannerType = (a) => (a.d2l?.kind === 'quiz' ? 'quiz' : a.d2l?.kind === 'topic' ? 'discussion_topic' : 'assignment');
  async function plannerItems(params) {
    const from = t(params?.start_date) || Date.now() - 14 * 864e5;
    const to = t(params?.end_date) || Date.now() + 21 * 864e5;
    const codes = list(params?.['context_codes[]'] || params?.context_codes).map(contextOf).filter(Boolean);
    const l = await local();
    const ovs = l.overrides || [];
    const ovFor = (type, id) => ovs.find((o) => o.plannable_type === type && String(o.plannable_id) === String(id)) || null;
    const out = [];
    // the student's own tasks (and nothing else) when the planner is asked for the user's own context
    const onlyMine = codes.length && codes.every((c) => c.kind === 'user');
    for (const n of l.notes || []) {
      const at = t(n.todo_date);
      if (!(at >= from && at <= to)) continue;
      out.push({ context_type: n.course_id ? 'Course' : 'User', course_id: n.course_id || null, context_name: null, plannable_id: n.id, plannable_type: 'planner_note', plannable_date: n.todo_date, plannable: { id: n.id, title: n.title, todo_date: n.todo_date, details: n.details || '' }, planner_override: ovFor('planner_note', n.id), submissions: false, html_url: null, new_activity: false });
    }
    if (onlyMine) return out;
    const es = (await enrollments()).filter((e) => e.Access?.CanAccess !== false && e.Access?.IsActive !== false);
    const wanted = codes.filter((c) => c.kind === 'course').map((c) => c.id);
    const ous = es.map((e) => String(e.OrgUnit.Id)).filter((ou) => !wanted.length || wanted.includes(ou));
    const per = await Promise.all(ous.map(async (ou) => {
      const e = es.find((x) => String(x.OrgUnit.Id) === ou);
      const name = e?.OrgUnit?.Name || '';
      const items = [];
      const work = await assignments(ou, { withSub: true }).catch(() => []);
      for (const a of work) {
        const at = t(a.due_at);
        if (!(at >= from && at <= to)) continue;
        const type = plannerType(a);
        const s = a.submission || {};
        items.push({
          context_type: 'Course', course_id: ou, context_name: name, plannable_id: a.id, plannable_type: type, plannable_date: a.due_at,
          plannable: { id: a.id, title: a.name, due_at: a.due_at, points_possible: a.points_possible, assignment_id: type === 'discussion_topic' ? a.id : undefined },
          submissions: { submitted: s.workflow_state === 'submitted' || s.workflow_state === 'graded' || !!s.submitted_at, graded: s.workflow_state === 'graded', late: !!s.late, missing: !!s.missing, excused: false, needs_grading: false, has_feedback: !!(s.submission_comments || []).length, redo_request: false },
          planner_override: ovFor(type, a.id), html_url: a.html_url, new_activity: false,
        });
      }
      for (const ev of await calendarOf(ou).catch(() => [])) {
        if (ev.IsAssociatedWithEntity) continue; // (a due date: its work is on the list already)
        const at = t(ev.StartDateTime || ev.StartDay);
        if (!(at >= from && at <= to)) continue;
        items.push({ context_type: 'Course', course_id: ou, context_name: name, plannable_id: String(ev.CalendarEventId), plannable_type: 'calendar_event', plannable_date: iso(ev.StartDateTime || ev.StartDay), plannable: { id: String(ev.CalendarEventId), title: ev.Title, start_at: iso(ev.StartDateTime || ev.StartDay), end_at: iso(ev.EndDateTime || ev.EndDay), all_day: !!ev.IsAllDayEvent, location_name: ev.LocationName || null }, submissions: false, planner_override: ovFor('calendar_event', ev.CalendarEventId), html_url: `/calendar?event_id=${ev.CalendarEventId}`, new_activity: false });
      }
      for (const n of await news(ou).catch(() => [])) {
        const at = t(n.StartDate || n.CreatedDate);
        if (!(at >= from && at <= to) || n.IsPublished === false || n.IsHidden) continue;
        const id = idIn('news', n.Id);
        items.push({ context_type: 'Course', course_id: ou, context_name: name, plannable_id: id, plannable_type: 'announcement', plannable_date: iso(n.StartDate || n.CreatedDate), plannable: { id, title: n.Title, posted_at: iso(n.StartDate || n.CreatedDate) }, submissions: false, planner_override: ovFor('announcement', id), html_url: `/courses/${ou}/discussion_topics/${id}`, new_activity: !l.read[`news:${n.Id}`] });
      }
      return items;
    }));
    out.push(...per.flat());
    return out.sort((a, b) => t(a.plannable_date) - t(b.plannable_date));
  }
  async function notes(params) {
    const l = await local();
    const from = t(params?.start_date) || -Infinity, to = t(params?.end_date) || Infinity;
    return (l.notes || []).filter((n) => !n.todo_date || (t(n.todo_date) >= from && t(n.todo_date) <= to)).map((n) => ({ ...n, workflow_state: 'active', user_id: String(page.userId || '') }));
  }
  function addNote(body) {
    return saveLocal((l) => {
      const n = { id: `n${Date.now()}${l.seq++}`, title: String(body?.title || 'Task'), details: body?.details || '', todo_date: body?.todo_date || null, course_id: body?.course_id ? String(body.course_id) : null, created_at: new Date().toISOString() };
      l.notes.push(n);
      return n;
    });
  }
  const dropNote = (id) => saveLocal((l) => { l.notes = l.notes.filter((n) => String(n.id) !== String(id)); l.overrides = l.overrides.filter((o) => !(o.plannable_type === 'planner_note' && String(o.plannable_id) === String(id))); return {}; });
  function addOverride(body) {
    return saveLocal((l) => {
      const had = l.overrides.find((o) => o.plannable_type === body.plannable_type && String(o.plannable_id) === String(body.plannable_id));
      if (had) throw err('An override for this item exists already.', 400);
      const o = { id: `o${Date.now()}${l.seq++}`, plannable_type: body.plannable_type, plannable_id: String(body.plannable_id), marked_complete: !!body.marked_complete, dismissed: !!body.dismissed, user_id: String(page.userId || '') };
      l.overrides.push(o);
      return o;
    });
  }
  function setOverride(id, body) {
    return saveLocal((l) => {
      const o = l.overrides.find((x) => String(x.id) === String(id));
      if (!o) throw err('That override could not be found.', 404);
      if ('marked_complete' in (body || {})) o.marked_complete = !!body.marked_complete;
      if ('dismissed' in (body || {})) o.dismissed = !!body.dismissed;
      return { ...o };
    });
  }

  // ---- announcements --------------------------------------------------------------------------------------------------
  async function shapeNews(ou, n, l) {
    const id = idIn('news', n.Id);
    return {
      id, title: n.Title, message: rich(n.Body), posted_at: iso(n.StartDate || n.CreatedDate), delayed_post_at: null, created_at: iso(n.CreatedDate), last_reply_at: null,
      author: { id: null, display_name: n.IsAuthorInfoShown === false ? '' : 'Instructor', avatar_image_url: null }, user_name: null,
      context_code: `course_${ou}`, html_url: `/courses/${ou}/discussion_topics/${id}`, url: `/courses/${ou}/discussion_topics/${id}`,
      read_state: l.read[`news:${n.Id}`] ? 'read' : 'unread', unread_count: 0, discussion_subentry_count: 0, announcement: true, locked: true, pinned: !!n.IsPinned,
      attachments: list(n.Attachments).map((a) => ({ id: String(a.FileId), display_name: a.FileName, filename: a.FileName, size: a.Size, url: `/d2l/api/le/${PREFER.le}/${ou}/news/${n.Id}/attachments/${a.FileId}` })),
    };
  }
  async function announcements(params) {
    const codes = list(params?.['context_codes[]'] || params?.context_codes).map(contextOf).filter((c) => c?.kind === 'course');
    const from = t(params?.start_date) || -Infinity, to = t(params?.end_date) || Infinity;
    const l = await local();
    const per = await Promise.all(codes.map(async (c) => (await news(c.id)).filter((n) => n.IsPublished !== false && !n.IsHidden).map((n) => shapeNews(c.id, n, l))));
    const all = (await Promise.all(per.flat())).filter((a) => { const at = t(a.posted_at); return !(at < from) && !(at > to); });
    return all.sort((a, b) => t(b.posted_at) - t(a.posted_at));
  }

  // ---- discussions --------------------------------------------------------------------------------------------------------
  async function shapeTopic(ou, tp, l) {
    const id = idIn('topic', tp.TopicId);
    const graded = !!(tp.GradeItemId || num(tp.ScoreOutOf));
    return {
      id, title: tp.Name, message: rich(tp.Description), posted_at: iso(tp.StartDate) || null, last_reply_at: null, created_at: null,
      author: { display_name: tp.forum?.Name || '' }, // (a topic has no author in Brightspace: its forum stands where Canvas names the teacher)
      context_code: `course_${ou}`, html_url: `/courses/${ou}/discussion_topics/${id}`, url: `/courses/${ou}/discussion_topics/${id}`,
      discussion_subentry_count: num(tp.PostCount) ?? 0, unread_count: num(tp.UnreadPostCount) ?? 0, read_state: num(tp.UnreadPostCount) ? 'unread' : 'read',
      locked: !!tp.IsLocked, locked_for_user: !!tp.IsLocked, pinned: false, discussion_type: 'threaded', published: !tp.IsHidden,
      assignment_id: graded ? id : null, assignment: graded ? { id, due_at: iso(tp.DueDate || tp.EndDate), points_possible: num(tp.ScoreOutOf) } : null,
      require_initial_post: !!tp.MustPostToParticipate || !!tp.forum?.MustPostToParticipate, lock_at: iso(tp.EndDate), todo_date: null,
      forum: tp.forum ? { id: String(tp.forum.ForumId), name: tp.forum.Name } : null,
    };
  }
  async function topics(ou, params) {
    if (params?.only_announcements) {
      const l = await local();
      return Promise.all((await news(ou)).filter((n) => n.IsPublished !== false && !n.IsHidden).map((n) => shapeNews(ou, n, l)));
    }
    const l = await local();
    return Promise.all((await allTopics(ou)).filter((tp) => !tp.IsHidden).map((tp) => shapeTopic(ou, tp, l)));
  }
  async function topic(ou, tid) {
    const [space, id] = spaceOf(tid);
    const l = await local();
    if (space === 'news') {
      const n = (await news(ou)).find((x) => String(x.Id) === id);
      if (!n) throw err('That announcement could not be found.', 404);
      await saveLocal((x) => { x.read[`news:${id}`] = Date.now(); return {}; });
      return shapeNews(ou, n, l);
    }
    if (space !== 'topic') throw err('That discussion could not be found.', 404);
    const tp = (await allTopics(ou)).find((x) => String(x.TopicId) === id);
    if (!tp) throw err('That discussion could not be found.', 404);
    return shapeTopic(ou, tp, l);
  }
  /** A topic's posts as Canvas's threaded view: the participants and each entry with its replies. */
  async function topicView(ou, tid) {
    const [space, id] = spaceOf(tid);
    if (space === 'news') return { participants: [], unread_entries: [], forced_entries: [], view: [], new_entries: [] };
    const tp = (await allTopics(ou)).find((x) => String(x.TopicId) === id);
    if (!tp) throw err('That discussion could not be found.', 404);
    const posts = await postsOf(ou, tp.forum.ForumId, id);
    const people = new Map();
    const entry = (p) => {
      const uid = String(p.PostingUserId || p.PostingUser?.Identifier || '');
      if (uid && !people.has(uid)) people.set(uid, { id: uid, display_name: p.PostingUserDisplayName || p.PostingUser?.DisplayName || '', avatar_image_url: null });
      return { id: String(p.PostId), user_id: uid, parent_id: p.ParentPostId ? String(p.ParentPostId) : null, created_at: iso(p.DatePosted), updated_at: iso(p.LastEditDate || p.DatePosted), message: rich(p.Message), deleted: !!p.IsDeleted, replies: [] };
    };
    const all = posts.map(entry);
    const byId = new Map(all.map((e) => [e.id, e]));
    const roots = [];
    for (const e of all) {
      const parent = e.parent_id && byId.get(e.parent_id);
      if (parent) parent.replies.push(e); else roots.push(e);
    }
    return { participants: [...people.values()], unread_entries: [], forced_entries: [], new_entries: [], view: roots };
  }
  async function reply(ou, tid, body, parent = null) {
    const [space, id] = spaceOf(tid);
    if (space !== 'topic') throw err('Replies are not taken here.', 400);
    const tp = (await allTopics(ou)).find((x) => String(x.TopicId) === id);
    if (!tp) throw err('That discussion could not be found.', 404);
    const text = String(body?.message || '');
    const post = await call('POST', await LE(`/${ou}/discussions/forums/${tp.forum.ForumId}/topics/${id}/posts/`), { json: { ParentPostId: parent ? Number(parent) : null, Subject: parent ? `Re: ${tp.Name}` : tp.Name, Message: { Content: text, Type: 'Html' }, IsAnonymous: false } });
    forget(`posts:${ou}:${tp.forum.ForumId}:${id}`);
    return { id: String(post?.PostId || Date.now()), message: text, created_at: new Date().toISOString(), user_id: String(page.userId || ''), parent_id: parent ? String(parent) : null };
  }

  // ---- content: modules, their items, pages and files -------------------------------------------------------------------
  // A topic's activity (Brightspace's ACTIVITYTYPE_T): a file, a link, a dropbox folder, a quiz, a forum, a topic of one.
  // The rest — a checklist, a survey, a SCORM package, an LTI tool — are Brightspace's to show, in its content viewer.
  const ACTIVITY = { 1: 'File', 2: 'Link', 3: 'Assignment', 4: 'Quiz', 5: 'Forum', 6: 'Discussion' };
  /** A module's topics and its own modules, in Brightspace's order (one SortOrder runs through both). */
  const childrenOf = (m) => [...list(m.Topics).map((tp) => ({ tp, at: tp })), ...list(m.Modules).map((sub) => ({ sub, at: sub }))]
    .filter((x) => !x.at.IsHidden)
    .sort((a, b) => (a.at.SortOrder ?? 0) - (b.at.SortOrder ?? 0));
  /** Everything in a module, in reading order: its topics, and each module inside it as a heading over its own. */
  function walk(m, depth = 0, out = []) {
    for (const x of childrenOf(m)) {
      if (x.tp) out.push({ tp: x.tp, depth });
      else { out.push({ head: x.sub, depth }); walk(x.sub, depth + 1, out); }
    }
    return out;
  }
  /** The course's modules, top level (Brightspace's root holds no topics of its own). */
  const topModules = (tree) => childrenOf({ Modules: tree?.Modules }).map((x) => x.sub).filter(Boolean);
  function itemOf(ou, tp, m, i, indent) {
    const kind = ACTIVITY[tp.ActivityType] || (tp.TypeIdentifier === 'Link' && !/^\/d2l\//.test(tp.Url || '') ? 'Link' : 'Other');
    const toolId = tp.ToolItemId ? String(tp.ToolItemId) : null;
    let type = 'File', url = `/courses/${ou}/modules/items/${tp.TopicId}`, content = String(tp.TopicId), external;
    if (kind === 'Assignment' && toolId) { type = 'Assignment'; url = `/courses/${ou}/assignments/${toolId}`; content = toolId; }
    else if (kind === 'Quiz' && toolId) { type = 'Quiz'; url = `/courses/${ou}/quizzes/${toolId}`; content = toolId; }
    else if (kind === 'Discussion' && toolId) { type = 'Discussion'; url = `/courses/${ou}/discussion_topics/${idIn('topic', toolId)}`; content = idIn('topic', toolId); }
    else if (kind === 'Forum') { type = 'Discussion'; url = `/courses/${ou}/discussion_topics`; content = undefined; }
    else if (kind === 'Link') { type = 'ExternalUrl'; external = new URL(tp.Url || '/', location.origin).href; }
    else if (kind === 'File' && /\.html?$/i.test(tp.Url || '')) { type = 'Page'; url = `/courses/${ou}/pages/${tp.TopicId}`; }
    else if (kind !== 'File') { type = 'ExternalUrl'; external = new URL(`/d2l/le/content/${ou}/viewContent/${tp.TopicId}/View`, location.origin).href; } // (Brightspace's own viewer, in a tab of its own)
    const done = tp.CompletionType === 3 ? null : !!tp.IsCompleted;
    return {
      id: String(tp.TopicId), module_id: String(m.ModuleId), position: i + 1, title: tp.Title, indent, type, content_id: content, page_url: type === 'Page' ? String(tp.TopicId) : undefined,
      html_url: external || url, url: external || url, external_url: external, published: !tp.IsHidden,
      completion_requirement: tp.CompletionType === 1 ? { type: 'must_mark_done', completed: !!done } : tp.CompletionType === 2 ? { type: 'must_view', completed: !!done } : undefined,
      content_details: { locked_for_user: !!tp.IsLocked, due_at: null },
    };
  }
  async function modules(ou) {
    const tree = await toc(ou);
    // Canvas's modules do not nest: a module inside one is a heading in it, with its topics under it a step in
    return topModules(tree).map((m, i) => {
      const items = walk(m).map((x, j) => (x.head
        ? { id: String(x.head.ModuleId), module_id: String(m.ModuleId), position: j + 1, title: x.head.Title, indent: x.depth, type: 'SubHeader', published: true }
        : itemOf(ou, x.tp, m, j, x.depth)));
      return {
        id: String(m.ModuleId), name: m.Title, position: i + 1, unlock_at: iso(m.StartDateTime), require_sequential_progress: false,
        state: m.IsLocked ? 'locked' : 'unlocked', published: true, items_count: items.length, items,
      };
    });
  }
  /** Every content topic of a course, in reading order, with its (top-level) module. */
  async function topicsInOrder(ou) {
    const tree = await toc(ou);
    return topModules(tree).flatMap((m) => walk(m).filter((x) => x.tp).map((x) => ({ tp: x.tp, m })));
  }
  async function itemSequence(ou, params) {
    const all = await topicsInOrder(ou);
    const id = String(params?.asset_id || '');
    const type = String(params?.asset_type || '');
    const i = all.findIndex(({ tp }) => (type === 'ModuleItem' || type === 'Page' || type === 'File' ? String(tp.TopicId) === id : String(tp.ToolItemId || '') === spaceOf(id)[1]));
    if (i < 0) return { items: [], modules: [] };
    const at = (k) => (all[k] ? itemOf(ou, all[k].tp, all[k].m, k, 0) : null);
    return { items: [{ prev: at(i - 1), current: at(i), next: at(i + 1), mastery_path: null }], modules: [{ id: String(all[i].m.ModuleId), name: all[i].m.Title }] };
  }
  /** A content topic as a page: its file's HTML, read with the session. */
  async function pageOf(ou, slug) {
    const all = await topicsInOrder(ou);
    const hit = all.find(({ tp }) => String(tp.TopicId) === String(slug));
    if (!hit) throw err('That page could not be found.', 404);
    let body = rich(hit.tp.Description);
    try {
      const text = await call('GET', await LE(`/${ou}/content/topics/${slug}/file`));
      if (typeof text === 'string') body = text.replace(/^[\s\S]*<body[^>]*>/i, '').replace(/<\/body>[\s\S]*$/i, '');
    } catch { /* the description, then */ }
    return { page_id: String(slug), url: String(slug), title: hit.tp.Title, body, updated_at: iso(hit.tp.LastModifiedDate), published: true, front_page: false, html_url: `/courses/${ou}/pages/${slug}`, locked_for_user: !!hit.tp.IsLocked };
  }
  async function pagesList(ou) {
    const all = await topicsInOrder(ou);
    return all.filter(({ tp }) => /\.html?$/i.test(tp.Url || '')).map(({ tp }) => ({ page_id: String(tp.TopicId), url: String(tp.TopicId), title: tp.Title, updated_at: iso(tp.LastModifiedDate), published: true, front_page: false, html_url: `/courses/${ou}/pages/${tp.TopicId}` }));
  }
  function mimeOf(name) {
    const ext = String(name || '').split('.').pop().toLowerCase();
    return { pdf: 'application/pdf', doc: 'application/msword', docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', ppt: 'application/vnd.ms-powerpoint', pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation', xls: 'application/vnd.ms-excel', xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', mp4: 'video/mp4', mp3: 'audio/mpeg', txt: 'text/plain', html: 'text/html', htm: 'text/html', zip: 'application/zip' }[ext] || 'application/octet-stream';
  }
  function mimeClass(type) {
    if (type === 'application/pdf') return 'pdf';
    if (/^image\//.test(type)) return 'image';
    if (/^video\//.test(type)) return 'video';
    if (/^audio\//.test(type)) return 'audio';
    if (/word/.test(type)) return 'doc';
    if (/presentation|powerpoint/.test(type)) return 'ppt';
    if (/sheet|excel/.test(type)) return 'xls';
    if (/html/.test(type)) return 'html';
    return 'file';
  }
  async function fileOf(ou, tp) {
    const name = decodeURIComponent(String(tp.Url || tp.Title).split('/').pop() || tp.Title);
    const type = mimeOf(name);
    const url = `${await LE(`/${ou}/content/topics/${tp.TopicId}/file`)}?stream=false`;
    // (preview: Brightspace's own viewer for the topic, without its header — it shows what the interface does not draw
    // itself, a slide deck or a spreadsheet made into pages, and counts the topic as viewed, as opening it in Brightspace does)
    return { id: String(tp.TopicId), uuid: String(tp.TopicId), folder_id: `root-${ou}`, display_name: tp.Title || name, filename: name, 'content-type': type, url, size: null, created_at: null, updated_at: iso(tp.LastModifiedDate), modified_at: iso(tp.LastModifiedDate), locked: !!tp.IsLocked, hidden: false, locked_for_user: !!tp.IsLocked, mime_class: mimeClass(type), preview_url: `/d2l/le/content/${ou}/fullscreen/${tp.TopicId}/View?skipHeader=True`, thumbnail_url: null };
  }
  const fileTopics = async (ou) => (await topicsInOrder(ou)).filter(({ tp }) => tp.TypeIdentifier === 'File' && !/\.html?$/i.test(tp.Url || ''));
  async function filesList(ou) { return Promise.all((await fileTopics(ou)).map(({ tp }) => fileOf(ou, tp))); }
  async function rootFolder(ou) {
    const n = (await fileTopics(ou)).length;
    return { id: `root-${ou}`, name: 'course files', full_name: 'course files', parent_folder_id: null, files_count: n, folders_count: 0, context_type: 'Course', context_id: ou, files_url: null, folders_url: null, locked: false, hidden: false };
  }
  /** A file by its content topic, wherever it is (the store asks without the course). */
  async function fileById(fid) {
    for (const e of await enrollments()) {
      const ou = String(e.OrgUnit.Id);
      const hit = (await fileTopics(ou).catch(() => [])).find(({ tp }) => String(tp.TopicId) === String(fid));
      if (hit) return fileOf(ou, hit.tp);
    }
    throw err('That file could not be found.', 404);
  }

  // ---- people ---------------------------------------------------------------------------------------------------------
  async function users(ou) {
    const people = await classlist(ou);
    return people.map((p) => {
      const name = [p.FirstName, p.LastName].filter(Boolean).join(' ') || p.DisplayName || '';
      const teaching = isTeacher(p.ClasslistRoleDisplayName);
      return { id: String(p.Identifier), name, short_name: p.FirstName || name, sortable_name: p.DisplayName || name, avatar_url: null, pronouns: p.Pronouns || null, login_id: p.Username || null, email: p.Email || null, enrollments: [{ type: teaching ? 'TeacherEnrollment' : 'StudentEnrollment', role: teaching ? 'TeacherEnrollment' : 'StudentEnrollment', enrollment_state: 'active' }], role_label: p.ClasslistRoleDisplayName || '' };
    });
  }

  // ---- quizzes ------------------------------------------------------------------------------------------------------------
  async function shapeQuiz(ou, q, gm) {
    const go = q.GradeItemId ? gm.byId.get(String(q.GradeItemId)) : null;
    const value = go ? gm.value.get(String(go.Id)) : null;
    return {
      id: String(q.QuizId), title: q.Name, description: rich(q.Description?.Text) || rich(q.Instructions?.Text), html_url: `/courses/${ou}/quizzes/${q.QuizId}`, mobile_url: null,
      due_at: iso(q.DueDate), lock_at: iso(q.EndDate), unlock_at: iso(q.StartDate), points_possible: num(go?.MaxPoints), quiz_type: 'assignment',
      time_limit: q.SubmissionTimeLimit?.IsEnforced ? num(q.SubmissionTimeLimit.TimeLimitValue) : null, allowed_attempts: q.AttemptsAllowed?.IsUnlimited ? -1 : num(q.AttemptsAllowed?.NumberOfAttemptsAllowed) ?? 1,
      assignment_id: idIn('quiz', q.QuizId), published: q.IsActive !== false, locked_for_user: false, question_count: null, one_question_at_a_time: false,
      d2l: { score: num(value?.PointsNumerator), grade: value?.DisplayedGrade || null, page: `/d2l/lms/quizzing/user/quiz_summary.d2l?qi=${q.QuizId}&ou=${ou}` },
    };
  }
  async function quizList(ou) { const gm = await gradeMap(ou); return Promise.all((await quizzes(ou)).filter((q) => q.IsActive !== false).map((q) => shapeQuiz(ou, q, gm))); }
  async function quizOne(ou, qid) {
    const q = (await quizzes(ou)).find((x) => String(x.QuizId) === String(qid));
    if (!q) throw err('That quiz could not be found.', 404);
    return shapeQuiz(ou, q, await gradeMap(ou));
  }

  // ---- the calendar ----------------------------------------------------------------------------------------------------------
  async function calendarEvents(params) {
    const from = t(params?.start_date) || -Infinity, to = t(params?.end_date) || Infinity;
    const codes = list(params?.['context_codes[]'] || params?.context_codes).map(contextOf).filter((c) => c?.kind === 'course');
    const ous = codes.length ? codes.map((c) => c.id) : (await enrollments()).map((e) => String(e.OrgUnit.Id));
    const type = params?.type || 'event';
    const per = await Promise.all(ous.map(async (ou) => {
      if (type === 'assignment') {
        const work = await assignments(ou, { withSub: true }).catch(() => []);
        return work.filter((a) => a.due_at && t(a.due_at) >= from && t(a.due_at) <= to).map((a) => ({
          id: `assignment_${a.id}`, title: a.name, start_at: a.due_at, end_at: a.due_at, all_day: false, all_day_date: null, type: 'assignment',
          context_code: `course_${ou}`, html_url: a.html_url, assignment: a, description: a.description || '', workflow_state: 'active',
        }));
      }
      return (await calendarOf(ou).catch(() => [])).filter((ev) => !ev.IsAssociatedWithEntity).filter((ev) => { const at = t(ev.StartDateTime || ev.StartDay); return at >= from && at <= to; }).map((ev) => ({
        id: String(ev.CalendarEventId), title: ev.Title, start_at: iso(ev.StartDateTime || ev.StartDay), end_at: iso(ev.EndDateTime || ev.EndDay), all_day: !!ev.IsAllDayEvent,
        all_day_date: ev.IsAllDayEvent ? String(ev.StartDay || ev.StartDateTime || '').slice(0, 10) : null, type: 'event', context_code: `course_${ou}`, location_name: ev.LocationName || null,
        description: ev.Description || '', html_url: `/calendar?event_id=${ev.CalendarEventId}`, workflow_state: 'active',
      }));
    }));
    return per.flat();
  }
  async function calendarEvent(id) {
    for (const e of await enrollments()) {
      const ou = String(e.OrgUnit.Id);
      const ev = (await calendarOf(ou).catch(() => [])).find((x) => String(x.CalendarEventId) === String(id));
      if (ev) return { id: String(ev.CalendarEventId), title: ev.Title, start_at: iso(ev.StartDateTime || ev.StartDay), end_at: iso(ev.EndDateTime || ev.EndDay), all_day: !!ev.IsAllDayEvent, context_code: `course_${ou}`, location_name: ev.LocationName || null, description: ev.Description || '', html_url: `/calendar?event_id=${ev.CalendarEventId}` };
    }
    throw err('That event could not be found.', 404);
  }

  // ---- what Canvas has and Brightspace does not -------------------------------------------------------------------------------
  const none = () => [];
  const nothing = () => null;

  // ---- the routes: a Canvas address, and who answers it --------------------------------------------------------------------------
  const C = '/api/v1';
  const R = [];
  const on = (method, re, fn) => R.push([method, re, fn]);
  const ID = '(\\d+)';
  const KIND = '(courses|groups)';
  const rx = (s) => new RegExp(`^${s}$`);

  on('GET', rx(`${C}/users/self`), () => userSelf());
  on('GET', rx(`${C}/users/self/profile`), async () => { const u = await userSelf(); return { ...u, primary_email: u.email, login_id: u.login_id }; });
  on('GET', rx(`${C}/users/self/colors`), async () => ({ custom_colors: { ...(await local()).colors } }));
  on('PUT', rx(`${C}/users/self/colors/course_${ID}`), (m, p, body) => saveLocal((l) => { l.colors[`course_${m[1]}`] = body?.hexcode ? `#${String(body.hexcode).replace(/^#/, '')}` : null; return { hexcode: l.colors[`course_${m[1]}`] }; }));
  on('GET', rx(`${C}/users/self/course_nicknames/${ID}`), async (m) => ({ course_id: m[1], nickname: (await local()).nicknames[m[1]] || null }));
  on('PUT', rx(`${C}/users/self/course_nicknames/${ID}`), (m, p, body) => { forget('enr'); return saveLocal((l) => { l.nicknames[m[1]] = String(body?.nickname || '').trim() || undefined; return { course_id: m[1], nickname: l.nicknames[m[1]] || null }; }); });
  on('DELETE', rx(`${C}/users/self/course_nicknames/${ID}`), (m) => saveLocal((l) => { delete l.nicknames[m[1]]; return { course_id: m[1], nickname: null }; }));
  on('POST', rx(`${C}/users/self/favorites/courses/${ID}`), async (m) => { await call('POST', await LP(`/enrollments/myenrollments/${m[1]}/pin`)).catch(() => null); forget('enr'); return { context_id: m[1], context_type: 'Course' }; });
  on('DELETE', rx(`${C}/users/self/favorites/courses/${ID}`), async (m) => { await call('DELETE', await LP(`/enrollments/myenrollments/${m[1]}/pin`)).catch(() => null); forget('enr'); return { context_id: m[1], context_type: 'Course' }; });
  on('GET', rx(`${C}/users/self/enrollments`), none);
  on('GET', rx(`${C}/users/self/groups`), none);
  on('GET', rx(`${C}/users/self/activity_stream`), none);
  on('GET', rx(`${C}/users/self/activity_stream/summary`), none);
  on('GET', rx(`${C}/users/self/history`), none);
  on('GET', rx(`${C}/accounts/${ID}`), async () => { const o = await orgInfo(); return { id: String(o?.Identifier || page.orgId || ''), name: o?.Name || '' }; });

  on('GET', rx(`${C}/courses`), (m, p) => courses(p));
  on('GET', rx(`${C}/courses/${ID}`), (m, p) => course(m[1], p));
  on('GET', rx(`${C}/dashboard/dashboard_cards`), () => dashboardCards());
  on('GET', rx(`${C}/${KIND}/${ID}/tabs`), (m) => tabs(m[2]));
  on('GET', rx(`${C}/${KIND}/${ID}/front_page`), nothing);
  on('GET', rx(`${C}/courses/${ID}/todo`), none);
  on('GET', rx(`${C}/${KIND}/${ID}/activity_stream`), none);
  on('GET', rx(`${C}/courses/${ID}/sections`), none);
  on('GET', rx(`${C}/courses/${ID}/groups`), none);
  on('GET', rx(`${C}/groups/${ID}`), () => { throw err('Groups are not shown for Brightspace yet.', 404); });

  on('GET', rx(`${C}/courses/${ID}/assignments`), (m, p) => assignments(m[1], { withSub: includes(p, 'submission') }));
  on('GET', rx(`${C}/courses/${ID}/assignments/${ID}`), (m, p) => assignment(m[1], m[2], { withSub: includes(p, 'submission') || true }));
  on('GET', rx(`${C}/courses/${ID}/assignments/${ID}/submissions/self`), async (m) => (await assignment(m[1], m[2], { withSub: true })).submission);
  on('GET', rx(`${C}/courses/${ID}/assignment_groups`), (m) => assignmentGroups(m[1]));
  on('POST', rx(`${C}/courses/${ID}/assignments/${ID}/submissions/self/files`), (m, p, body) => startUpload(body));
  on('POST', rx(`${C}/courses/${ID}/assignments/${ID}/submissions`), (m, p, body) => submit(m[1], m[2], body));
  on('PUT', rx(`${C}/courses/${ID}/assignments/${ID}/submissions/self`), () => { throw err('Comments on your work are written in Brightspace.', 400); });
  on('GET', rx(`${C}/courses/${ID}/external_tools`), none);

  on('GET', rx(`${C}/planner/items`), (m, p) => plannerItems(p));
  on('GET', rx(`${C}/planner_notes`), (m, p) => notes(p));
  on('POST', rx(`${C}/planner_notes`), (m, p, body) => addNote(body));
  on('DELETE', rx(`${C}/planner_notes/([^/]+)`), (m) => dropNote(decodeURIComponent(m[1])));
  on('GET', rx(`${C}/planner/overrides`), async () => (await local()).overrides.slice());
  on('POST', rx(`${C}/planner/overrides`), (m, p, body) => addOverride(body));
  on('PUT', rx(`${C}/planner/overrides/([^/]+)`), (m, p, body) => setOverride(decodeURIComponent(m[1]), body));

  on('GET', rx(`${C}/announcements`), (m, p) => announcements(p));
  on('GET', rx(`${C}/${KIND}/${ID}/discussion_topics`), (m, p) => topics(m[2], p));
  on('GET', rx(`${C}/${KIND}/${ID}/discussion_topics/${ID}`), (m) => topic(m[2], m[3]));
  on('GET', rx(`${C}/${KIND}/${ID}/discussion_topics/${ID}/view`), (m) => topicView(m[2], m[3]));
  on('PUT', rx(`${C}/${KIND}/${ID}/discussion_topics/${ID}/read_all`), () => null);
  on('POST', rx(`${C}/${KIND}/${ID}/discussion_topics/${ID}/entries`), (m, p, body) => reply(m[2], m[3], body));
  on('POST', rx(`${C}/${KIND}/${ID}/discussion_topics/${ID}/entries/${ID}/replies`), (m, p, body) => reply(m[2], m[3], body, m[4]));

  on('GET', rx(`${C}/courses/${ID}/modules`), (m) => modules(m[1]));
  on('GET', rx(`${C}/courses/${ID}/module_item_sequence`), (m, p) => itemSequence(m[1], p));
  on('POST', rx(`${C}/courses/${ID}/modules/${ID}/items/${ID}/done`), () => { throw err('Brightspace marks this done as you open it.', 400); });
  on('PUT', rx(`${C}/courses/${ID}/modules/${ID}/items/${ID}/done`), () => { throw err('Brightspace marks this done as you open it.', 400); });
  on('GET', rx(`${C}/${KIND}/${ID}/pages`), (m) => pagesList(m[2]));
  on('GET', rx(`${C}/${KIND}/${ID}/pages/([^/]+)`), (m) => pageOf(m[2], decodeURIComponent(m[3])));
  on('GET', rx(`${C}/courses/${ID}/files`), (m) => filesList(m[1]));
  on('GET', rx(`${C}/${KIND}/${ID}/folders/root`), (m) => rootFolder(m[2]));
  on('GET', rx(`${C}/${KIND}/${ID}/folders/by_path(?:/.*)?`), async (m) => [await rootFolder(m[2])]);
  on('GET', rx(`${C}/folders/root-${ID}/folders`), none);
  on('GET', rx(`${C}/folders/root-${ID}/files`), (m) => filesList(m[1]));
  on('GET', rx(`${C}/files/${ID}`), (m) => fileById(m[1]));

  on('GET', rx(`${C}/${KIND}/${ID}/users`), (m) => users(m[2]));
  on('GET', rx(`${C}/courses/${ID}/quizzes`), (m) => quizList(m[1]));
  on('GET', rx(`${C}/courses/${ID}/quizzes/${ID}`), (m) => quizOne(m[1], m[2]));
  on('GET', rx(`${C}/courses/${ID}/quizzes/${ID}/submissions`), () => ({ quiz_submissions: [] }));

  on('GET', rx(`${C}/calendar_events`), (m, p) => calendarEvents(p));
  on('GET', rx(`${C}/calendar_events/${ID}`), (m) => calendarEvent(m[1]));
  on('GET', rx(`${C}/appointment_groups`), none);

  on('GET', rx(`${C}/conversations`), none);
  on('GET', rx(`${C}/conversations/unread_count`), () => ({ unread_count: '0' }));
  on('GET', rx(`${C}/search/recipients`), none);
  on('GET', rx('/dashboard/view'), nothing);
  on('GET', rx('/help_links'), none);

  /** A Canvas request, answered by Brightspace. `all` (every page) changes nothing here: each answer is whole. */
  async function request(method, path, { params, body } = {}) {
    let url;
    try { url = new URL(path, location.origin); } catch { throw err('Not a Canvas address', 400); }
    const q = { ...(params || {}) };
    for (const [k, v] of url.searchParams) { if (k in q) q[k] = list(q[k]).concat(v); else q[k] = v; }
    const p = url.pathname.replace(/\/+$/, '');
    for (const [m, re, fn] of R) {
      if (m !== method) continue;
      const hit = p.match(re);
      if (hit) return fn(hit, q, body);
    }
    throw err(`Not in Brightspace: ${method} ${p}`, 404);
  }

  /** Canvas's ENV, as far as Brightspace's page gives it: who is signed in, the organization. */
  function env() {
    return { current_user_id: page.userId ? String(page.userId) : null, current_user: { id: page.userId ? String(page.userId) : null, display_name: '' }, DOMAIN_ROOT_ACCOUNT_ID: page.orgId ? String(page.orgId) : null, PREFERENCES: { custom_colors: {} } };
  }

  BCV.d2l = {
    request,
    /** Step 2 of a hand-in's file (lib/canvas-api.js upload, given a d2l-upload: address). */
    upload: takeUpload,
    env,
    signedIn: () => !!page.userId,
    versions,
    /** For the tests: the id spaces. */
    ids: { idIn, spaceOf },
  };
})();
