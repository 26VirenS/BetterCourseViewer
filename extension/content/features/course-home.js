/* Course overview: on a course's home page, a header with the grade ring,
 * instructors and term, plus up-next items, module progress and recent
 * announcements, rendered above Canvas's own home content. */
(function () {
  const BCV = self.BCV;
  const { h, formatDue, relative, urgency, htmlToText, DAY } = BCV.utils;
  BCV.features = BCV.features || [];

  const state = { el: null };
  const skinOn = () => document.documentElement.classList.contains('bcv-skin');

  function scoreOf(course) {
    const e = (course.enrollments || []).find((x) => x.type === 'student') || (course.enrollments || [])[0];
    return e?.computed_current_score == null ? null : Number(e.computed_current_score);
  }

  function nextRow(it) {
    const u = it.isDue ? urgency(it.due) : 'scheduled';
    return h('a', { class: `bcv-next bcv-next--${u}`, href: it.url || '#', title: `${it.typeLabel} · ${it.due.toLocaleString()}` }, [
      h('span', { class: 'bcv-next__icon', html: BCV.dueData.iconFor(it.iconKey) }),
      h('span', { class: 'bcv-next__title', text: it.title }),
      h('span', { class: 'bcv-next__when', text: it.isDue ? (u === 'overdue' ? `Overdue · ${relative(it.due)}` : formatDue(it.due)) : `${it.type === 'event' ? 'Event' : 'To-do'} · ${formatDue(it.due)}` }),
    ]);
  }

  function moduleRow(m, courseId) {
    const items = (m.items || []).filter((i) => i.type !== 'SubHeader');
    const required = items.filter((i) => i.completion_requirement);
    const completed = required.filter((i) => i.completion_requirement?.completed);
    const value = required.length ? completed.length / required.length : (m.state === 'completed' ? 1 : 0);
    const label = required.length ? `${completed.length}/${required.length}` : `${items.length}`;
    return h('a', { class: `bcv-mod${m.state === 'locked' ? ' is-locked' : ''}`, href: `/courses/${courseId}/modules#context_module_${m.id}`, title: required.length ? `${completed.length} of ${required.length} requirements done` : `${items.length} items` }, [
      BCV.ui.ring({ size: 30, stroke: 3.5, value, label: '', color: value >= 1 ? 'var(--bcv-ok)' : 'var(--bcv-accent)', title: '' }),
      h('span', { class: 'bcv-mod__name', text: m.name }),
      h('span', { class: 'bcv-mod__meta', text: m.state === 'locked' ? 'Locked' : label }),
    ]);
  }

  async function build(courseId) {
    const C = BCV.canvas;
    const [course, modules, anns] = await Promise.all([
      C.course(courseId),
      C.courseModules(courseId).catch(() => []),
      C.announcements([courseId]).catch(() => []),
    ]);
    const colors = await C.courseColors().catch(() => ({}));
    const color = BCV.ui.paint(colors[`course_${courseId}`] || '#8e8e93');
    const score = scoreOf(course);
    const now = Date.now();
    const next = (BCV.dueData?.items || [])
      .filter((i) => i.courseId === String(courseId) && i.due && !i.done && i.due > now - 7 * DAY)
      .sort((a, b) => (b.isDue - a.isDue) || (a.due - b.due))
      .slice(0, 6);
    const teachers = (course.teachers || []).map((t) => t.display_name).filter(Boolean);
    const hasModulesOnPage = !!document.getElementById('context_modules');

    const head = h('div', { class: 'bcv-ch__head', style: { '--c': color } }, [
      h('div', { class: 'bcv-ch__text' }, [
        h('div', { class: 'bcv-ch__eyebrow' }, [h('span', { class: 'bcv-side__dot' }), course.course_code || '']),
        h('h1', { class: 'bcv-ch__title', text: course.name }),
        h('p', { class: 'bcv-ch__meta', text: [course.term?.name, teachers.length ? teachers.join(', ') : null].filter(Boolean).join(' · ') }),
      ]),
      h('div', { class: 'bcv-ch__grade' }, [
        BCV.ui.ring({ size: 72, stroke: 6, value: score != null ? score / 100 : 0, label: score != null ? `${Math.round(score)}%` : '–', color: score != null ? color : 'var(--bcv-bg-3)', title: score != null ? `Current grade ${score}%` : 'No grade yet' }),
        h('a', { class: 'bcv-ch__grade-label', href: `/courses/${courseId}/grades`, text: score != null ? 'Current grade' : 'Grades' }),
      ]),
    ]);

    const cols = [];
    cols.push(h('div', { class: 'bcv-ch__col' }, [
      h('h3', { class: 'bcv-h3', text: 'Up next' }),
      next.length ? h('div', { class: 'bcv-ch__list' }, next.map(nextRow)) : h('div', { class: 'bcv-ch__empty', text: 'Nothing coming up' }),
    ]));
    if (modules?.length && !hasModulesOnPage) {
      cols.push(h('div', { class: 'bcv-ch__col' }, [
        h('h3', { class: 'bcv-h3' }, [h('a', { href: `/courses/${courseId}/modules`, text: 'Modules' })]),
        h('div', { class: 'bcv-ch__list' }, modules.slice(0, 8).map((m) => moduleRow(m, courseId))),
      ]));
    }
    if (anns?.length) {
      cols.push(h('div', { class: 'bcv-ch__col' }, [
        h('h3', { class: 'bcv-h3' }, [h('a', { href: `/courses/${courseId}/announcements`, text: 'Announcements' })]),
        h('div', { class: 'bcv-ch__list' }, anns.slice(0, 4).map((a) => h('a', { class: 'bcv-ann bcv-ann--compact', href: a.html_url || '#' }, [
          h('span', { class: 'bcv-ann__title', text: a.title }),
          h('span', { class: 'bcv-ann__snippet', text: htmlToText(a.message, 120).replace(/\s+/g, ' ') }),
          h('span', { class: 'bcv-ann__when', text: relative(a.posted_at) }),
        ]))),
      ]));
    }
    return h('section', { id: 'bcv-course-hero', class: 'bcv-ui bcv-ch', 'aria-label': 'Course overview' }, [head, h('div', { class: 'bcv-ch__grid' }, cols)]);
  }

  BCV.features.push({
    id: 'course-home',
    async init() {
      if (!skinOn() || BCV.page?.kind !== 'course-home' || !BCV.page.courseId) return;
      const content = document.getElementById('content');
      if (!content) return;
      try {
        state.el = await build(BCV.page.courseId);
        content.prepend(state.el);
        document.documentElement.classList.add('bcv-course-hero-active');
        BCV.dueData?.onChange(async () => {
          const fresh = await build(BCV.page.courseId).catch(() => null);
          if (fresh && state.el) {
            state.el.replaceWith(fresh);
            state.el = fresh;
          }
        });
      } catch {
        /* leave Canvas's home as is */
      }
    },
    onSettings() {
      if (!skinOn()) {
        state.el?.remove();
        state.el = null;
        document.documentElement.classList.remove('bcv-course-hero-active');
      }
    },
  });
})();
