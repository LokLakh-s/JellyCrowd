/*
 * Jelly Crowd — releases calendar.
 * Lists upcoming movie & show releases (combined) grouped by date, newest releases last. Each card
 * opens the same details modal as the catalog (overview, seasons, per-title request, quota-aware).
 * Pure grouping lives in catalog.lib.js (groupByReleaseDate); strings follow the active language.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w342';
  var BACKDROP_BASE = 'https://image.tmdb.org/t/p/w780';
  var lib = window.JellyCrowdLib;
  var strings = {};
  var quotaExceeded = false;
  var cfgLang = 'auto';

  function fullLocale() { return lib.contentLocale(cfgLang, navigator.language || 'en-US'); }
  function shortLang() { return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US'); }
  var REGION = (fullLocale().split('-')[1] || 'US').toUpperCase();

  function t(key) {
    return Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;
  }

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

  function apiPost(path, body) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path), data: JSON.stringify(body), contentType: 'application/json', dataType: 'json' });
    }
    return fetch(pluginUrl(path), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).then(function (r) {
      if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
      return r.json();
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

  function navigateToItem(itemId) {
    var hash = '#/details?id=' + itemId;
    if (window.ApiClient && typeof window.ApiClient.serverId === 'function') {
      hash += '&serverId=' + window.ApiClient.serverId();
    }
    window.location.hash = hash;
  }

  function blockedRequestButton() {
    var button = document.createElement('button');
    button.className = 'jellycrowd-request jellycrowd-request-blocked';
    button.type = 'button';
    button.disabled = true;
    button.title = t('quota_full_hint');
    button.textContent = t('request_button');
    button.addEventListener('click', function (e) { e.stopPropagation(); });
    return button;
  }

  function requestItem(item, button, season, dateInput) {
    button.disabled = true;
    button.textContent = t('requesting');
    apiPost('JellyCrowd/Requests', {
      TmdbId: item.TmdbId,
      MediaType: item.MediaType,
      Title: item.Title,
      PosterPath: item.PosterPath,
      ReleaseDate: item.ReleaseDate,
      Season: (typeof season === 'number') ? season : null,
      DesiredAt: (dateInput && dateInput.value) ? dateInput.value : null
    }).then(function () {
      button.textContent = t('requested');
    }).catch(function (error) {
      if (error && error.status === 409) { button.textContent = t('already_requested'); }
      else if (error && error.status === 403) { button.textContent = t('quota_exceeded'); }
      else { button.disabled = false; button.textContent = t('request_button'); }
    });
  }

  function buildDesiredDateRow() {
    var wrap = document.createElement('div');
    wrap.className = 'jellycrowd-desired-date';
    var label = document.createElement('label');
    label.textContent = t('desired_date_label');
    var input = document.createElement('input');
    input.type = 'date';
    input.value = lib.isoDate(new Date());
    input.min = lib.isoDate(new Date());
    label.appendChild(input);
    wrap.appendChild(label);
    return { row: wrap, input: input };
  }

  function externalLink(href, text) {
    var a = document.createElement('a');
    a.href = href;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.textContent = text;
    return a;
  }

  function renderSeasonRequests(container, item, seasons, dateInput) {
    container.innerHTML = '';
    (seasons || []).forEach(function (season) {
      var row = document.createElement('div');
      row.className = 'jellycrowd-season-row';
      var label = document.createElement('span');
      label.textContent = season.Name + (season.EpisodeCount ? ' (' + season.EpisodeCount + ')' : '');
      row.appendChild(label);
      if (quotaExceeded) {
        row.appendChild(blockedRequestButton());
      } else {
        var btn = document.createElement('button');
        btn.className = 'jellycrowd-request';
        btn.type = 'button';
        btn.textContent = t('request_button');
        btn.addEventListener('click', function () { requestItem(item, btn, season.SeasonNumber, dateInput); });
        row.appendChild(btn);
      }
      container.appendChild(row);
    });
  }

  function openModal(item) {
    var overlay = document.createElement('div');
    overlay.className = 'jellycrowd-modal-overlay';
    var modal = document.createElement('div');
    modal.className = 'jellycrowd-modal';
    if (item.BackdropPath) { modal.style.backgroundImage = 'url("' + BACKDROP_BASE + item.BackdropPath + '")'; }

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'jellycrowd-modal-close';
    close.setAttribute('aria-label', t('close'));
    close.textContent = '✕';

    var body = document.createElement('div');
    body.className = 'jellycrowd-modal-body';

    if (item.PosterPath) {
      var poster = document.createElement('img');
      poster.className = 'jellycrowd-modal-poster';
      poster.loading = 'lazy';
      poster.alt = item.Title || '';
      poster.src = POSTER_BASE + item.PosterPath;
      body.appendChild(poster);
    }

    var content = document.createElement('div');
    content.className = 'jellycrowd-modal-content';
    var title = document.createElement('h2');
    title.className = 'jellycrowd-modal-title';
    title.textContent = lib.formatTitle(item);
    content.appendChild(title);

    var meta = document.createElement('div');
    meta.className = 'jellycrowd-modal-meta';
    var rating = lib.formatRating(item.VoteAverage);
    if (rating) { var rs = document.createElement('span'); rs.textContent = '★ ' + rating; meta.appendChild(rs); }
    if (item.Available) { var av = document.createElement('span'); av.textContent = t('available_badge'); meta.appendChild(av); }
    content.appendChild(meta);

    var genresEl = document.createElement('div');
    genresEl.className = 'jellycrowd-modal-genres';
    content.appendChild(genresEl);

    var overview = document.createElement('p');
    overview.className = 'jellycrowd-modal-overview';
    overview.textContent = item.Overview || t('no_overview');
    content.appendChild(overview);

    var links = document.createElement('div');
    links.className = 'jellycrowd-modal-links';
    links.appendChild(externalLink('https://www.themoviedb.org/' + item.MediaType + '/' + item.TmdbId, t('view_tmdb')));
    content.appendChild(links);

    if (!item.Available) {
      var dateInput = null;
      if (!quotaExceeded) {
        var dateRow = buildDesiredDateRow();
        dateInput = dateRow.input;
        content.appendChild(dateRow.row);
      }

      if (item.MediaType === 'tv') {
        var seasonsEl = document.createElement('div');
        seasonsEl.className = 'jellycrowd-seasons';
        content.appendChild(seasonsEl);
        apiGet('JellyCrowd/Catalog/Seasons/' + item.TmdbId + '?language=' + encodeURIComponent(fullLocale()))
          .then(function (seasons) { renderSeasonRequests(seasonsEl, item, seasons, dateInput); })
          .catch(function () { /* best-effort */ });
      } else if (quotaExceeded) {
        content.appendChild(blockedRequestButton());
      } else {
        var requestButton = document.createElement('button');
        requestButton.className = 'jellycrowd-request';
        requestButton.type = 'button';
        requestButton.textContent = t('request_button');
        requestButton.addEventListener('click', function () { requestItem(item, requestButton, null, dateInput); });
        content.appendChild(requestButton);
      }
    }

    body.appendChild(content);

    apiGet('JellyCrowd/Catalog/Details/' + item.MediaType + '/' + item.TmdbId + '?language=' + encodeURIComponent(fullLocale()))
      .then(function (details) {
        if (!details) { return; }
        (details.Genres || []).forEach(function (name) {
          var chip = document.createElement('span');
          chip.className = 'jellycrowd-chip';
          chip.textContent = name;
          genresEl.appendChild(chip);
        });
        if (details.Runtime) { var rt = document.createElement('span'); rt.textContent = details.Runtime + ' ' + t('runtime_min'); meta.appendChild(rt); }
        if (details.ImdbId) { links.appendChild(externalLink('https://www.imdb.com/title/' + details.ImdbId, t('view_imdb'))); }
      })
      .catch(function () { /* best-effort */ });

    modal.appendChild(close);
    modal.appendChild(body);
    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    function dismiss() { overlay.remove(); document.removeEventListener('keydown', onKey); }
    function onKey(e) { if (e.key === 'Escape') { dismiss(); } }
    close.addEventListener('click', dismiss);
    overlay.addEventListener('click', function (e) { if (e.target === overlay) { dismiss(); } });
    document.addEventListener('keydown', onKey);
  }

  function renderCard(item) {
    var card = document.createElement('div');
    card.className = 'jellycrowd-card jellycrowd-card-clickable';
    card.addEventListener('click', function () {
      if (item.Available && item.JellyfinItemId) { navigateToItem(item.JellyfinItemId); } else { openModal(item); }
    });

    var posterWrap = document.createElement('div');
    posterWrap.className = 'jellycrowd-poster-wrap';
    if (item.PosterPath) {
      var img = document.createElement('img');
      img.className = 'jellycrowd-poster';
      img.loading = 'lazy';
      img.alt = item.Title || '';
      img.src = POSTER_BASE + item.PosterPath;
      posterWrap.appendChild(img);
    } else {
      var placeholder = document.createElement('div');
      placeholder.className = 'jellycrowd-poster jellycrowd-poster-empty';
      posterWrap.appendChild(placeholder);
    }

    if (item.Available) {
      var badge = document.createElement('span');
      badge.className = 'jellycrowd-badge';
      badge.textContent = t('available_badge');
      posterWrap.appendChild(badge);
    }

    var hover = document.createElement('div');
    hover.className = 'jellycrowd-hover';
    var hoverTitle = document.createElement('div');
    hoverTitle.className = 'jellycrowd-hover-title';
    hoverTitle.textContent = item.Title || '';
    var hoverMeta = document.createElement('div');
    hoverMeta.className = 'jellycrowd-hover-meta';
    hoverMeta.textContent = item.MediaType === 'tv' ? t('type_shows') : t('type_movies');
    hover.appendChild(hoverTitle);
    hover.appendChild(hoverMeta);
    posterWrap.appendChild(hover);

    card.appendChild(posterWrap);

    if (!item.Available) {
      var button = document.createElement('button');
      button.className = 'jellycrowd-request';
      button.type = 'button';
      button.textContent = t('details_button');
      button.addEventListener('click', function (e) { e.stopPropagation(); openModal(item); });
      card.appendChild(button);
    }

    return card;
  }

  function setMessage(text) {
    var el = document.getElementById('jcCalMessage');
    if (text) { el.textContent = text; el.hidden = false; } else { el.hidden = true; }
  }

  function formatDate(isoDate) {
    var d = new Date(isoDate + 'T00:00:00');
    return isNaN(d.getTime()) ? isoDate : d.toLocaleDateString();
  }

  function render(groups) {
    var feed = document.getElementById('jcCalFeed');
    feed.innerHTML = '';
    if (!groups || groups.length === 0) {
      setMessage(t('no_results'));
      return;
    }
    setMessage('');
    groups.forEach(function (group) {
      var section = document.createElement('div');
      section.className = 'jellycrowd-row';
      var title = document.createElement('h3');
      title.className = 'jellycrowd-row-title';
      title.textContent = formatDate(group.date);
      section.appendChild(title);
      var grid = document.createElement('div');
      grid.className = 'jellycrowd-grid';
      group.items.forEach(function (item) { grid.appendChild(renderCard(item)); });
      section.appendChild(grid);
      feed.appendChild(section);
    });
  }

  function load() {
    setMessage(t('loading'));
    apiGet('JellyCrowd/Quota/Me')
      .then(function (q) { quotaExceeded = !!(q && !q.Unlimited && q.QuotaBytes > 0 && q.UsedBytes >= q.QuotaBytes); })
      .catch(function () { /* best-effort */ })
      .then(function () { return apiGet('JellyCrowd/Catalog/Calendar?language=' + encodeURIComponent(fullLocale()) + '&region=' + encodeURIComponent(REGION)); })
      .then(function (items) { render(lib.groupByReleaseDate(items)); })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      document.getElementById('jcCalLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
      document.getElementById('jcCalTitle').textContent = t('calendar_title');
      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('calendar', load);
      }
      load();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
