/*
 * Jelly Crowd — "Admin" overlay (admin only). A self-designed admin area with internal tabs, hosted by
 * the plugin and opened from the admin-only navbar entry. Built incrementally; this first cut hosts the
 * Moderation and Ownership tools (more tabs — Requests, Reports, Quotas, Logs — are being moved here from
 * the Dashboard config page). The backend endpoints are admin-protected; the navbar entry is admin-only.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w92';
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';
  var activeTab = null;

  function shortLang() { return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US'); }
  function t(key) { return Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key; }

  function pluginUrl(path) {
    return (window.ApiClient && typeof window.ApiClient.getUrl === 'function') ? window.ApiClient.getUrl(path) : '/' + path;
  }

  function apiGet(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'GET', url: pluginUrl(path), dataType: 'json' });
    }
    return fetch(pluginUrl(path)).then(function (r) {
      if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
      return r.json();
    });
  }

  function apiPostNoResult(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path), contentType: 'application/json' });
    }
    return fetch(pluginUrl(path), { method: 'POST' }).then(function (r) {
      if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
    });
  }

  function loadConfigLang() {
    return apiGet('JellyCrowd/Settings/Language')
      .then(function (d) { if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); } })
      .catch(function () { /* keep 'auto' */ });
  }

  function loadStrings() {
    return fetch(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .catch(function () { return {}; })
      .then(function (loaded) { strings = loaded || {}; });
  }

  function setMessage(text) {
    var el = document.getElementById('jcAdminMessage');
    if (!el) { return; }
    if (text) { el.textContent = text; el.hidden = false; } else { el.hidden = true; }
  }

  // The admin tabs. `render(container)` fills the content area for that tab.
  var TABS = [
    { id: 'moderation', labelKey: 'nav_moderation', render: renderModeration },
    { id: 'ownership', labelKey: 'nav_ownership', render: renderOwnership }
  ];

  // ---------- Moderation ----------
  function renderModeration(container) {
    setMessage(t('loading'));
    apiGet('JellyCrowd/Comments/All')
      .then(function (reviews) {
        container.innerHTML = '';
        reviews = reviews || [];
        if (!reviews.length) { setMessage(t('moderation_empty')); return; }
        setMessage('');
        var sub = document.createElement('p');
        sub.className = 'jellycrowd-disclaimer';
        sub.textContent = t('moderation_subtitle');
        container.appendChild(sub);
        reviews.forEach(function (r) { container.appendChild(moderationRow(r, container)); });
      })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function titleLabel(item) {
    var typeLabel = (item.MediaType === 'tv' ? t('media_type_tv') : t('media_type_movie'));
    return item.Title ? (item.Title + ' (' + typeLabel + ')') : (typeLabel + ' #' + item.TmdbId);
  }

  function tmdbLink(item) {
    var a = document.createElement('a');
    a.href = 'https://www.themoviedb.org/' + item.MediaType + '/' + item.TmdbId;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.className = 'jellycrowd-link';
    a.textContent = titleLabel(item);
    return a;
  }

  function actionButton(label, handler, secondary) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'jellycrowd-request' + (secondary ? ' jellycrowd-request-secondary' : '');
    b.textContent = label;
    b.addEventListener('click', function () { handler(b); });
    return b;
  }

  function moderationRow(review, container) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-mod-row';

    var head = document.createElement('div');
    head.className = 'jellycrowd-mod-head';
    head.appendChild(tmdbLink(review));
    if (review.Rating > 0) {
      var rating = document.createElement('span');
      rating.className = 'jellycrowd-mod-rating';
      rating.textContent = '★ ' + review.Rating + '/10';
      head.appendChild(rating);
    }
    var author = document.createElement('span');
    author.className = 'jellycrowd-mod-author';
    author.textContent = review.UserName || '—';
    head.appendChild(author);
    var when = document.createElement('span');
    when.className = 'jellycrowd-mod-date';
    when.textContent = review.CreatedAt ? new Date(review.CreatedAt).toLocaleString() : '';
    head.appendChild(when);
    if (review.Hidden) {
      var badge = document.createElement('span');
      badge.className = 'jellycrowd-mod-hidden-badge';
      badge.textContent = t('moderation_hidden');
      head.appendChild(badge);
    }
    row.appendChild(head);

    if (review.Text) {
      var text = document.createElement('div');
      text.className = 'jellycrowd-mod-text';
      text.textContent = review.Text;
      row.appendChild(text);
    }

    var actions = document.createElement('div');
    actions.className = 'jellycrowd-mod-actions';
    actions.appendChild(actionButton(review.Hidden ? t('comment_show') : t('comment_hide'), function (btn) {
      btn.disabled = true;
      apiPostNoResult('JellyCrowd/Comments/' + review.Id + (review.Hidden ? '/Show' : '/Hide'))
        .then(function () { renderModeration(container); })
        .catch(function () { btn.disabled = false; });
    }, true));
    actions.appendChild(actionButton(t('comment_delete'), function (btn) {
      btn.disabled = true;
      apiPostNoResult('JellyCrowd/Comments/' + review.Id + '/Delete')
        .then(function () { renderModeration(container); })
        .catch(function () { btn.disabled = false; });
    }));
    row.appendChild(actions);
    return row;
  }

  // ---------- Ownership ----------
  function renderOwnership(container) {
    setMessage(t('loading'));
    apiGet('JellyCrowd/Requests/Ownerships')
      .then(function (items) {
        container.innerHTML = '';
        items = items || [];
        var sub = document.createElement('p');
        sub.className = 'jellycrowd-disclaimer';
        sub.textContent = t('ownership_subtitle');
        container.appendChild(sub);

        var filter = document.createElement('input');
        filter.type = 'search';
        filter.className = 'jellycrowd-search-input';
        filter.placeholder = t('ownership_filter_placeholder');
        container.appendChild(filter);

        var list = document.createElement('div');
        list.className = 'jellycrowd-list';
        container.appendChild(list);

        function paint() {
          var q = (filter.value || '').trim().toLowerCase();
          var shown = items.filter(function (it) {
            if (!q) { return true; }
            if ((it.Title || '').toLowerCase().indexOf(q) >= 0) { return true; }
            return (it.Owners || []).some(function (o) { return (o.Name || '').toLowerCase().indexOf(q) >= 0; });
          });
          list.innerHTML = '';
          if (!shown.length) { setMessage(q ? t('no_results') : t('ownership_empty')); return; }
          setMessage('');
          shown.forEach(function (it) { list.appendChild(ownershipRow(it)); });
        }
        filter.addEventListener('input', paint);
        paint();
      })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function ownershipRow(item) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-own-row';
    if (item.PosterPath) {
      var img = document.createElement('img');
      img.className = 'jellycrowd-own-poster';
      img.loading = 'lazy';
      img.alt = '';
      img.src = POSTER_BASE + item.PosterPath;
      row.appendChild(img);
    }
    var body = document.createElement('div');
    body.className = 'jellycrowd-own-body';
    var head = document.createElement('div');
    head.className = 'jellycrowd-own-head';
    var title = tmdbLink(item);
    title.classList.add('jellycrowd-own-title');
    head.appendChild(title);
    if (item.Season != null) {
      var scope = document.createElement('span');
      scope.className = 'jellycrowd-own-scope';
      scope.textContent = 'S' + item.Season + (item.Episode != null ? 'E' + item.Episode : '');
      head.appendChild(scope);
    }
    var count = document.createElement('span');
    count.className = 'jellycrowd-own-count';
    count.textContent = (item.Owners ? item.Owners.length : 0) + ' ' + t('ownership_owners');
    head.appendChild(count);
    body.appendChild(head);

    var owners = document.createElement('div');
    owners.className = 'jellycrowd-own-owners';
    (item.Owners || []).forEach(function (o) {
      var chip = document.createElement('span');
      chip.className = 'jellycrowd-own-owner';
      chip.textContent = o.Name;
      if (o.SinceUtc) { chip.title = t('ownership_since') + ' ' + new Date(o.SinceUtc).toLocaleDateString(); }
      owners.appendChild(chip);
    });
    body.appendChild(owners);
    row.appendChild(body);
    return row;
  }

  // ---------- shell ----------
  function activate(id) {
    activeTab = id;
    var bar = document.getElementById('jcAdminTabs');
    [].forEach.call(bar.querySelectorAll('.jellycrowd-admin-tab'), function (b) {
      b.classList.toggle('jellycrowd-admin-tab-active', b.getAttribute('data-tab') === id);
    });
    var content = document.getElementById('jcAdminContent');
    content.innerHTML = '';
    setMessage('');
    var tab = null;
    TABS.forEach(function (x) { if (x.id === id) { tab = x; } });
    if (tab) { tab.render(content); }
  }

  function buildTabBar() {
    var bar = document.getElementById('jcAdminTabs');
    bar.innerHTML = '';
    TABS.forEach(function (tab) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'jellycrowd-admin-tab';
      b.setAttribute('data-tab', tab.id);
      b.setAttribute('role', 'tab');
      b.textContent = t(tab.labelKey);
      b.addEventListener('click', function () { activate(tab.id); });
      bar.appendChild(b);
    });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      var logo = document.getElementById('jcAdminLogo');
      if (logo) { logo.src = pluginUrl('JellyCrowd/Web/logo.png'); }
      document.getElementById('jcAdminTitle').textContent = t('nav_admin');
      buildTabBar();
      activate(TABS[0].id);
      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('admin', function () { if (activeTab) { activate(activeTab); } });
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
