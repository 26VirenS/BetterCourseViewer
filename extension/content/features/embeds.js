/* "Open in new tab" buttons for embedded content: LTI tool launches,
 * file previews, and other iframes inside Canvas pages. */
(function () {
  const BCV = self.BCV;
  const { h, $$, observe, ICONS } = BCV.utils;
  BCV.features = BCV.features || [];

  const SELECTOR = [
    'iframe#tool_content',
    'iframe.tool_launch',
    'iframe[src*="external_tools"]',
    'iframe[src*="/files/"]',
    'iframe[src*="file_preview"]',
    'iframe[src*="youtube"]',
    'iframe[src*="youtu.be"]',
    'iframe[src*="vimeo"]',
    'iframe[src*="docs.google"]',
    'iframe[src*="drive.google"]',
    'iframe[src*="office.com"]',
    'iframe[src*="onedrive"]',
    'iframe[src*="kaltura"]',
    'iframe[src*="panopto"]',
    'iframe[src*="zoom"]',
    'iframe[src*="instructuremedia"]',
    '#content iframe[src^="http"]',
    '.user_content iframe',
    'iframe[name][src=""]',
    'iframe[name][src="about:blank"]',
    'iframe[name]:not([src])',
    '#content iframe[name]',
  ].join(', ');

  function launchFormFor(iframe) {
    const name = iframe.getAttribute('name');
    if (!name) return null;
    try {
      return document.querySelector(`form[target="${CSS.escape(name)}"]`);
    } catch {
      return null;
    }
  }

  function targetFor(iframe) {
    const src = iframe.getAttribute('src') || '';
    if (src && src !== 'about:blank' && !src.startsWith('javascript:')) {
      try {
        return { url: new URL(src, location.href).href, form: null };
      } catch {
        return null;
      }
    }
    const form = launchFormFor(iframe);
    return form ? { url: null, form } : null;
  }

  function open(target) {
    if (target.url) {
      window.open(target.url, '_blank', 'noopener');
      return;
    }
    if (target.form) {
      // LTI 1.3 launches POST a form into the iframe; re-submit it to a new tab.
      const clone = target.form.cloneNode(true);
      clone.target = '_blank';
      clone.style.display = 'none';
      document.body.append(clone);
      clone.submit();
      setTimeout(() => clone.remove(), 1000);
    }
  }

  function isBig(iframe) {
    const r = iframe.getBoundingClientRect();
    return r.width >= 200 && r.height >= 120;
  }

  function decorate(iframe) {
    if (iframe.dataset.bcvEmbed) return;
    if (iframe.closest('.bcv-ui, .tox, .mce-container')) return;
    if (!isBig(iframe) && !iframe.matches('#tool_content, .tool_launch')) return;
    const target = targetFor(iframe);
    if (!target) return;
    iframe.dataset.bcvEmbed = '1';
    const btn = h('button', {
      type: 'button',
      class: 'bcv-ui bcv-embed-btn',
      title: 'Open this embedded content in a new tab',
      html: `${ICONS.external}<span>Open in new tab</span>`,
      onClick: (e) => {
        e.preventDefault();
        e.stopPropagation();
        open(targetFor(iframe) || target);
      },
    });
    const parent = iframe.parentElement;
    if (!parent) return;
    const style = getComputedStyle(parent);
    const parentIsWrapper = parent.children.length === 1;
    if (parentIsWrapper) {
      if (style.position === 'static') parent.classList.add('bcv-embed-wrap');
      parent.append(btn);
    } else {
      const bar = h('div', { class: 'bcv-ui bcv-embed-toolbar' }, [btn]);
      iframe.before(bar);
    }
  }

  function scan() {
    for (const iframe of $$(SELECTOR)) decorate(iframe);
  }

  BCV.features.push({
    id: 'embeds',
    init(ctx) {
      if (!ctx.settings.embeds.openInNewTabButton) return;
      scan();
      setTimeout(scan, 1500);
      setTimeout(scan, 4000);
      observe(scan, { debounceMs: 400 });
    },
  });
})();
