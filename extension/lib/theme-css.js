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

  // Redesigned-interface palettes (Apple-like neutrals). Dark values are the
  // colours we want on screen; build() pre-transforms content colours so the
  // page's invert filter lands on them exactly.
  const SKIN_LIGHT = {
    pageBg: '#f5f5f7', card: '#ffffff', surface2: '#f2f2f7', separator: '#e5e5ea',
    text: '#1d1d1f', text2: '#6e6e73', text3: '#86868b', primary: '#0071e3', link: '#0066cc',
    success: '#1f8f3f', warning: '#c77700', danger: '#d70015',
    navBg: '#fbfbfd', navFg: '#3a3a3c', sideBg: '#f6f6f7',
    shadow: '0 1px 2px rgba(0,0,0,.04), 0 8px 24px rgba(0,0,0,.05)',
  };
  const SKIN_DARK = {
    pageBg: '#0f0f10', card: '#1c1c1e', surface2: '#2a2a2d', separator: '#323236',
    text: '#f5f5f7', text2: '#a1a1a6', text3: '#8e8e93', primary: '#0a84ff', link: '#2997ff',
    success: '#30d158', warning: '#ffd60a', danger: '#ff453a',
    navBg: '#161618', navFg: '#d1d1d6', sideBg: '#151517',
    shadow: '0 1px 2px rgba(255,255,255,.05), 0 8px 24px rgba(255,255,255,.06)',
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
    const skin = a.skin !== false;
    // Global nav colours render as written (the nav is counter-filtered in dark
    // mode), so these are final colours. A theme's nav colour wins; otherwise the
    // redesigned interface uses a light (or, in dark mode, graphite) sidebar.
    let nav = t.nav && C.parseHex(t.nav) ? t.nav : null;
    let navText = t.navText && C.parseHex(t.navText) ? t.navText : null;
    if (!nav && skin) {
      nav = dark ? SKIN_DARK.navBg : SKIN_LIGHT.navBg;
      navText = dark ? SKIN_DARK.navFg : SKIN_LIGHT.navFg;
    }
    if (nav) {
      const navLight = C.isLight(nav);
      navText = navText || (navLight ? '#111827' : '#ffffff');
      const accentFinal = t.accent && C.parseHex(t.accent) ? t.accent : (skin ? (dark ? SKIN_DARK.primary : SKIN_LIGHT.primary) : nav);
      const activeFg = navLight ? C.darken(accentFinal, 0.05) : C.lighten(accentFinal, 0.35);
      const vars = {
        '--ic-brand-global-nav-bgd': nav,
        '--ic-brand-global-nav-logo-bgd': navLight ? '#1d1d1f' : C.darken(nav, 0.25),
        '--ic-brand-global-nav-ic-icon-svg-fill': navText,
        '--ic-brand-global-nav-menu-item__text-color': navText,
        '--ic-brand-global-nav-avatar-border': navLight ? '#e5e5ea' : navText,
        '--ic-brand-global-nav-menu-item__badge-bgd': accentFinal,
        '--ic-brand-global-nav-menu-item__badge-text': C.isLight(accentFinal) ? '#111827' : '#ffffff',
        '--ic-brand-global-nav-ic-icon-svg-fill--active': activeFg,
        '--ic-brand-global-nav-menu-item__text-color--active': activeFg,
        '--bcv-nav-bg': nav,
        '--bcv-nav-fg': navText,
        '--bcv-nav-border': navLight ? C.darken(nav, 0.08) : C.lighten(nav, 0.08),
        '--bcv-nav-hover': navLight ? C.darken(nav, 0.05) : C.lighten(nav, 0.07),
        '--bcv-nav-active-bg': navLight ? C.mix(accentFinal, nav, 0.88) : C.mix(accentFinal, nav, 0.78),
        '--bcv-nav-active-fg': activeFg,
      };
      for (const [k, v] of Object.entries(vars)) lines.push(`${k}: ${v} !important;`);
    }

    // Redesigned interface palette (page content: pre-transformed in dark mode).
    if (skin) {
      const sk = dark ? SKIN_DARK : SKIN_LIGHT;
      const primary = t.accent && C.parseHex(t.accent) ? t.accent : sk.primary;
      const skinVars = {
        '--bcv-page-bg': sk.pageBg,
        '--bcv-card': sk.card,
        '--bcv-surface-2': sk.surface2,
        '--bcv-separator': sk.separator,
        '--bcv-text': sk.text,
        '--bcv-text-2': sk.text2,
        '--bcv-text-3': sk.text3,
        '--bcv-primary': primary,
        '--bcv-primary-fg': C.isLight(primary) ? '#111827' : '#ffffff',
        '--bcv-primary-soft': C.mix(primary, sk.card, dark ? 0.78 : 0.88),
        '--bcv-link': t.link && C.parseHex(t.link) ? t.link : (t.accent && C.parseHex(t.accent) ? t.accent : sk.link),
        '--bcv-success': sk.success,
        '--bcv-warning': sk.warning,
        '--bcv-danger-2': sk.danger,
      };
      for (const [k, v] of Object.entries(skinVars)) lines.push(`${k}: ${page(v)};`);
      lines.push(`--bcv-shadow-card: ${sk.shadow};`);
      // Shell (our own sidebar + top bar) lives inside the filtered page, so
      // its colours are pre-transformed like other content. A theme's nav
      // colour is honoured; otherwise a quiet neutral rail.
      const sideBg = t.nav && C.parseHex(t.nav) ? t.nav : sk.sideBg;
      const sideLight = C.isLight(sideBg);
      const sideFg = t.nav && C.parseHex(t.nav) ? (t.navText && C.parseHex(t.navText) ? t.navText : (sideLight ? '#1d1d1f' : '#f5f5f7')) : sk.text;
      const shellVars = {
        '--bcv-side-bg': sideBg,
        '--bcv-side-fg': sideFg,
        '--bcv-side-fg-2': C.mix(sideFg, sideBg, 0.45),
        '--bcv-side-border': sideLight ? C.darken(sideBg, 0.07) : C.lighten(sideBg, 0.09),
        '--bcv-side-hover': sideLight ? C.darken(sideBg, 0.045) : C.lighten(sideBg, 0.06),
        '--bcv-side-active-bg': sideLight ? '#ffffff' : C.lighten(sideBg, 0.12),
        '--bcv-side-active-fg': sideLight ? primary : C.lighten(primary, 0.3),
        '--bcv-top-bg': sk.card,
      };
      for (const [k, v] of Object.entries(shellVars)) lines.push(`${k}: ${page(v)};`);
    }

    // Extension UI palette.
    const pal = dark ? DARK : LIGHT;
    const accent = (t.accent && C.parseHex(t.accent)) ? t.accent : skin ? (dark ? SKIN_DARK.primary : SKIN_LIGHT.primary) : (dark ? '#818cf8' : '#4f46e5');
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

  BCV.themeCss = { build, LIGHT, DARK, SKIN_LIGHT, SKIN_DARK };
})();
