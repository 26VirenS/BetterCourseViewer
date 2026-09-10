/* Shared UI plumbing for content features: overlay root, toasts, side
 * panels (one open at a time), and injected global-nav items. */
(function () {
  const BCV = self.BCV;
  const { h, ICONS } = BCV.utils;

  let rootEl = null;
  function root() {
    if (rootEl && rootEl.isConnected) return rootEl;
    rootEl = document.getElementById('bcv-root') || h('div', { id: 'bcv-root', class: 'bcv-ui' });
    if (!rootEl.isConnected) (document.body || document.documentElement).append(rootEl);
    return rootEl;
  }

  // ---- toasts ---------------------------------------------------------------
  function toastHost() {
    let host = document.getElementById('bcv-toasts');
    if (!host) {
      host = h('div', { id: 'bcv-toasts', class: 'bcv-ui', role: 'status', 'aria-live': 'polite' });
      root().append(host);
    }
    return host;
  }

  function toast({ title, body, actions = [], timeout = 8000, kind = 'info', icon = null } = {}) {
    const el = h('div', { class: `bcv-toast bcv-toast--${kind}` });
    const close = () => {
      el.classList.add('is-leaving');
      setTimeout(() => el.remove(), 180);
    };
    const head = h('div', { class: 'bcv-toast__head' }, [
      icon ? h('span', { class: 'bcv-toast__icon', html: icon }) : null,
      h('div', { class: 'bcv-toast__title', text: title || '' }),
      h('button', { class: 'bcv-iconbtn', title: 'Dismiss', 'aria-label': 'Dismiss', html: ICONS.close, onClick: close }),
    ]);
    el.append(head);
    if (body) el.append(h('div', { class: 'bcv-toast__body', text: body }));
    if (actions.length) {
      el.append(h('div', { class: 'bcv-toast__actions' }, actions.map((a) =>
        h('button', {
          class: `bcv-btn bcv-btn--sm${a.primary ? ' bcv-btn--primary' : ''}`,
          text: a.label,
          onClick: async () => {
            try {
              await a.onClick?.();
            } finally {
              if (a.keepOpen !== true) close();
            }
          },
        }))));
    }
    toastHost().append(el);
    requestAnimationFrame(() => el.classList.add('is-visible'));
    if (timeout > 0) setTimeout(close, timeout);
    return { el, close };
  }

  // ---- panels ---------------------------------------------------------------
  const panels = new Map();
  function registerPanel(id, { el, onOpen, onClose }) {
    panels.set(id, { el, onOpen, onClose, open: false });
    el.classList.add('bcv-ui', 'bcv-panel');
    el.hidden = true;
    root().append(el);
  }
  function openPanel(id) {
    const p = panels.get(id);
    if (!p) return;
    for (const [otherId, other] of panels) if (otherId !== id && other.open) closePanel(otherId);
    p.open = true;
    p.el.hidden = false;
    requestAnimationFrame(() => p.el.classList.add('is-open'));
    document.documentElement.classList.add('bcv-panel-open');
    p.onOpen?.();
  }
  function closePanel(id) {
    const p = panels.get(id);
    if (!p || !p.open) return;
    p.open = false;
    p.el.classList.remove('is-open');
    if (p.el.contains(document.activeElement)) document.activeElement.blur();
    p.el.hidden = true;
    if (![...panels.values()].some((x) => x.open)) document.documentElement.classList.remove('bcv-panel-open');
    p.onClose?.();
  }
  const togglePanel = (id) => (panels.get(id)?.open ? closePanel(id) : openPanel(id));
  const isPanelOpen = (id) => !!panels.get(id)?.open;
  const closeAllPanels = () => {
    for (const id of panels.keys()) closePanel(id);
  };

  // ---- global nav items -----------------------------------------------------
  const navItems = new Map();
  function fabHost() {
    let host = document.getElementById('bcv-fab');
    if (!host) {
      host = h('div', { id: 'bcv-fab', class: 'bcv-ui' });
      root().append(host);
      toastHost().classList.add('has-fab');
    }
    return host;
  }

  function addNavItem({ id, label, icon, onClick, title }) {
    const menu = document.getElementById('menu');
    let el;
    const badge = h('span', { class: 'bcv-nav-badge', hidden: true });
    if (menu) {
      const btn = h('button', {
        type: 'button',
        class: 'ic-app-header__menu-list-link bcv-nav-link',
        title: title || label,
        'aria-label': label,
        onClick,
      }, [
        h('div', { class: 'menu-item-icon-container bcv-nav-icon', 'aria-hidden': 'true', html: icon }, [badge]),
        h('div', { class: 'menu-item__text', text: label }),
      ]);
      el = h('li', { class: 'menu-item ic-app-header__menu-list-item bcv-nav-item', id: `bcv-nav-${id}` }, [btn]);
      const help = menu.querySelector('#global_nav_help_link')?.closest('li');
      if (help) help.before(el);
      else menu.append(el);
    } else {
      el = h('button', { type: 'button', class: 'bcv-fab-btn', id: `bcv-nav-${id}`, title: title || label, 'aria-label': label, html: icon, onClick }, [badge]);
      fabHost().append(el);
    }
    navItems.set(id, { el, badge });
    return el;
  }

  function setNavBadge(id, count) {
    const item = navItems.get(id);
    if (!item) return;
    const n = Number(count) || 0;
    item.badge.textContent = n > 99 ? '99+' : String(n);
    item.badge.hidden = n <= 0;
  }

  // ---- small helpers --------------------------------------------------------
  function panelHeader(title, icon, actions = []) {
    return h('header', { class: 'bcv-panel__header' }, [
      h('div', { class: 'bcv-panel__title' }, [h('span', { class: 'bcv-panel__title-icon', html: icon }), h('span', { text: title })]),
      h('div', { class: 'bcv-panel__actions' }, actions),
    ]);
  }

  function iconButton(icon, title, onClick, extraClass = '') {
    return h('button', { type: 'button', class: `bcv-iconbtn ${extraClass}`.trim(), title, 'aria-label': title, html: icon, onClick });
  }

  BCV.ui = { root, toast, registerPanel, openPanel, closePanel, togglePanel, isPanelOpen, closeAllPanels, addNavItem, setNavBadge, panelHeader, iconButton };
})();
