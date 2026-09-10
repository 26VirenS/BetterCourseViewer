/* Declutter feature: page-kind classes for scoped CSS rules, plus a few
 * dynamic tweaks that CSS alone cannot express. */
(function () {
  const BCV = self.BCV;
  const { onUrlChange } = BCV.utils;
  BCV.features = BCV.features || [];

  function applyPageClass() {
    const page = BCV.pageContext.refresh();
    const html = document.documentElement;
    for (const c of Array.from(html.classList)) if (c.startsWith('bcv-page-')) html.classList.remove(c);
    html.classList.add(`bcv-page-${page.kind}`);
  }

  function tidyDashboard() {
    // Canvas keeps a "with-right-side" body class that reserves room for the
    // sidebar; drop it while the sidebar is hidden so cards use the full width.
    const hide = document.documentElement.classList.contains('bcv-hide-sidebar');
    const page = BCV.page;
    if (hide && (page.kind === 'dashboard' || page.kind === 'course-home')) {
      document.body.classList.add('bcv-no-right-side');
    } else {
      document.body.classList.remove('bcv-no-right-side');
    }
  }

  BCV.features.push({
    id: 'declutter',
    init() {
      applyPageClass();
      tidyDashboard();
      onUrlChange(() => {
        applyPageClass();
        tidyDashboard();
      });
    },
    onSettings() {
      tidyDashboard();
    },
  });
})();
