/* Fallback screen for anything that has no screen of its own — external
 * tools, file previews, profile and settings pages, Canvas's own quiz page…
 * The page Canvas rendered is left untouched in the DOM (moving it would
 * reload tool launches and embeds); our shell floats over it and leaves a
 * hole that Canvas's content is laid out into (see app.punchIn). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;

  function titleFromPage() {
    const t = document.title.replace(/\s*[:·-]\s*[^:·-]+$/, '').trim();
    const h1 = document.querySelector('#content h1, #content .page-title, #content h2');
    return (h1 && h1.textContent.trim()) || t || 'Canvas';
  }

  /** Canvas's own sub-navigation for this page (course/group/account menu), as pills. */
  function subNav(app, { inCourse }) {
    if (inCourse) return null;
    const links = Array.from(document.querySelectorAll('#section-tabs a[href], #left-side nav a[href]'));
    if (!links.length) return null;
    return U.el('bcv-tabs bcv-native__tabs', links.map((a) => {
      const active = a.classList.contains('active') || a.getAttribute('aria-current') === 'page';
      const href = a.getAttribute('href');
      return h('button', { type: 'button', class: `bcv-tab ${active ? 'is-active' : ''}`, onclick: () => app.go(href) }, [U.svg(IC.page, { size: 13, stroke: active ? '#fff' : 'var(--bcv-ink2)', width: 1.9 }), a.textContent.trim()]);
    }));
  }

  /** Builds the native block: a note bar, the page's sub-nav (outside a
   *  course shell) and the hole Canvas's page shows through. `inCourse`
   *  skips the sub-nav (the course shell has its own rail). */
  function block(ctx, { inCourse = false } = {}) {
    const app = ctx.app;
    const hasContent = !!(document.getElementById('content') || document.getElementById('main'));
    const note = U.el('bcv-native__bar', [
      U.text('bcv-native__note', 'This page is shown as Canvas drew it, inside the new look.'),
      h('span', { class: 'bcv-ml-auto' }),
      U.btn('Open in stock Canvas', { icon: IC.external, kind: 'xs', onClick: async () => BCV.settings.update({ appearance: { skin: false } }) }),
    ]);
    const hole = U.el('bcv-native__hole', hasContent ? null : U.emptyCard('Canvas did not render anything for this page.'));
    if (hasContent) app.punchIn(hole);
    return U.el('bcv-native', [note, subNav(app, { inCourse }), hole]);
  }

  async function render(ctx) {
    const screen = U.el('bcv-screen', null, { style: { '--w': '1040px' } });
    screen.append(
      U.el('bcv-head bcv-head--tight', U.el('bcv-head__in', h('h1', { class: 'bcv-h1 bcv-h1--30', text: titleFromPage() }))),
      U.el('bcv-body bcv-body--24', block(ctx)),
    );
    ctx.setSmart({ label: titleFromPage(), actions: [], context: () => BCV.utils.elementText(document.getElementById('content'), 12000) });
    return screen;
  }

  BCV.screens.native = { render, block };
})();
