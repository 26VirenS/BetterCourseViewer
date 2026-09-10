/* Theme feature: classes and CSS are applied by early.js; this exposes
 * toggles used by the keyboard shortcuts, popup and command palette. */
(function () {
  const BCV = self.BCV;
  const S = BCV.settings;
  BCV.features = BCV.features || [];

  async function toggleDark() {
    const isDark = document.documentElement.classList.contains('bcv-dark');
    await S.update({ appearance: { darkMode: isDark ? 'off' : 'on' } });
    BCV.ui?.toast({ title: isDark ? 'Dark mode off' : 'Dark mode on', timeout: 1500 });
    return !isDark;
  }

  async function toggleMinimal() {
    const s = await S.get();
    const next = !s.appearance.minimal;
    await S.update({ appearance: { minimal: next } });
    BCV.ui?.toast({ title: next ? 'Minimal mode on' : 'Minimal mode off', timeout: 1500 });
    return next;
  }

  async function setTheme(theme) {
    await S.update({ appearance: { theme } });
  }

  BCV.theme = { toggleDark, toggleMinimal, setTheme };
  BCV.features.push({
    id: 'theme',
    init() {
      /* nothing to do at idle; early.js keeps classes + CSS in sync */
    },
  });
})();
