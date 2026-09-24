/* A CSV of past courses, read into the record before this term (the GPA and the courses it covers)
 * for GPA tracking — shared by Settings → Grades and the Grades page's own settings sheet.
 *
 * The file has a header row, then one course a line: course, grade, and credits and term when it
 * has them, the columns matched by name (course/class/name, grade/letter/score, credits/units/hours,
 * term/semester); quoted cells and a tab-separated file read too. A letter (A−, B+), a percentage
 * (93%) and 4.0 points (3.0) all read as points; a row whose grade is none of those (P/NP, W, I,
 * CR) is skipped and counted as such; credits, when given, weight the GPA the way a transcript does. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const POINTS = { 'A+': 4, A: 4, 'A-': 3.7, 'B+': 3.3, B: 3, 'B-': 2.7, 'C+': 2.3, C: 2, 'C-': 1.7, 'D+': 1.3, D: 1, 'D-': 0.7, F: 0 };
  const SCALE = [[97, 4], [93, 4], [90, 3.7], [87, 3.3], [83, 3], [80, 2.7], [77, 2.3], [73, 2], [70, 1.7], [60, 1], [0, 0]];
  const COLS = {
    course: /^(course|class|name|title|subject|code|course\s*(name|code|title))$/i,
    grade: /^(grade|letter|final(\s*grade)?|mark|score|result)$/i,
    credits: /^(credits?|units?|hours?|credit\s*hours?|cr)$/i,
    term: /^(term|semester|quarter|session|period|year)$/i,
  };
  /** The points a grade cell is worth, or null when it carries none (P, W, blank). */
  function points(raw) {
    const g = String(raw || '').trim().toUpperCase().replace(/[−–]/g, '-').replace(/\s+/g, '');
    if (!g) return null;
    if (g in POINTS) return POINTS[g];
    const pct = /^(\d+(?:\.\d+)?)%$/.exec(g);
    if (pct) { const n = Number(pct[1]); return Number.isFinite(n) ? (SCALE.find(([at]) => n >= at) || [0, 0])[1] : null; }
    const n = Number(g);
    if (!Number.isFinite(n) || n < 0) return null;
    if (n <= 4.3) return Math.min(4, n); // 4.0-scale points as they are
    if (n <= 100) return (SCALE.find(([at]) => n >= at) || [0, 0])[1]; // a percentage without its sign
    return null;
  }
  /** One CSV line as cells: quotes and quoted commas handled; a tab-separated line reads too. */
  function cells(line) {
    const out = [];
    let cell = '';
    let q = false;
    const sep = line.includes('\t') && !line.includes(',') ? '\t' : ',';
    for (let i = 0; i < line.length; i++) {
      const ch = line[i];
      if (q) {
        if (ch === '"') { if (line[i + 1] === '"') { cell += '"'; i++; } else q = false; }
        else cell += ch;
      } else if (ch === '"') q = true;
      else if (ch === sep) { out.push(cell); cell = ''; }
      else cell += ch;
    }
    out.push(cell);
    return out.map((c) => c.trim());
  }
  /** The file as rows { course, term, grade, points, credits } and the count skipped. Throws without a
   *  header naming a course or grade column, or when no row has a grade that counts. */
  function parse(text) {
    const lines = String(text).split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
    if (!lines.length) throw new Error('empty');
    const head = cells(lines[0]);
    const idx = {};
    for (const [k, re] of Object.entries(COLS)) { const i = head.findIndex((h2) => re.test(h2.replace(/[_-]/g, ' ').trim())); if (i >= 0) idx[k] = i; }
    if (idx.course == null && idx.grade == null) throw new Error('no header');
    if (idx.course == null) idx.course = 0;
    const rows = [];
    let skipped = 0;
    for (const line of lines.slice(1)) {
      const cs = cells(line);
      if (!cs.some(Boolean)) continue;
      const course = (cs[idx.course] || '').trim();
      const gradeCell = idx.grade != null ? cs[idx.grade] : cs.slice(1).find((c) => points(c) !== null || /^[A-F][+-]?$/i.test(c));
      const pts = points(gradeCell);
      const credits = idx.credits != null && cs[idx.credits] !== '' ? Number(cs[idx.credits]) : null;
      const term = idx.term != null ? (cs[idx.term] || '').trim() : '';
      if (!course && pts === null) continue;
      if (pts === null) { skipped += 1; continue; }
      rows.push({ course: course || 'Course', term, grade: String(gradeCell).trim().replace(/-/g, '−'), points: pts, credits: Number.isFinite(credits) && credits > 0 ? credits : null });
    }
    if (!rows.length) throw new Error('no rows');
    return { rows, skipped };
  }
  /** The GPA the rows make (credit-weighted when any row has credits), the courses, the credits. */
  function summarize(rows) {
    const weighted = rows.some((r) => r.credits !== null);
    const w = (r) => (weighted ? (r.credits ?? 1) : 1);
    const total = rows.reduce((s, r) => s + w(r), 0);
    const gpa = Math.round((rows.reduce((s, r) => s + r.points * w(r), 0) / total) * 1000) / 1000;
    return { gpa, courses: rows.length, credits: weighted ? Math.round(total * 100) / 100 : null };
  }
  /** The record as the tracking pref keeps it, from a file's text and name. */
  function record(text, name) {
    const { rows, skipped } = parse(text);
    const sum = summarize(rows);
    return { priorGpa: sum.gpa, priorCourses: sum.courses, record: { name: name || 'a CSV', at: new Date().toISOString(), courses: rows, credits: sum.credits, skipped } };
  }
  const TEMPLATE = ['term,course,grade,credits', 'Fall 2025,MATH 021,A-,4', 'Fall 2025,WRI 010,B+,4', 'Spring 2026,PHYS 008,A,4', 'Spring 2026,SPRK 010,P,1'].join('\n');
  /** Rows written back out in the shape parse reads: term, course, grade (a plain hyphen), credits. */
  function csv(rows) {
    const cell = (v) => { const t = String(v ?? '').replace(/−/g, '-'); return /[",\n]/.test(t) ? `"${t.replace(/"/g, '""')}"` : t; };
    return ['term,course,grade,credits', ...rows.map((r) => [r.term || '', r.course || '', r.grade || '', Number.isFinite(r.credits) ? r.credits : ''].map(cell).join(','))].join('\n');
  }
  /** A File's text. Read before the input it came from is cleared: Safari lets go of the file the
   *  moment the input is reset, and a read after that fails. Older engines without Blob.text read it
   *  the long way. */
  function readText(file) {
    if (file && typeof file.text === 'function') return file.text();
    return new Promise((resolve, reject) => { const r = new FileReader(); r.onload = () => resolve(String(r.result || '')); r.onerror = () => reject(r.error || new Error('could not read')); r.readAsText(file); });
  }
  /** What to tell someone whose file did not go in. */
  function explain(err) {
    const m = String(err?.message || err || '');
    if (m === 'no header') return 'That file needs a header with course and grade columns (credits and term optional).';
    if (m === 'no rows') return 'No row in that file has a grade that counts (P/NP, W and blank grades are skipped).';
    if (m === 'empty') return 'That file is empty.';
    return `The file could not be read${m ? `: ${m}` : '.'}`;
  }
  BCV.recordCsv = { points, cells, parse, summarize, record, csv, readText, explain, TEMPLATE };
})();
