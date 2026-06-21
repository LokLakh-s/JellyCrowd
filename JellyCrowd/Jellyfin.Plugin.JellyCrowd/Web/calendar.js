/*
 * Jelly Crowd — releases calendar (monthly grid).
 * Shows a month grid (Monday-first) with movie & show releases in each day's cell; clicking a title
 * opens the same details modal as the catalog (overview, seasons, per-title request, quota-aware).
 * Pure layout helpers live in catalog.lib.js (buildMonthMatrix); strings follow the active language.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w342';
  var THUMB_BASE = 'https://image.tmdb.org/t/p/w92';
  var BACKDROP_BASE = 'https://image.tmdb.org/t/p/w780';
  var lib = window.JellyCrowdLib;
  var strings = {};
  var quotaExceeded = false;
  var cfgLang = 'auto';

  // Admin-only "request on behalf of" (see catalog.js).
  var isAdmin = false;
  var adminUsers = [];
  var actAsUserId = null;

  // Watchlist membership set ("type:tmdbId") for the modal star (see catalog.js).
  var watchlistKeys = {};

  // Language / production-country filters (see catalog.js).
  var FILTER_LANGUAGES = ['en', 'fr', 'es', 'it', 'de', 'pt', 'ja', 'ko', 'zh', 'hi'];
  var FILTER_COUNTRIES = ['US', 'FR', 'ES', 'IT', 'GB', 'DE', 'JP', 'KR', 'CA', 'IN'];
  var filterLanguage = '';
  var filterCountry = '';

  var now = new Date();
  var viewMode = 'month';                 // 'month' | 'week' | 'day'
  var anchor = new Date(now.getFullYear(), now.getMonth(), now.getDate()); // reference date for the view
  // Month view still keys off the anchor's year/month.
  function viewYearOf() { return anchor.getFullYear(); }
  function viewMonthOf() { return anchor.getMonth(); }

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

  // Ensure dropdown options are readable (default <option> rendering can be white-on-white on dark themes).
  function styleOption(opt) { opt.style.backgroundColor = '#1c1c1c'; opt.style.color = '#fff'; }

  function fillCodeSelect(select, codes, type, allLabel, current) {
    select.innerHTML = '';
    select.style.color = '#fff';
    select.style.backgroundColor = 'rgba(0,0,0,.35)';
    var allOpt = document.createElement('option');
    allOpt.value = '';
    allOpt.textContent = allLabel;
    styleOption(allOpt);
    select.appendChild(allOpt);
    var names = null;
    try { names = new Intl.DisplayNames([fullLocale()], { type: type }); } catch (e) { names = null; }
    codes.forEach(function (code) {
      var opt = document.createElement('option');
      opt.value = code;
      var label = code;
      try { if (names) { label = names.of(code) || code; } } catch (e) { label = code; }
      opt.textContent = label;
      styleOption(opt);
      select.appendChild(opt);
    });
    select.value = current || '';
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

  function loadAdmin() {
    if (!(window.ApiClient && typeof window.ApiClient.getCurrentUser === 'function')) {
      return Promise.resolve();
    }
    return window.ApiClient.getCurrentUser().then(function (user) {
      isAdmin = !!(user && user.Policy && user.Policy.IsAdministrator);
      if (isAdmin && typeof window.ApiClient.getUsers === 'function') {
        return window.ApiClient.getUsers().then(function (users) { adminUsers = users || []; }).catch(function () { /* ignore */ });
      }
      return null;
    }).catch(function () { /* ignore */ });
  }

  function submitRequest(payload) {
    if (actAsUserId) {
      var forUser = {};
      Object.keys(payload).forEach(function (k) { forUser[k] = payload[k]; });
      forUser.UserId = actAsUserId;
      return apiPost('JellyCrowd/Requests/ForUser', forUser);
    }
    return apiPost('JellyCrowd/Requests', payload);
  }

  function apiPostNoResult(path, body) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path), data: JSON.stringify(body), contentType: 'application/json' });
    }
    return fetch(pluginUrl(path), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      .then(function (r) { if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; } });
  }

  function loadWatchlist() {
    return apiGet('JellyCrowd/Watchlist').then(function (list) {
      watchlistKeys = {};
      (list || []).forEach(function (e) { watchlistKeys[e.MediaType + ':' + e.TmdbId] = true; });
    }).catch(function () { /* best-effort */ });
  }

  function watchlistStar(item) {
    var key = item.MediaType + ':' + item.TmdbId;
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'jellycrowd-star-inline';
    function refresh() {
      var on = !!watchlistKeys[key];
      btn.classList.toggle('jellycrowd-star-on', on);
      var label = on ? t('watchlist_remove') : t('watchlist_add');
      btn.textContent = (on ? '★ ' : '☆ ') + label;
      btn.title = label;
    }
    refresh();
    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      btn.disabled = true;
      var on = !!watchlistKeys[key];
      var body = { TmdbId: item.TmdbId, MediaType: item.MediaType, Title: item.Title, PosterPath: item.PosterPath, ReleaseDate: item.ReleaseDate };
      apiPostNoResult(on ? 'JellyCrowd/Watchlist/Remove' : 'JellyCrowd/Watchlist', body)
        .then(function () { if (on) { delete watchlistKeys[key]; } else { watchlistKeys[key] = true; } refresh(); })
        .catch(function () { /* ignore */ })
        .then(function () { btn.disabled = false; });
    });
    return btn;
  }

  function requestItem(item, button, season, dateInput, episode, releaseDate) {
    button.disabled = true;
    button.textContent = t('requesting');
    submitRequest({
      TmdbId: item.TmdbId,
      MediaType: item.MediaType,
      Title: item.Title,
      PosterPath: item.PosterPath,
      ReleaseDate: releaseDate || item.ReleaseDate,
      Season: (typeof season === 'number') ? season : null,
      Episode: (typeof episode === 'number') ? episode : null,
      DesiredAt: (dateInput && dateInput.value) ? dateInput.value : null
    }).then(function () {
      button.textContent = t('requested');
    }).catch(function (error) {
      if (error && error.status === 409) { button.textContent = t('already_requested'); }
      else if (error && error.status === 403) { button.textContent = t('requests_disabled'); }
      else { button.disabled = false; button.textContent = t('request_button'); }
    });
  }

  // POST a single-episode request (fire-and-forget), scheduled at the episode's air date.
  function postEpisode(item, seasonNumber, ep) {
    return submitRequest({
      TmdbId: item.TmdbId,
      MediaType: item.MediaType,
      Title: item.Title,
      PosterPath: item.PosterPath,
      ReleaseDate: ep.AirDate || item.ReleaseDate,
      Season: seasonNumber,
      Episode: ep.EpisodeNumber,
      DesiredAt: null
    });
  }

  function requestSeason(item, season, button, dateInput) {
    button.disabled = true;
    button.textContent = t('requesting');
    apiGet('JellyCrowd/Catalog/Episodes/' + item.TmdbId + '/' + season.SeasonNumber + '?language=' + encodeURIComponent(fullLocale()))
      .then(function (episodes) {
        var hasFuture = (episodes || []).some(function (e) {
          return e.AirDate && new Date(e.AirDate + 'T00:00:00').getTime() > Date.now();
        });
        if (episodes && episodes.length && hasFuture) {
          return Promise.all(episodes.map(function (e) { return postEpisode(item, season.SeasonNumber, e).catch(function () { /* skip dups */ }); }))
            .then(function () { button.textContent = t('requested'); });
        }
        requestItem(item, button, season.SeasonNumber, dateInput);
        return null;
      })
      .catch(function () { requestItem(item, button, season.SeasonNumber, dateInput); });
  }

  // A disabled "already requested" button, for seasons/episodes the user has an active request for.
  function alreadyRequestedButton() {
    var b = document.createElement('button');
    b.className = 'jellycrowd-request';
    b.type = 'button';
    b.textContent = t('already_requested');
    b.disabled = true;
    return b;
  }

  // Fetch the user's already-requested seasons/episodes for a title (best-effort).
  function loadRequestedKeys(tmdbId) {
    return apiGet('JellyCrowd/Requests/Mine')
      .then(function (reqs) { return lib.requestedKeys(reqs, tmdbId); })
      .catch(function () { return { seasons: {}, episodes: {} }; });
  }

  function loadEpisodes(item, season, container, requested) {
    var req = requested || { seasons: {}, episodes: {} };
    container.textContent = t('loading');
    apiGet('JellyCrowd/Catalog/Episodes/' + item.TmdbId + '/' + season.SeasonNumber + '?language=' + encodeURIComponent(fullLocale()))
      .then(function (episodes) {
        container.innerHTML = '';
        if (!episodes || episodes.length === 0) {
          container.textContent = t('no_results');
          return;
        }
        episodes.forEach(function (ep) {
          var row = document.createElement('div');
          row.className = 'jellycrowd-episode-row';
          var label = document.createElement('span');
          label.textContent = 'E' + ep.EpisodeNumber + ' · ' + (ep.Name || '') + (ep.AirDate ? ' (' + ep.AirDate + ')' : '');
          row.appendChild(label);
          if (req.seasons[season.SeasonNumber] || req.episodes[season.SeasonNumber + ':' + ep.EpisodeNumber]) {
            row.appendChild(alreadyRequestedButton());
          } else if (quotaExceeded) {
            row.appendChild(blockedRequestButton());
          } else {
            var btn = document.createElement('button');
            btn.className = 'jellycrowd-request';
            btn.type = 'button';
            btn.textContent = t('request_button');
            btn.addEventListener('click', function () { requestItem(item, btn, season.SeasonNumber, null, ep.EpisodeNumber, ep.AirDate); });
            row.appendChild(btn);
          }
          container.appendChild(row);
        });
      })
      .catch(function () { container.textContent = t('error_generic'); });
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

  function renderSeasonRequests(container, item, seasons, dateInput, requested) {
    var req = requested || { seasons: {}, episodes: {} };
    container.innerHTML = '';
    (seasons || []).forEach(function (season) {
      var row = document.createElement('div');
      row.className = 'jellycrowd-season-row';
      var label = document.createElement('span');
      label.textContent = season.Name + (season.EpisodeCount ? ' (' + season.EpisodeCount + ')' : '');
      row.appendChild(label);

      var actions = document.createElement('span');
      actions.className = 'jellycrowd-season-actions';
      var episodesBox = document.createElement('div');
      episodesBox.className = 'jellycrowd-episodes';
      episodesBox.style.display = 'none';
      var loaded = false;
      var toggle = document.createElement('button');
      toggle.type = 'button';
      toggle.className = 'jellycrowd-ep-toggle';
      toggle.textContent = t('episodes');
      toggle.addEventListener('click', function () {
        episodesBox.style.display = episodesBox.style.display === 'none' ? '' : 'none';
        if (!loaded) { loaded = true; loadEpisodes(item, season, episodesBox, req); }
      });
      actions.appendChild(toggle);

      if (req.seasons[season.SeasonNumber]) {
        actions.appendChild(alreadyRequestedButton());
      } else if (quotaExceeded) {
        actions.appendChild(blockedRequestButton());
      } else {
        var btn = document.createElement('button');
        btn.className = 'jellycrowd-request';
        btn.type = 'button';
        btn.textContent = t('request_season');
        btn.addEventListener('click', function () { requestSeason(item, season, btn, dateInput); });
        actions.appendChild(btn);
      }

      row.appendChild(actions);
      container.appendChild(row);
      container.appendChild(episodesBox);
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
    meta.appendChild(watchlistStar(item));

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

    actAsUserId = null;
    if (isAdmin) {
      var adminRow = document.createElement('div');
      adminRow.className = 'jellycrowd-admin-actas';
      var adminLabel = document.createElement('span');
      adminLabel.textContent = t('act_as');
      var adminSelect = document.createElement('select');
      var selfOption = document.createElement('option');
      selfOption.value = '';
      selfOption.textContent = t('act_as_self');
      adminSelect.appendChild(selfOption);
      adminUsers.forEach(function (u) {
        var opt = document.createElement('option');
        opt.value = u.Id;
        opt.textContent = u.Name;
        adminSelect.appendChild(opt);
      });
      adminSelect.addEventListener('change', function () { actAsUserId = adminSelect.value || null; });
      adminRow.appendChild(adminLabel);
      adminRow.appendChild(adminSelect);
      content.appendChild(adminRow);
    }

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
        Promise.all([
          apiGet('JellyCrowd/Catalog/Seasons/' + item.TmdbId + '?language=' + encodeURIComponent(fullLocale())),
          loadRequestedKeys(item.TmdbId)
        ])
          .then(function (res) { renderSeasonRequests(seasonsEl, item, res[0], dateInput, res[1]); })
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

  // ---------- month grid ----------

  function setMessage(text) {
    var el = document.getElementById('jcCalMessage');
    if (text) { el.textContent = text; el.hidden = false; } else { el.hidden = true; }
  }

  function pad2(n) { return n < 10 ? '0' + n : String(n); }
  function iso(d) { return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate()); }

  // Monday of the week containing d.
  function weekStart(d) {
    var x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
    var dow = (x.getDay() + 6) % 7; // 0 = Monday
    x.setDate(x.getDate() - dow);
    return x;
  }

  // The list of dates the current week/day view spans.
  function currentDays() {
    if (viewMode === 'day') { return [new Date(anchor.getFullYear(), anchor.getMonth(), anchor.getDate())]; }
    var ws = weekStart(anchor);
    var arr = [];
    for (var i = 0; i < 7; i++) { arr.push(new Date(ws.getFullYear(), ws.getMonth(), ws.getDate() + i)); }
    return arr;
  }

  function periodLabel() {
    try {
      if (viewMode === 'day') {
        return new Intl.DateTimeFormat(fullLocale(), { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(anchor);
      }
      if (viewMode === 'week') {
        var days = currentDays();
        var f = new Intl.DateTimeFormat(fullLocale(), { day: 'numeric', month: 'short' });
        return f.format(days[0]) + ' – ' + f.format(days[6]);
      }
      return new Intl.DateTimeFormat(fullLocale(), { month: 'long', year: 'numeric' }).format(new Date(viewYearOf(), viewMonthOf(), 1));
    } catch (e) {
      return iso(anchor);
    }
  }

  function weekdayLabels() {
    var labels = [];
    // 2024-01-01 is a Monday; format the next seven days as short weekday names.
    for (var i = 0; i < 7; i++) {
      try {
        labels.push(new Intl.DateTimeFormat(fullLocale(), { weekday: 'short' }).format(new Date(2024, 0, 1 + i)));
      } catch (e) {
        labels.push(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'][i]);
      }
    }
    return labels;
  }

  function dayItem(item) {
    var el = document.createElement('button');
    el.type = 'button';
    el.className = 'jellycrowd-cal-item' + (item.Requested ? ' jellycrowd-cal-item-requested' : '');
    var epTag = item.EpisodeNumber ? (' S' + (item.SeasonNumber || 0) + 'E' + item.EpisodeNumber) : '';
    el.title = (item.Title || '') + epTag + (item.EpisodeName ? ' — ' + item.EpisodeName : '') + (item.Requested ? ' · ' + t('requested') : '');
    // Distinct accent for titles already requested (vs plain releases). Anonymous — no requester shown.
    if (item.Requested) {
      el.style.borderLeft = '3px solid #a855f7';
      el.style.background = 'rgba(168,85,247,.18)';
    }
    if (item.PosterPath) {
      var img = document.createElement('img');
      img.loading = 'lazy';
      img.alt = '';
      img.src = THUMB_BASE + item.PosterPath;
      el.appendChild(img);
    }
    var label = document.createElement('span');
    label.textContent = (item.Title || '') + epTag;
    el.appendChild(label);
    el.addEventListener('click', function () {
      if (item.Available && item.JellyfinItemId) { navigateToItem(item.JellyfinItemId); } else { openModal(item); }
    });
    return el;
  }

  function renderGrid(byDate) {
    var grid = document.getElementById('jcCalGrid');
    grid.innerHTML = '';
    grid.classList.remove('jellycrowd-cal-list');

    weekdayLabels().forEach(function (label) {
      var head = document.createElement('div');
      head.className = 'jellycrowd-cal-weekday';
      head.textContent = label;
      grid.appendChild(head);
    });

    var todayIso = lib.isoDate(new Date());
    lib.buildMonthMatrix(viewYearOf(), viewMonthOf()).forEach(function (week) {
      week.forEach(function (cellDay) {
        var cell = document.createElement('div');
        if (!cellDay) {
          cell.className = 'jellycrowd-cal-cell jellycrowd-cal-cell-empty';
          grid.appendChild(cell);
          return;
        }
        cell.className = 'jellycrowd-cal-cell' + (cellDay.iso === todayIso ? ' jellycrowd-cal-today' : '');
        var num = document.createElement('div');
        num.className = 'jellycrowd-cal-daynum';
        num.textContent = String(cellDay.day);
        cell.appendChild(num);
        (byDate[cellDay.iso] || []).forEach(function (item) { cell.appendChild(dayItem(item)); });
        grid.appendChild(cell);
      });
    });
  }

  // Week/Day view: a vertical list of day sections (no horizontal scroll, mobile-friendly).
  function renderList(days, byDate) {
    var grid = document.getElementById('jcCalGrid');
    grid.innerHTML = '';
    grid.classList.add('jellycrowd-cal-list');

    var todayIso = lib.isoDate(new Date());
    days.forEach(function (d) {
      var dayIso = iso(d);
      var section = document.createElement('div');
      section.className = 'jellycrowd-cal-day-section' + (dayIso === todayIso ? ' jellycrowd-cal-today' : '');

      var header = document.createElement('div');
      header.className = 'jellycrowd-cal-day-header';
      try {
        header.textContent = new Intl.DateTimeFormat(fullLocale(), { weekday: 'long', day: 'numeric', month: 'long' }).format(d);
      } catch (e) { header.textContent = dayIso; }
      section.appendChild(header);

      var items = byDate[dayIso] || [];
      if (!items.length) {
        var none = document.createElement('div');
        none.className = 'jellycrowd-cal-day-empty';
        none.textContent = t('calendar_no_releases');
        section.appendChild(none);
      } else {
        var box = document.createElement('div');
        box.className = 'jellycrowd-cal-day-items';
        items.forEach(function (item) { box.appendChild(dayItem(item)); });
        section.appendChild(box);
      }

      grid.appendChild(section);
    });
  }

  function rangeFor() {
    if (viewMode === 'month') {
      var lastDay = new Date(viewYearOf(), viewMonthOf() + 1, 0).getDate();
      var mm = pad2(viewMonthOf() + 1);
      return { from: viewYearOf() + '-' + mm + '-01', to: viewYearOf() + '-' + mm + '-' + pad2(lastDay) };
    }
    var days = currentDays();
    return { from: iso(days[0]), to: iso(days[days.length - 1]) };
  }

  function load() {
    document.getElementById('jcCalMonth').textContent = periodLabel();
    setMessage(t('loading'));
    var range = rangeFor();
    apiGet('JellyCrowd/Quota/Me')
      .then(function (q) { quotaExceeded = !!(q && !q.Unlimited && q.QuotaBytes > 0 && q.UsedBytes >= q.QuotaBytes); })
      .catch(function () { /* best-effort */ })
      .then(function () {
        return apiGet('JellyCrowd/Catalog/Calendar?language=' + encodeURIComponent(fullLocale())
          + '&region=' + encodeURIComponent(REGION)
          + '&from=' + range.from + '&to=' + range.to
          + (filterLanguage ? '&originalLanguage=' + encodeURIComponent(filterLanguage) : '')
          + (filterCountry ? '&originCountry=' + encodeURIComponent(filterCountry) : ''));
      })
      .then(function (items) {
        var byDate = {};
        lib.groupByReleaseDate(items).forEach(function (group) { byDate[group.date] = group.items; });
        setMessage('');
        if (viewMode === 'month') { renderGrid(byDate); } else { renderList(currentDays(), byDate); }
      })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function step(delta) {
    if (viewMode === 'month') { anchor.setMonth(anchor.getMonth() + delta); }
    else if (viewMode === 'week') { anchor.setDate(anchor.getDate() + delta * 7); }
    else { anchor.setDate(anchor.getDate() + delta); }
    load();
  }

  function init() {
    loadConfigLang().then(loadStrings).then(loadAdmin).then(loadWatchlist).then(function () {
      document.getElementById('jcCalLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
      document.getElementById('jcCalTitle').textContent = t('calendar_title');
      document.getElementById('jcCalToday').textContent = t('calendar_today');
      document.getElementById('jcCalPrev').addEventListener('click', function () { step(-1); });
      document.getElementById('jcCalNext').addEventListener('click', function () { step(1); });
      // "Today" opens a date picker to jump to any date (defaults to the current view's date).
      var datePicker = document.getElementById('jcCalDatePicker');
      document.getElementById('jcCalToday').addEventListener('click', function () {
        datePicker.value = iso(anchor);
        if (typeof datePicker.showPicker === 'function') {
          try { datePicker.showPicker(); return; } catch (e) { /* fall back to focus */ }
        }
        datePicker.focus();
      });
      datePicker.addEventListener('change', function () {
        if (!datePicker.value) { return; }
        var p = datePicker.value.split('-');
        anchor = new Date(parseInt(p[0], 10), parseInt(p[1], 10) - 1, parseInt(p[2], 10));
        load();
      });

      // View switcher (Month / Week / Day).
      function setView(mode) {
        viewMode = mode;
        [['Month', 'month'], ['Week', 'week'], ['Day', 'day']].forEach(function (pair) {
          var b = document.getElementById('jcCalView' + pair[0]);
          if (b) { b.classList.toggle('jellycrowd-chip-active', pair[1] === mode); }
        });
        load();
      }
      document.getElementById('jcCalViewMonth').textContent = t('calendar_view_month');
      document.getElementById('jcCalViewWeek').textContent = t('calendar_view_week');
      document.getElementById('jcCalViewDay').textContent = t('calendar_view_day');
      document.getElementById('jcCalViewMonth').addEventListener('click', function () { setView('month'); });
      document.getElementById('jcCalViewWeek').addEventListener('click', function () { setView('week'); });
      document.getElementById('jcCalViewDay').addEventListener('click', function () { setView('day'); });
      document.getElementById('jcCalViewMonth').classList.add('jellycrowd-chip-active');

      var langSelect = document.getElementById('jcCalLang');
      fillCodeSelect(langSelect, FILTER_LANGUAGES, 'language', t('filters_language'), filterLanguage);
      langSelect.addEventListener('change', function () { filterLanguage = langSelect.value; load(); });
      var countrySelect = document.getElementById('jcCalCountry');
      fillCodeSelect(countrySelect, FILTER_COUNTRIES, 'region', t('filters_country'), filterCountry);
      countrySelect.addEventListener('change', function () { filterCountry = countrySelect.value; load(); });

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
