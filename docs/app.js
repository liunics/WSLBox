'use strict';
(() => {
  const messages = window.WSLBOX_I18N;
  const storageKey = 'wslbox.site.language';
  let language = 'en';
  let version = document.querySelector('#version').textContent.replace(/^.*?v/, '').trim();
  let releaseTag = null;
  let hasExecutable = false;
  let toastKey = null;
  let toastTimer;
  try { if (localStorage.getItem(storageKey) === 'zh-CN') language = 'zh-CN'; } catch (_) {}
  const text = key => messages[key]?.[language] || messages[key]?.en || key;
  function renderDynamic() {
    document.querySelector('#download').textContent = text(hasExecutable ? 'download.exe' : 'download.view');
    document.querySelector('#version').textContent = releaseTag || text('version.preview').replace('{version}', version);
    if (toastKey) document.querySelector('.toast').textContent = text(toastKey);
  }
  function setLanguage(value, remember = false) {
    language = value === 'zh-CN' ? 'zh-CN' : 'en';
    document.documentElement.lang = language;
    document.title = text('page.title');
    document.querySelector('meta[name="description"]').content = text('page.description');
    document.querySelectorAll('[data-i18n]').forEach(node => { node.innerHTML = text(node.dataset.i18n); });
    for (const attribute of ['alt', 'aria-label']) {
      document.querySelectorAll(`[data-i18n-${attribute}]`).forEach(node => {
        node.setAttribute(attribute, text(node.getAttribute(`data-i18n-${attribute}`)));
      });
    }
    document.querySelectorAll('[data-language]').forEach(button => {
      button.setAttribute('aria-pressed', String(button.dataset.language === language));
    });
    document.querySelector('.app-shot img').src = language === 'en' ? 'assets/window-en.png' : 'assets/window.png';
    renderDynamic();
    if (remember) { try { localStorage.setItem(storageKey, language); } catch (_) {} }
  }
  document.querySelectorAll('[data-language]').forEach(button => {
    button.addEventListener('click', () => setLanguage(button.dataset.language, true));
  });
  setLanguage(language);
  (async function () {
    let config = { repository: 'liunics/WSLBox', version: '1.5.0' };
    try { const result = await fetch('site-config.json'); if (result.ok) config = { ...config, ...await result.json() }; } catch (_) {}
    const repo = /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(config.repository) ? config.repository : 'liunics/WSLBox';
    const base = `https://github.com/${repo}`;
    document.querySelectorAll('.repo-link').forEach(a => { a.href = base; });
    document.querySelectorAll('.releases-link').forEach(a => { a.href = `${base}/releases`; });
    const download = document.querySelector('#download');
    download.href = `${base}/releases`;
    document.querySelector('#source').href = `${base}/archive/refs/heads/main.zip`;
    version = String(config.version);
    renderDynamic();
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 4000);
    try {
      const result = await fetch(`https://api.github.com/repos/${repo}/releases/latest`, { signal: controller.signal });
      if (result.ok) {
        const release = await result.json();
        const exe = (release.assets || []).find(a => /^WSLBox-v[\d.]+\.exe$/.test(a.name));
        if (exe) { download.href = exe.browser_download_url; hasExecutable = true; }
        else if (release.html_url) download.href = release.html_url;
        if (release.tag_name) {
          releaseTag = release.tag_name;
          document.querySelector('#source').href = `${base}/archive/refs/tags/${encodeURIComponent(release.tag_name)}.zip`;
        }
        renderDynamic();
      }
    } catch (_) {} finally { clearTimeout(timer); }
  })();
  document.querySelectorAll('[data-copy]').forEach(button => button.addEventListener('click', async () => {
    const value = document.getElementById(button.dataset.copy).textContent;
    try { await navigator.clipboard.writeText(value); toastKey = 'copy.success'; }
    catch (_) { toastKey = 'copy.fallback'; }
    const toast = document.querySelector('.toast');
    toast.textContent = text(toastKey);
    toast.classList.add('visible'); clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { toast.classList.remove('visible'); toastKey = null; }, 2500);
  }));
})();
