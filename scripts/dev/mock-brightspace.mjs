#!/usr/bin/env node
// A fake Brightspace (D2L) for local testing: Brightspace-like pages under /d2l/ and the Valence API
// (/d2l/api/lp/… and /d2l/api/le/…) lib/d2l-api.js reads, as a student sees them — grades given and
// withheld, work handed in, late and missing, a discussion with replies, content with a module inside a
// module. Every name here is made up. Dates are relative to "now", so something is always due soon.
//
// Writes are kept (and listed at GET /__mock/log): a hand-in's multipart/mixed parts, a discussion post,
// a course pinned or unpinned. Each must carry the X-Csrf-Token the page's /d2l/lp/auth/xsrf-tokens gives,
// as Brightspace's own do, or it is refused (403).
//
// Usage: node scripts/dev/mock-brightspace.mjs [port]
import http from 'node:http';

const port = Number(process.argv[2] || process.env.PORT || 8860);
const now = new Date();
const H = 3600e3;
const D = 24 * H;
const at = (dayOffset, hour = 23, minute = 59) => {
  const d = new Date(now);
  d.setDate(d.getDate() + dayOffset);
  d.setHours(hour, minute, 0, 0);
  return d.toISOString();
};
const XSRF = 'mock-xsrf-7f3a';
const SESSION = 'd2lSessionVal';
const LP = '1.50';
const LE = '1.82';

// ---- who, and where -----------------------------------------------------------------------------------
const ORG = { Identifier: '6606', Name: 'Lakeside University', TimeZone: 'America/Los_Angeles' };
const ME = { Identifier: '7001', FirstName: 'Avery', LastName: 'Quinn', UniqueName: 'aquinn', ProfileIdentifier: 'p7001', Pronouns: null };
const PROFILE = { Email: 'avery.quinn@lakeside.example', ProfileImage: null, Nickname: '', HomeTown: '' };
const FALL = { Identifier: '9001', Name: 'Fall 2026', Code: 'F26' };
const SPRING = { Identifier: '8801', Name: 'Spring 2026', Code: 'S26' };

const COURSES = [
  { id: 31001, name: 'BIO 110: Cells and Systems', code: 'BIO-110-F26', semester: FALL, start: at(-30, 0, 0), end: at(80, 0, 0), pinned: true },
  { id: 31002, name: 'HIST 205: Modern Europe', code: 'HIST-205-F26', semester: FALL, start: at(-30, 0, 0), end: at(80, 0, 0), pinned: true },
  { id: 31003, name: 'MATH 140: Calculus I', code: 'MATH-140-F26', semester: FALL, start: at(-30, 0, 0), end: at(80, 0, 0), pinned: false },
  { id: 30990, name: 'ENG 101: Composition', code: 'ENG-101-S26', semester: SPRING, start: at(-220, 0, 0), end: at(-60, 0, 0), pinned: false },
  // closed to the student (the school ended their access): never listed by the interface
  { id: 30980, name: 'CHEM 101: General Chemistry', code: 'CHEM-101-S26', semester: SPRING, start: at(-220, 0, 0), end: at(-60, 0, 0), pinned: false, closed: true },
];
const course = (ou) => COURSES.find((c) => String(c.id) === String(ou));
const enrollment = (c) => ({
  OrgUnit: { Id: c.id, Type: { Id: 3, Code: 'Course Offering', Name: 'Course Offering' }, Name: c.name, Code: c.code, HomeUrl: `/d2l/home/${c.id}`, ImageUrl: null },
  Access: { IsActive: true, StartDate: c.start, EndDate: c.end, CanAccess: !c.closed, ClasslistRoleName: 'Student', LISRoles: ['urn:lti:role:ims/lis/Learner'], LastAccessed: at(-1, 9, 0) },
  PinDate: c.pinned ? at(-20, 9, 0) : null,
});
const ORG_ENROLLMENT = { OrgUnit: { Id: 6606, Type: { Id: 1, Code: 'Organization', Name: 'Organization' }, Name: ORG.Name, Code: null, HomeUrl: null, ImageUrl: null }, Access: { IsActive: true, StartDate: null, EndDate: null, CanAccess: true, ClasslistRoleName: 'Student', LISRoles: [], LastAccessed: null }, PinDate: null };

// ---- per course: the tools ----------------------------------------------------------------------------------
const rich = (html) => ({ Text: html.replace(/<[^>]+>/g, ''), Html: html });
const T = {
  31001: {
    setup: { GradingSystem: 'Weighted', IsNullGradeZero: false, DefaultGradeSchemeId: 1 },
    categories: [
      { Id: 601, Name: 'Labs', Weight: 40, NumberOfLowestToDrop: 0, NumberOfHighestToDrop: 0 },
      { Id: 602, Name: 'Exams', Weight: 60, NumberOfLowestToDrop: 0, NumberOfHighestToDrop: 0 },
    ],
    grades: [
      { Id: 501, Name: 'Lab 1: Microscopy', MaxPoints: 20, CategoryId: 601, GradeType: 'Numeric', AssociatedTool: { ToolId: 2000, ToolItemId: 701 } },
      { Id: 502, Name: 'Lab 2: Osmosis', MaxPoints: 20, CategoryId: 601, GradeType: 'Numeric', AssociatedTool: { ToolId: 2000, ToolItemId: 702 } },
      { Id: 503, Name: 'Midterm Quiz', MaxPoints: 50, CategoryId: 602, GradeType: 'Numeric', AssociatedTool: { ToolId: 51000, ToolItemId: 801 } },
      { Id: 504, Name: 'Participation', MaxPoints: 10, CategoryId: 601, GradeType: 'Numeric', AssociatedTool: null },
      { Id: 505, Name: 'Week 3 Discussion', MaxPoints: 10, CategoryId: 601, GradeType: 'Numeric', AssociatedTool: { ToolId: 3000, ToolItemId: 902 } },
      { Id: 506, Name: 'Final Exam', MaxPoints: 100, CategoryId: 602, GradeType: 'Numeric', AssociatedTool: null, IsHidden: true },
    ],
    values: [
      { GradeObjectIdentifier: '501', GradeObjectName: 'Lab 1: Microscopy', PointsNumerator: 18, PointsDenominator: 20, DisplayedGrade: '18 / 20', Comments: rich('<p>Clear drawings. Label the nucleolus next time.</p>'), LastModified: at(-3, 14, 0), ReleasedDate: at(-3, 14, 0) },
      { GradeObjectIdentifier: '503', GradeObjectName: 'Midterm Quiz', PointsNumerator: 41, PointsDenominator: 50, DisplayedGrade: '41 / 50', Comments: rich(''), LastModified: at(-1, 18, 0), ReleasedDate: at(-1, 18, 0) },
      { GradeObjectIdentifier: '504', GradeObjectName: 'Participation', PointsNumerator: 9, PointsDenominator: 10, DisplayedGrade: '9 / 10', Comments: rich(''), LastModified: at(-2, 10, 0), ReleasedDate: at(-2, 10, 0) },
    ],
    final: { PointsNumerator: 87.5, PointsDenominator: 100, WeightedNumerator: 87.5, WeightedDenominator: 100, DisplayedGrade: 'B+', GradeObjectIdentifier: '0' },
    folders: [
      { Id: 701, Name: 'Lab 1: Microscopy', DueDate: at(-5), SubmissionType: 0, GradeItemId: 501, Assessment: { ScoreDenominator: 20 }, CustomInstructions: rich('<p>Upload your drawings of the three slides as one PDF.</p>'), Availability: null, IsHidden: false, AllowableFileType: 0 },
      { Id: 702, Name: 'Lab 2: Osmosis', DueDate: at(2), SubmissionType: 0, GradeItemId: 502, Assessment: { ScoreDenominator: 20 }, CustomInstructions: rich('<p>Hand in your data table and the graph of mass against time.</p>'), Availability: null, IsHidden: false, AllowableFileType: 0 },
      { Id: 703, Name: 'Reflection Journal', DueDate: at(5), SubmissionType: 1, GradeItemId: null, Assessment: { ScoreDenominator: null }, CustomInstructions: rich('<p>A paragraph on what surprised you this week.</p>'), Availability: null, IsHidden: false, AllowableFileType: 0 },
      { Id: 704, Name: 'Lab Safety Form', DueDate: at(-2), SubmissionType: 0, GradeItemId: null, Assessment: { ScoreDenominator: 5 }, CustomInstructions: rich('<p>Signed and scanned.</p>'), Availability: null, IsHidden: false, AllowableFileType: 0 },
      { Id: 705, Name: 'Poster Session', DueDate: null, SubmissionType: 2, GradeItemId: null, Assessment: { ScoreDenominator: 15 }, CustomInstructions: rich('<p>Bring your poster to the atrium.</p>'), Availability: null, IsHidden: false, AllowableFileType: 0 },
      { Id: 706, Name: 'Draft (hidden)', DueDate: at(9), SubmissionType: 0, GradeItemId: null, Assessment: { ScoreDenominator: 5 }, CustomInstructions: rich(''), Availability: null, IsHidden: true, AllowableFileType: 0 },
    ],
    subs: {
      701: [{ Entity: { DisplayName: 'Avery Quinn', EntityId: 7001, EntityType: 'User', Active: true }, Status: 3, Feedback: { Score: 18, Feedback: rich('<p>Well drawn.</p>') }, Submissions: [{ Id: 1, SubmittedBy: { Identifier: '7001', DisplayName: 'Avery Quinn' }, SubmissionDate: at(-6, 20, 15), Comment: rich(''), Files: [{ FileId: 4401, FileName: 'microscopy.pdf', Size: 48211 }] }] }],
    },
    quizzes: [
      { QuizId: 801, Name: 'Midterm Quiz', IsActive: true, DueDate: at(-1, 17, 0), StartDate: at(-3, 8, 0), EndDate: null, GradeItemId: 503, AttemptsAllowed: { IsUnlimited: false, NumberOfAttemptsAllowed: 1 }, SubmissionTimeLimit: { IsEnforced: true, TimeLimitValue: 45 }, Description: { Text: rich('<p>Chapters 1–3.</p>') } },
      { QuizId: 802, Name: 'Chapter 4 Check', IsActive: true, DueDate: at(3, 17, 0), StartDate: null, EndDate: null, GradeItemId: null, AttemptsAllowed: { IsUnlimited: false, NumberOfAttemptsAllowed: 2 }, SubmissionTimeLimit: { IsEnforced: false }, Description: { Text: rich('') } },
    ],
    forums: [{ ForumId: 900, Name: 'Weekly Discussions', Description: rich(''), MustPostToParticipate: false }],
    topics: {
      900: [
        { TopicId: 901, ForumId: 900, Name: 'Introduce Yourself', Description: rich('<p>Say hello, and what brings you to biology.</p>'), StartDate: at(-28, 8, 0), EndDate: null, DueDate: null, ScoreOutOf: null, GradeItemId: null, IsHidden: false, IsLocked: false, UnreadPostCount: 0 },
        { TopicId: 902, ForumId: 900, Name: 'Week 3 Discussion', Description: rich('<p>Is a virus alive? Make a case either way.</p>'), StartDate: at(-6, 8, 0), EndDate: null, DueDate: at(4), ScoreOutOf: 10, GradeItemId: 505, IsHidden: false, IsLocked: false, UnreadPostCount: 2 },
      ],
    },
    posts: {
      902: [
        { PostId: 5001, ParentPostId: null, PostingUserId: 7101, PostingUserDisplayName: 'Jordan Reyes', DatePosted: at(-2, 11, 5), Subject: 'Week 3 Discussion', Message: { Html: '<p>Not alive: it cannot reproduce on its own.</p>' }, IsDeleted: false },
        { PostId: 5002, ParentPostId: 5001, PostingUserId: 7102, PostingUserDisplayName: 'Sam Patel', DatePosted: at(-1, 16, 40), Subject: 'Re: Week 3 Discussion', Message: { Html: '<p>Neither can a mule, and a mule is alive.</p>' }, IsDeleted: false },
      ],
    },
    news: [
      { Id: 1101, Title: 'Welcome to BIO 110', Body: rich('<p>The syllabus is under Content. Labs start in week two.</p>'), StartDate: at(-6, 8, 0), CreatedDate: at(-6, 8, 0), IsPublished: true, IsHidden: false, Attachments: [], IsPinned: false },
      { Id: 1102, Title: 'Lab 2 moved to Thursday', Body: rich('<p>The osmosis lab is on Thursday this week.</p>'), StartDate: at(-1, 9, 0), CreatedDate: at(-1, 9, 0), IsPublished: true, IsHidden: false, Attachments: [], IsPinned: false },
    ],
    toc: {
      Modules: [
        { ModuleId: 1201, Title: 'Week 1: The Cell', SortOrder: 1, IsHidden: false, IsLocked: false, StartDateTime: null, Modules: [
          { ModuleId: 1202, Title: 'Optional Reading', SortOrder: 5, IsHidden: false, IsLocked: false, StartDateTime: null, Modules: [], Topics: [
            { TopicId: 1304, Title: 'Cell Biology Primer', SortOrder: 6, TypeIdentifier: 'Link', ActivityType: 2, Url: 'https://example.org/primer', ToolItemId: null, CompletionType: 2, IsHidden: false, IsLocked: false, LastModifiedDate: at(-30, 9, 0) },
          ] },
        ], Topics: [
          { TopicId: 1301, Title: 'Syllabus', SortOrder: 2, TypeIdentifier: 'File', ActivityType: 1, Url: '/content/enforced/31001-BIO-110-F26/Syllabus.html', ToolItemId: null, CompletionType: 2, IsHidden: false, IsLocked: false, LastModifiedDate: at(-30, 9, 0) },
          { TopicId: 1302, Title: 'Cell Diagram', SortOrder: 3, TypeIdentifier: 'File', ActivityType: 1, Url: '/content/enforced/31001-BIO-110-F26/Cell%20Diagram.pdf', ToolItemId: null, CompletionType: 2, IsHidden: false, IsLocked: false, LastModifiedDate: at(-30, 9, 0) },
          { TopicId: 1303, Title: 'Lab 1: Microscopy', SortOrder: 4, TypeIdentifier: 'Link', ActivityType: 3, Url: '/d2l/common/dialogs/quickLink/quickLink.d2l?ou=31001&type=dropbox&rcode=x-701', ToolItemId: 701, CompletionType: 2, IsHidden: false, IsLocked: false, LastModifiedDate: at(-30, 9, 0) },
          { TopicId: 1305, Title: 'Introduce Yourself', SortOrder: 9, TypeIdentifier: 'Link', ActivityType: 6, Url: '/d2l/common/dialogs/quickLink/quickLink.d2l?ou=31001&type=discuss&rcode=x-901', ToolItemId: 901, CompletionType: 2, IsHidden: false, IsLocked: false, LastModifiedDate: at(-30, 9, 0) },
        ] },
        { ModuleId: 1203, Title: 'Week 2: Membranes', SortOrder: 20, IsHidden: false, IsLocked: false, StartDateTime: null, Modules: [], Topics: [
          { TopicId: 1306, Title: 'Membranes Notes', SortOrder: 21, TypeIdentifier: 'File', ActivityType: 1, Url: '/content/enforced/31001-BIO-110-F26/Membranes.html', ToolItemId: null, CompletionType: 2, IsHidden: false, IsLocked: false, LastModifiedDate: at(-8, 9, 0) },
          { TopicId: 1307, Title: 'Chapter 4 Check', SortOrder: 22, TypeIdentifier: 'Link', ActivityType: 4, Url: '/d2l/common/dialogs/quickLink/quickLink.d2l?ou=31001&type=quiz&rcode=x-802', ToolItemId: 802, CompletionType: 2, IsHidden: false, IsLocked: false, LastModifiedDate: at(-8, 9, 0) },
          { TopicId: 1308, Title: 'Lab Checklist', SortOrder: 23, TypeIdentifier: 'Link', ActivityType: 10, Url: '/d2l/common/dialogs/quickLink/quickLink.d2l?ou=31001&type=checklist&rcode=x-77', ToolItemId: 77, CompletionType: 3, IsHidden: false, IsLocked: false, LastModifiedDate: at(-8, 9, 0) },
        ] },
        { ModuleId: 1204, Title: 'Instructor notes', SortOrder: 30, IsHidden: true, IsLocked: false, StartDateTime: null, Modules: [], Topics: [] },
      ],
    },
    files: {
      1301: { type: 'text/html', body: '<!doctype html><html><head><title>Syllabus</title></head><body><h1>BIO 110 Syllabus</h1><p>Labs are worth 40% and exams 60%.</p></body></html>' },
      1302: { type: 'application/pdf', body: '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n' },
      1306: { type: 'text/html', body: '<!doctype html><html><body><h2>Membranes</h2><p>The phospholipid bilayer is selectively permeable.</p></body></html>' },
    },
    calendar: [
      { CalendarEventId: 1401, Title: 'Lab office hours', StartDateTime: at(1, 15, 0), EndDateTime: at(1, 16, 0), IsAllDayEvent: false, IsAssociatedWithEntity: false, LocationName: 'Science Hall 204', Description: 'Bring your data.' },
      { CalendarEventId: 1402, Title: 'Lab 2: Osmosis - Due', StartDateTime: at(2), EndDateTime: at(2), IsAllDayEvent: false, IsAssociatedWithEntity: true, LocationName: '', Description: '' },
    ],
    classlist: [
      { Identifier: '7001', FirstName: 'Avery', LastName: 'Quinn', DisplayName: 'Quinn, Avery', ClasslistRoleDisplayName: 'Student', Username: 'aquinn', Email: 'avery.quinn@lakeside.example' },
      { Identifier: '7101', FirstName: 'Jordan', LastName: 'Reyes', DisplayName: 'Reyes, Jordan', ClasslistRoleDisplayName: 'Student', Username: 'jreyes', Email: null },
      { Identifier: '7102', FirstName: 'Sam', LastName: 'Patel', DisplayName: 'Patel, Sam', ClasslistRoleDisplayName: 'Student', Username: 'spatel', Email: null },
      { Identifier: '7201', FirstName: 'Lena', LastName: 'Ortiz', DisplayName: 'Ortiz, Lena', ClasslistRoleDisplayName: 'Instructor', Username: 'lortiz', Email: 'lena.ortiz@lakeside.example' },
    ],
  },
  31002: {
    setup: { GradingSystem: 'Points', IsNullGradeZero: false },
    categories: [],
    grades: [{ Id: 521, Name: 'Essay Proposal', MaxPoints: 25, CategoryId: 0, GradeType: 'Numeric', AssociatedTool: { ToolId: 2000, ToolItemId: 721 } }],
    values: [],
    final: null,
    folders: [
      { Id: 721, Name: 'Essay Proposal', DueDate: at(1, 12, 0), SubmissionType: 4, GradeItemId: 521, Assessment: { ScoreDenominator: 25 }, CustomInstructions: rich('<p>One page: your question and three sources.</p>'), Availability: null, IsHidden: false, AllowableFileType: 0 },
      { Id: 722, Name: 'Source Analysis', DueDate: at(9), SubmissionType: 0, GradeItemId: null, Assessment: { ScoreDenominator: 30 }, CustomInstructions: rich(''), Availability: null, IsHidden: false, AllowableFileType: 0 },
    ],
    subs: {},
    quizzes: [],
    forums: [],
    topics: {},
    posts: {},
    news: [{ Id: 1121, Title: 'Reading for Monday', Body: rich('<p>Hobsbawm, chapter two.</p>'), StartDate: at(-2, 8, 0), CreatedDate: at(-2, 8, 0), IsPublished: true, IsHidden: false, Attachments: [], IsPinned: false }],
    toc: { Modules: [{ ModuleId: 1221, Title: 'Unit 1: Revolutions', SortOrder: 1, IsHidden: false, IsLocked: false, Modules: [], Topics: [] }] },
    files: {},
    calendar: [],
    classlist: [
      { Identifier: '7001', FirstName: 'Avery', LastName: 'Quinn', DisplayName: 'Quinn, Avery', ClasslistRoleDisplayName: 'Student' },
      { Identifier: '7202', FirstName: 'Tomas', LastName: 'Berg', DisplayName: 'Berg, Tomas', ClasslistRoleDisplayName: 'Instructor' },
    ],
  },
  31003: {
    setup: { GradingSystem: 'Points' }, categories: [], grades: [], values: [], final: null,
    folders: [{ Id: 731, Name: 'Problem Set 3', DueDate: at(6), SubmissionType: 0, GradeItemId: null, Assessment: { ScoreDenominator: 20 }, CustomInstructions: rich(''), Availability: null, IsHidden: false, AllowableFileType: 5, CustomAllowableFileTypes: 'pdf, .png' }],
    subs: {}, quizzes: [], forums: [], topics: {}, posts: {}, news: [], toc: { Modules: [] }, files: {}, calendar: [], classlist: [],
  },
  30990: {
    setup: { GradingSystem: 'Points' }, categories: [], grades: [], values: [], final: { PointsNumerator: 93, PointsDenominator: 100, DisplayedGrade: 'A' },
    folders: [], subs: {}, quizzes: [], forums: [], topics: {}, posts: {}, news: [], toc: { Modules: [] }, files: {}, calendar: [], classlist: [],
  },
};

// ---- writes, kept for the tests ----------------------------------------------------------------------------
const log = [];
let nextPostId = 6000;
let nextSubId = 100;
let nextFileId = 9000;

/** A multipart body's parts: [{ headers, body: Buffer }]. */
function parts(buf, boundary) {
  const out = [];
  const sep = Buffer.from(`--${boundary}`);
  let i = buf.indexOf(sep);
  while (i >= 0) {
    const start = i + sep.length;
    if (buf.slice(start, start + 2).toString() === '--') break;
    const next = buf.indexOf(sep, start);
    if (next < 0) break;
    const chunk = buf.slice(start + 2, next - 2); // (past the CRLF after the boundary; before the CRLF ahead of the next)
    const headEnd = chunk.indexOf('\r\n\r\n');
    const head = chunk.slice(0, headEnd).toString();
    const headers = Object.fromEntries(head.split('\r\n').filter(Boolean).map((l) => { const k = l.indexOf(':'); return [l.slice(0, k).trim().toLowerCase(), l.slice(k + 1).trim()]; }));
    out.push({ headers, body: chunk.slice(headEnd + 4) });
    i = next;
  }
  return out;
}

// ---- pages ------------------------------------------------------------------------------------------------------
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
function page(title, ou, body, { signedIn = true } = {}) {
  const ctx = signedIn ? JSON.stringify({ orgUnitId: String(ou || ORG.Identifier), orgId: ORG.Identifier, userId: ME.Identifier }) : '{}';
  return `<!DOCTYPE html><html lang="en" data-global-context='${esc(ctx)}'><head><meta charset="utf-8"><title>${esc(title)} - ${esc(ORG.Name)}</title>
<style>body{font-family:Lato,sans-serif;margin:0}d2l-navigation{display:block;background:#004489;color:#fff;padding:14px 24px}.d2l-page-main{padding:24px}</style></head>
<body class="d2l-body"><d2l-navigation class="d2l-navigation-s"><span>${esc(ORG.Name)}</span></d2l-navigation><div class="d2l-page-main" id="d2l_body"><h1 class="d2l-page-title">${esc(title)}</h1>${body}</div></body></html>`;
}
const loginPage = (target = '') => `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>Login - ${esc(ORG.Name)}</title></head><body class="d2l-body">
<form id="d2l_login" method="post" action="/d2l/lp/auth/login/login.d2l"><input type="hidden" name="target" value="${esc(target)}"><input type="hidden" name="loginPath" value="/d2l/login">
<label>Username <input id="userName" name="userName"></label><label>Password <input id="password" name="password" type="password"></label><button type="submit" id="login">Log In</button></form></body></html>`;

// ---- the server ---------------------------------------------------------------------------------------------------
const json = (res, status, body) => { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(body === undefined ? '' : JSON.stringify(body)); };
const html = (res, status, body, headers = {}) => { res.writeHead(status, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', ...headers }); res.end(body); };
const redirect = (res, to, headers = {}) => { res.writeHead(302, { location: to, ...headers }); res.end(); };
const signedIn = (req) => new RegExp(`(?:^|;\\s*)${SESSION}=ok`).test(req.headers.cookie || '');

function api(req, res, path, q, body) {
  const m = (re) => path.match(re);
  const write = req.method !== 'GET';
  if (write && req.headers['x-csrf-token'] !== XSRF) return json(res, 403, { Errors: [{ Message: 'Invalid XSRF token' }] });
  let r;
  // the platform (lp)
  if (path === '/d2l/api/versions/') return json(res, 200, [
    { ProductCode: 'lp', Version: '1.64', LatestVersion: '1.64', SupportedVersions: ['1.40', '1.45', '1.50', '1.55', '1.64'] },
    { ProductCode: 'le', Version: '1.100', LatestVersion: '1.100', SupportedVersions: ['1.70', '1.75', '1.82', '1.90', '1.100'] },
  ]);
  if ((r = m(/^\/d2l\/api\/lp\/[\d.]+\/(.*)$/))) {
    const p = r[1];
    if (p === 'users/whoami') return json(res, 200, ME);
    if (p === 'profile/myProfile') return json(res, 200, PROFILE);
    if (p === 'organization/info') return json(res, 200, ORG);
    if (p === 'enrollments/myenrollments/') {
      // two pages, as Brightspace pages a long list: the organization and the first course, then the rest
      const all = [ORG_ENROLLMENT, ...COURSES.map(enrollment)];
      const first = !q.get('bookmark');
      return json(res, 200, { PagingInfo: { Bookmark: first ? 'b2' : '', HasMoreItems: first }, Items: first ? all.slice(0, 2) : all.slice(2) });
    }
    if ((r = p.match(/^enrollments\/myenrollments\/(\d+)\/pin$/))) {
      const c = course(r[1]);
      if (!c) return json(res, 404, {});
      c.pinned = req.method === 'POST';
      log.push({ kind: 'pin', ou: c.id, pinned: c.pinned });
      return json(res, 200, enrollment(c));
    }
    if ((r = p.match(/^courses\/(\d+)$/))) {
      const c = course(r[1]);
      if (!c) return json(res, 404, {});
      return json(res, 200, { Identifier: String(c.id), Name: c.name, Code: c.code, IsActive: true, Path: `/content/enforced/${c.id}-${c.code}/`, StartDate: c.start, EndDate: c.end, Semester: c.semester, Department: null, Description: rich('') });
    }
    return json(res, 404, { Errors: [{ Message: `No such route: ${p}` }] });
  }
  // the learning tools (le)
  if ((r = m(/^\/d2l\/api\/le\/[\d.]+\/(\d+)\/(.*)$/))) {
    const ou = r[1];
    const p = r[2];
    const t = T[ou];
    if (!t) return json(res, 403, { Errors: [{ Message: 'Not authorized' }] });
    if (p === 'dropbox/folders/') return json(res, 200, t.folders);
    if ((r = p.match(/^dropbox\/folders\/(\d+)\/submissions\/mysubmissions\/$/))) {
      const fid = r[1];
      const f = t.folders.find((x) => String(x.Id) === fid);
      if (!f) return json(res, 404, {});
      if (req.method === 'GET') return json(res, 200, t.subs[fid] || []);
      const type = req.headers['content-type'] || '';
      const b = /boundary=([^;]+)/.exec(type)?.[1];
      if (!/^multipart\/mixed/i.test(type) || !b) return json(res, 400, { Errors: [{ Message: 'Expected multipart/mixed' }] });
      const ps = parts(body, b);
      const head = ps[0];
      let comment = null;
      try { comment = JSON.parse(head.body.toString()); } catch { /* bad */ }
      const files = ps.slice(1).map((x) => ({ name: /filename="([^"]*)"/.exec(x.headers['content-disposition'] || '')?.[1] || null, type: x.headers['content-type'] || null, size: x.body.length, text: /text|html/.test(x.headers['content-type'] || '') ? x.body.toString() : null }));
      if (!comment || /json/i.test(head.headers['content-type'] || '') === false || !files.length || files.some((x) => !x.name)) return json(res, 400, { Errors: [{ Message: 'A JSON part, then one part per file' }] });
      log.push({ kind: 'submit', ou: Number(ou), folder: Number(fid), comment, files });
      const list = (t.subs[fid] ||= [{ Entity: { DisplayName: 'Avery Quinn', EntityId: 7001, EntityType: 'User', Active: true }, Status: 0, Feedback: null, Submissions: [] }]);
      list[0].Status = 1;
      list[0].Submissions.push({ Id: nextSubId++, SubmittedBy: { Identifier: ME.Identifier, DisplayName: 'Avery Quinn' }, SubmissionDate: new Date().toISOString(), Comment: { Text: comment.Text || '', Html: comment.Html || '' }, Files: files.map((x) => ({ FileId: nextFileId++, FileName: x.name, Size: x.size })) });
      return json(res, 200);
    }
    if (p === 'quizzes/') return json(res, 200, { Objects: t.quizzes, Next: null });
    if (p === 'grades/') return json(res, 200, t.grades.map((g) => ({ Description: rich(''), ExcludeFromFinalGradeCalculation: false, IsHidden: false, ...g })));
    if (p === 'grades/categories/') return json(res, 200, t.categories);
    if (p === 'grades/setup/') return json(res, 200, t.setup);
    if (p === 'grades/values/myGradeValues/') return json(res, 200, t.values);
    if (p === 'grades/final/values/myGradeValue') return t.final ? json(res, 200, t.final) : json(res, 404, { Errors: [{ Message: 'No final grade' }] });
    if (p === 'news/') return json(res, 200, t.news);
    if (p === 'discussions/forums/') return json(res, 200, t.forums);
    if ((r = p.match(/^discussions\/forums\/(\d+)\/topics\/$/))) return json(res, 200, t.topics[r[1]] || []);
    if ((r = p.match(/^discussions\/forums\/(\d+)\/topics\/(\d+)\/posts\/$/))) {
      const [, fid, tid] = r;
      const tp = (t.topics[fid] || []).find((x) => String(x.TopicId) === tid);
      if (!tp) return json(res, 404, {});
      const posts = (t.posts[tid] ||= []);
      if (req.method === 'GET') return json(res, 200, posts);
      let b = null;
      try { b = JSON.parse(body.toString()); } catch { /* bad */ }
      if (!b || !b.Message || typeof b.Message.Content !== 'string') return json(res, 400, { Errors: [{ Message: 'A post needs a Message' }] });
      const post = { PostId: nextPostId++, ParentPostId: b.ParentPostId || null, PostingUserId: Number(ME.Identifier), PostingUserDisplayName: 'Avery Quinn', DatePosted: new Date().toISOString(), Subject: b.Subject || '', Message: { Html: b.Message.Content }, IsDeleted: false };
      posts.push(post);
      log.push({ kind: 'post', ou: Number(ou), topic: Number(tid), parent: post.ParentPostId, subject: post.Subject, html: b.Message.Content });
      return json(res, 200, post);
    }
    if (p === 'content/toc') return json(res, 200, t.toc);
    if ((r = p.match(/^content\/topics\/(\d+)\/file$/))) {
      const f = t.files[r[1]];
      if (!f) return json(res, 404, {});
      res.writeHead(200, { 'content-type': f.type, 'cache-control': 'no-store' });
      return res.end(f.body);
    }
    if (p === 'calendar/events/') return json(res, 200, t.calendar);
    if (p === 'classlist/') return json(res, 200, t.classlist);
    return json(res, 404, { Errors: [{ Message: `No such route: ${p}` }] });
  }
  return json(res, 404, { Errors: [{ Message: 'Not found' }] });
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://localhost:${port}`);
  const path = url.pathname;
  const q = url.searchParams;
  const chunks = [];
  req.on('data', (c) => chunks.push(c));
  req.on('end', () => {
    const body = Buffer.concat(chunks);
    try {
      // the tests' own door: what was written, and a fresh start
      if (path === '/__mock/log') return json(res, 200, log);
      if (path === '/__mock/reset') { log.length = 0; return json(res, 200, {}); }
      // signing in: Brightspace's own form, its session cookie
      if (path === '/d2l/login' || path === '/d2l/lp/auth/login/login.d2l') {
        if (req.method === 'POST') {
          const f = new URLSearchParams(body.toString());
          if (f.get('userName') && f.get('password')) return redirect(res, f.get('target') || '/d2l/home', { 'set-cookie': `${SESSION}=ok; Path=/; HttpOnly` });
          return html(res, 200, loginPage(f.get('target') || ''));
        }
        return html(res, 200, loginPage(q.get('target') || ''));
      }
      if (path === '/d2l/logout') return redirect(res, '/d2l/login', { 'set-cookie': `${SESSION}=; Path=/; Max-Age=0` });
      if (!signedIn(req)) {
        if (path.startsWith('/d2l/api/')) return json(res, 401, { Errors: [{ Message: 'Not signed in' }] });
        return redirect(res, `/d2l/login?target=${encodeURIComponent(path + url.search)}&sessionExpired=1`);
      }
      if (path === '/d2l/lp/auth/xsrf-tokens') return json(res, 200, { referrerToken: XSRF, hitCodePrefix: '1' });
      if (path.startsWith('/d2l/api/')) return api(req, res, path, q, body);
      // the pages the interface draws over (the homepage, a course's) and the ones it leaves as Brightspace draws them
      let r;
      if (path === '/d2l/home' || path === '/d2l/home/') return html(res, 200, page('Homepage', null, '<d2l-my-courses></d2l-my-courses><p class="d2l-widget">My Courses</p>'));
      if ((r = path.match(/^\/d2l\/home\/(\d+)\/?$/))) { const c = course(r[1]); return c && !c.closed ? html(res, 200, page(c.name, c.id, '<p class="d2l-widget">Course homepage</p>')) : redirect(res, `/d2l/error/404/log?targetUrl=${encodeURIComponent(path)}`); }
      if (path === '/d2l/lms/dropbox/user/folders_list.d2l') return html(res, 200, page('Assignments', q.get('ou'), `<table id="d2l-folders"><tr><td>${(T[q.get('ou')]?.folders || []).filter((f) => !f.IsHidden).map((f) => esc(f.Name)).join('</td></tr><tr><td>')}</td></tr></table>`));
      if (path === '/d2l/lms/quizzing/user/quiz_summary.d2l') return html(res, 200, page('Quiz Summary', q.get('ou'), `<p id="d2l-quiz-summary">Summary of quiz ${esc(q.get('qi') || '')}</p><button id="d2l-quiz-start">Start Quiz!</button>`));
      if ((r = path.match(/^\/d2l\/le\/content\/(\d+)\/viewContent\/(\d+)\/View$/))) return html(res, 200, page('Content', r[1], `<p id="d2l-content-viewer">Topic ${esc(r[2])}</p>`));
      if ((r = path.match(/^\/d2l\/le\/content\/(\d+)\/fullscreen\/(\d+)\/View$/))) return html(res, 200, `<!DOCTYPE html><html><body><p id="d2l-fullscreen-viewer">Topic ${esc(r[2])}, without the header</p></body></html>`);
      if (path === '/d2l/error/404/log') return html(res, 404, page('Page Not Found', null, `<p id="d2l-404">The page ${esc(q.get('targetUrl') || '')} could not be found.</p>`));
      if (path === '/favicon.ico') { res.writeHead(204); return res.end(); }
      // anything else — a Canvas-style address among them — is Brightspace's 404, which names it
      return redirect(res, `/d2l/error/404/log?targetUrl=${encodeURIComponent(path + url.search)}`);
    } catch (e) {
      console.error(e);
      json(res, 500, { Errors: [{ Message: String(e.message || e) }] });
    }
  });
});

server.listen(port, () => console.log(`Mock Brightspace listening on http://localhost:${port}`));
