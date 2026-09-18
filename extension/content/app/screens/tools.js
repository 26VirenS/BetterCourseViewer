/* Tools: the page of tool cards (the "Tools" mockup). One card per tool, each opening its tool in
 * a popup over this page; a card dragged to the top of the page becomes a pin beside the look
 * switch (content/app/tools/tools.js has the tools, the popup and the drag). The first time the
 * page opens, the screen goes black and says what this is, then shows the drag — once. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const U = BCV.ui;
  const T = () => BCV.tools;

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const screen = U.el('bcv-screen');
    const grid = U.el('bcv-tools');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', [U.el('', [U.text('bcv-h1', 'Tools', 'h1'), U.text('bcv-head__sub', 'Things Simpl Courses does on its own.')])]))),
      U.el('bcv-body bcv-body--24', grid),
    );
    const welcome = T().welcomeIfFirst(app).catch(() => false); // the black goes up now, over the page drawing under it
    await T().pinsLoad().catch(() => {});
    if (!ctx.alive()) return screen;
    T().TOOLS.forEach((t, i) => {
      const card = T().cardEl(t, { dark });
      U.enter(card, i, 35, 300);
      grid.append(card);
    });
    void welcome;
    return screen;
  }

  BCV.screens.tools = { render };
})();
