/* Fallback screen: shows the page Canvas itself rendered (its #content
 * region) inside our shell, for anything that has no screen of its own —
 * quiz taking, external tools, file previews, profile and settings pages… */
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

  /** Builds the native block; `inCourse` skips the page header (the course shell has its own). */
  function block(ctx, { inCourse = false } = {}) {
    const app = ctx.app;
    const content = app.takeNative();
    const wrap = U.el(`bcv-native ${inCourse ? '' : ''} bcv-native--invert`);
    if (content) {
      wrap.append(content);
      document.documentElement.classList.add('bcv-native-mode');
    } else {
      wrap.append(U.empty('Canvas did not render anything for this page.'));
    }
    const note = U.el('bcv-native__bar', [
      U.text('bcv-native__note', 'This page is shown as Canvas drew it, inside the new look.'),
      h('span', { class: 'bcv-ml-auto' }),
      U.btn('Open in stock Canvas', { icon: IC.external, kind: 'xs', onClick: async () => BCV.settings.update({ appearance: { skin: false } }) }),
    ]);
    return U.el('bcv-col', [note, wrap]);
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
