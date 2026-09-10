/* Generates the <style> text that re-brands Canvas (accent, nav, links)
 * and defines the extension's own UI variables. Loaded at document_start
 * so early.js can inject a cached copy before first paint. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const LIGHT = {
    bg: '#ffffff', bg2: '#f4f5f7', bg3: '#e9ebef', fg: '#111827', fg2: '#6b7280', border: '#e2e5ea',
    danger: '#dc2626', warn: '#d97706', ok: '#16a34a', info: '#2563eb',
    shadow: '0 12px 32px rgba(0,0,0,.18), 0 1px 3px rgba(0,0,0,.08)',
    accentFg: '#ffffff',
  };
  const DARK = {
    bg: '#1e1f23', bg2: '#27282d', bg3: '#33353b', fg: '#e8eaee', fg2: '#9aa0aa', border: '#3b3d44',
    danger: '#f87171', warn: '#fbbf24', ok: '#4ade80', info: '#60a5fa',
    shadow: '0 12px 32px rgba(255,255,255,.22), 0 1px 3px rgba(255,255,255,.1)',
    accentFg: '#ffffff',
  };

  function build(settings, dark) {
    const S = BCV.settings;
    const C = BCV.color;
    const a = settings.appearance;
    const t = S.effectiveTheme(a);
    // Colours painted inside normal page content go through the dark filter,
    // so pre-transform them; the global nav is counter-filtered and renders as written.
    const page = (hex) => (dark ? C.preDarkFilter(hex) : hex);
    const lines = [];

    if (t.accent && C.parseHex(t.accent)) {
      const accent = t.accent;
      const link = t.link && C.parseHex(t.link) ? t.link : accent;
      const primaryText = C.isLight(accent) ? '#111827' : '#ffffff';
      const vars = {
        '--ic-brand-primary': accent,
        '--ic-brand-primary-lightened-5': C.lighten(accent, 0.05),
        '--ic-brand-primary-lightened-10': C.lighten(accent, 0.1),
        '--ic-brand-primary-lightened-15': C.lighten(accent, 0.15),
        '--ic-brand-primary-darkened-5': C.darken(accent, 0.05),
        '--ic-brand-primary-darkened-10': C.darken(accent, 0.1),
        '--ic-brand-primary-darkened-15': C.darken(accent, 0.15),
        '--ic-brand-button--primary-bgd': accent,
        '--ic-brand-button--primary-bgd-darkened-5': C.darken(accent, 0.05),
        '--ic-brand-button--primary-bgd-darkened-15': C.darken(accent, 0.15),
        '--ic-brand-button--primary-text': primaryText,
        '--ic-link-color': link,
        '--ic-link-color-darkened-10': C.darken(link, 0.1),
        '--ic-link-color-lightened-10': C.lighten(link, 0.1),
        '--ic-brand-msapplication-tile-color': accent,
      };
      for (const [k, v] of Object.entries(vars)) lines.push(`${k}: ${page(v)} !important;`);
    }
    if (t.nav && C.parseHex(t.nav)) {
      const nav = t.nav;
      const navText = t.navText && C.parseHex(t.navText) ? t.navText : (C.isLight(nav) ? '#111827' : '#ffffff');
      const active = t.accent && C.parseHex(t.accent) ? t.accent : nav;
      const activeText = C.isLight(active) ? '#111827' : '#ffffff';
      const vars = {
        '--ic-brand-global-nav-bgd': nav,
        '--ic-brand-global-nav-logo-bgd': C.darken(nav, 0.25),
        '--ic-brand-global-nav-ic-icon-svg-fill': navText,
        '--ic-brand-global-nav-menu-item__text-color': navText,
        '--ic-brand-global-nav-avatar-border': navText,
        '--ic-brand-global-nav-menu-item__badge-bgd': active,
        '--ic-brand-global-nav-menu-item__badge-text': activeText,
        '--ic-brand-global-nav-ic-icon-svg-fill--active': C.isLight(nav) ? C.darken(active, 0.1) : C.lighten(active, 0.35),
        '--ic-brand-global-nav-menu-item__text-color--active': C.isLight(nav) ? C.darken(active, 0.1) : C.lighten(active, 0.35),
      };
      for (const [k, v] of Object.entries(vars)) lines.push(`${k}: ${v} !important;`);
    }

    // Extension UI palette.
    const pal = dark ? DARK : LIGHT;
    const accent = (t.accent && C.parseHex(t.accent)) ? t.accent : (dark ? '#818cf8' : '#4f46e5');
    const ui = {
      '--bcv-bg': page(pal.bg),
      '--bcv-bg-2': page(pal.bg2),
      '--bcv-bg-3': page(pal.bg3),
      '--bcv-fg': page(pal.fg),
      '--bcv-fg-2': page(pal.fg2),
      '--bcv-border': page(pal.border),
      '--bcv-accent': page(accent),
      '--bcv-accent-soft': page(dark ? C.mix(accent, '#1e1f23', 0.75) : C.mix(accent, '#ffffff', 0.88)),
      '--bcv-accent-fg': page(C.isLight(accent) ? '#111827' : '#ffffff'),
      '--bcv-danger': page(pal.danger),
      '--bcv-danger-soft': page(dark ? '#3b1d1d' : '#fee2e2'),
      '--bcv-warn': page(pal.warn),
      '--bcv-warn-soft': page(dark ? '#3a2e12' : '#fef3c7'),
      '--bcv-ok': page(pal.ok),
      '--bcv-ok-soft': page(dark ? '#14321f' : '#dcfce7'),
      '--bcv-info': page(pal.info),
      '--bcv-shadow': pal.shadow,
      '--bcv-radius': '12px',
      '--bcv-sidebar-width': `${Math.max(320, Math.min(720, Number(settings.smart.sidebarWidth) || 400))}px`,
    };
    for (const [k, v] of Object.entries(ui)) lines.push(`${k}: ${v};`);

    return `html { ${lines.join(' ')} }`;
  }

  BCV.themeCss = { build, LIGHT, DARK };
})();
