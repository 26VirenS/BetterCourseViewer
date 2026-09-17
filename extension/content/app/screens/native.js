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

  // In the dark appearance the Canvas content in the hole is darkened with a
  // filter; some embeds (viewers, tool pickers) read badly that way, so the bar
  // offers to show the hole as Canvas drew it. The choice is kept per site.
  let lightPref = null; // null until read
  const html = document.documentElement;
  const applyLight = (on) => html.classList.toggle('bcv-punch-light', !!on);
  function lightButton(ctx) {
    if (!ctx.dark) return null;
    const btn = U.btn('', { icon: IC.sun, kind: 'xs', cls: 'bcv-native__light', title: 'Show this Canvas page in light mode' });
    const label = () => {
      const on = html.classList.contains('bcv-punch-light');
      btn.replaceChildren(U.svg(on ? IC.moon : IC.sun, { size: 14, stroke: 'currentColor', width: 1.8 }), on ? 'Back to dark' : 'View in light mode');
      btn.title = on ? 'Darken this Canvas page again' : 'Show this Canvas page as Canvas drew it, in light mode';
    };
    btn.addEventListener('click', async () => {
      const on = !html.classList.contains('bcv-punch-light');
      applyLight(on);
      lightPref = on;
      label();
      await BCV.store.setPref('punchLight', on);
    });
    if (lightPref === null) {
      BCV.store.pref('punchLight', false).then((v) => {
        lightPref = !!v;
        if (!ctx.alive()) return;
        applyLight(lightPref);
        label();
      });
    } else applyLight(lightPref);
    label();
    return btn;
  }

  /** Builds the native block: a note bar and the hole Canvas's page shows
   *  through. Canvas's own sub-navigation is not repeated here — the sidebar
   *  and the course rail already lead everywhere it led. */
  function block(ctx) {
    const app = ctx.app;
    const hasContent = !!(document.getElementById('content') || document.getElementById('main'));
    const note = U.el('bcv-native__bar', [
      U.text('bcv-native__note', 'This page is shown as Canvas drew it, inside the new look.'),
      h('span', { class: 'bcv-ml-auto' }),
      lightButton(ctx),
      // the same move as the switch at the top right: saved, or this page only (the popup's Persistent switch decides)
      U.btn('Open in stock Canvas', { icon: IC.external, kind: 'xs', cls: 'bcv-native__stock', onClick: async () => (BCV.early?.flipLook ? BCV.early.flipLook(false) : BCV.settings.update({ appearance: { skin: false } })) }),
    ]);
    const hole = U.el('bcv-native__hole', hasContent ? null : U.emptyCard('Canvas did not render anything for this page.'));
    if (hasContent) app.punchIn(hole);
    return U.el('bcv-native', [note, hole]);
  }

  async function render(ctx) {
    const screen = U.el('bcv-screen', null, { style: { '--w': '1180px' } });
    screen.append(
      U.el('bcv-head bcv-head--tight', U.el('bcv-head__in', h('h1', { class: 'bcv-h1 bcv-h1--30', text: titleFromPage() }))),
      U.el('bcv-body bcv-body--24', block(ctx)),
    );
    return screen;
  }

  BCV.screens.native = { render, block };
})();
