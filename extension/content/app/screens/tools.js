/* Tools: the page of tool cards (the "Tools" mockup). One card per tool, each opening its tool in
 * a popup over this page — (2.99.79) in sections by what they are for, as the Mac's page has them
 * (Study, Math & science, Files, Chemistry, then your own widgets), and a pin at each card's corner
 * that keeps the tool beside the look switch (content/app/tools/tools.js has the tools, the popup
 * and the pins). The guided tour (welcome.js) brings the student here and asks for a pin. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const U = BCV.ui;
  const { h } = BCV.utils;
  const T = () => BCV.tools;

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const screen = U.el('bcv-screen');
    const sections = U.el('bcv-tools-page');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', [U.el('', [U.text('bcv-h1', 'Tools', 'h1'), U.text('bcv-head__sub', 'Pin a tool to keep it beside the switch on every page.')])]))),
      U.el('bcv-body bcv-body--24', sections),
    );
    await T().pinsLoad().catch(() => {});
    // widgets of your own (content/app/tools/widgets.js): their cards in a section after the built-in tools, and the card that adds one
    try { if (!BCV.widgets && BCV.lazy?.load) await BCV.lazy.load('widgets'); await BCV.widgets?.all?.(); } catch { /* the built-in tools alone */ }
    if (!ctx.alive()) return screen;
    let i = 0;
    const section = (key, title, cards) => {
      const grid = U.el('bcv-tools', cards.map((c) => U.enter(c, i++, 35, 300)));
      return h('section', { class: 'bcv-tools-sec', dataset: { group: key } }, [U.text('bcv-tools-sec__h', title, 'h2'), grid]);
    };
    for (const [key, title] of T().GROUPS) {
      const list = T().toolsIn(key);
      if (list.length) sections.append(section(key, title, list.map((t) => T().cardEl(t, { dark }))));
    }
    const own = T().TOOLS.filter((t) => !t.group);
    const ownCards = own.map((t) => T().cardEl(t, { dark }));
    if (BCV.widgets) ownCards.push(BCV.widgets.addCard(app, { dark }));
    if (ownCards.length) sections.append(section('widgets', 'Your widgets', ownCards));
    return screen;
  }

  BCV.screens.tools = { render };
})();
