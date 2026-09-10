/* Fallback screen: shows the page Canvas itself rendered (its #content
 * region, plus the page's own sub-navigation and sidebar) inside our shell,
 * for anything that has no screen of its own — quiz taking, external tools,
 * file previews, profile and settings pages… */
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

  /** Builds the native block; `inCourse` skips the page header and sub-nav (the course shell has its own). */
  function block(ctx, { inCourse = false } = {}) {
    const app = ctx.app;
    const content = app.takeNative();
    const wrap = U.el('bcv-native bcv-native--invert');
    if (content) {
      wrap.append(content);
      document.documentElement.classList.add('bcv-native-mode');
    } else {
      wrap.append(U.empty('Canvas did not render anything for this page.'));
    }
    const side = document.getElementById('right-side');
    const sideHasContent = side && side.textContent.trim().length > 0;
    const note = U.el('bcv-native__bar', [
      U.text('bcv-native__note', 'This page is shown as Canvas drew it, inside the new look.'),
      h('span', { class: 'bcv-ml-auto' }),
      U.btn('Open in stock Canvas', { icon: IC.external, kind: 'xs', onClick: async () => BCV.settings.update({ appearance: { skin: false } }) }),
    ]);
    const main = U.el('bcv-col', [note, subNav(app, { inCourse }), wrap], { style: { flex: '1 1 560px', minWidth: '0' } });
    if (!sideHasContent) return main;
    const sideWrap = U.el('bcv-native bcv-native--invert', side);
    return U.el('bcv-body--cols', [main, h('div', { class: 'bcv-col', style: { flex: '1 1 260px' } }, sideWrap)], { style: { padding: '0', display: 'flex', flexWrap: 'wrap', gap: '20px', alignItems: 'flex-start' } });
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
