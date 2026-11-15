'use strict';
(() => {
  const root = document.documentElement;
  const system = matchMedia('(prefers-color-scheme: dark)');
  const storageKey = 'wslbox.site.theme';
  let preference;
  try { preference = localStorage.getItem(storageKey); } catch (_) {}

  function applyTheme() {
    const theme = preference === 'light' || preference === 'dark'
      ? preference : system.matches ? 'dark' : 'light';
    root.dataset.theme = theme;
    document.querySelector('meta[name="theme-color"]').content = theme === 'dark' ? '#0a1420' : '#f5f8fc';
    const button = document.getElementById('theme-toggle');
    if (button) {
      const key = theme === 'dark' ? 'theme.light' : 'theme.dark';
      button.setAttribute('data-i18n-aria-label', key);
      const labels = window.WSLBOX_I18N?.[key];
      if (labels) button.setAttribute('aria-label', labels[root.lang] || labels.en);
    }
  }

  applyTheme();
  system.addEventListener('change', applyTheme);
  document.addEventListener('DOMContentLoaded', () => {
    applyTheme();
    document.getElementById('theme-toggle').addEventListener('click', () => {
      preference = root.dataset.theme === 'dark' ? 'light' : 'dark';
      try { localStorage.setItem(storageKey, preference); } catch (_) {}
      applyTheme();
    });
  });
})();
