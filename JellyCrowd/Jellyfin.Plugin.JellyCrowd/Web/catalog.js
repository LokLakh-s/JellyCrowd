/*
 * Jelly Crowd — catalog page.
 * Browse/discover the TMDB catalog with genre/year/rating/sort filters, search, a hover preview,
 * a details modal (genres, runtime, TMDB/IMDb links) and per-title requests. All user-visible
 * strings come from Web/strings/<lang>.json so the UI follows the active Jellyfin/browser language.
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
  var commentsEnabled = false;   // community comments are an admin opt-in
  var isChild = false;           // current user is a child account (member of a child group)
  // Instance scope: which media types are offered, and which TV request granularities are allowed.
  var reqScope = { movies: true, series: true, allowSeries: true, allowSeason: true, allowEpisode: true };

  // Admin-only "request on behalf of" support. When an admin picks a user in a modal, requests made
  // from that modal are created for that user (via Requests/ForUser); empty = the admin themselves.
  var isAdmin = false;
  var adminUsers = [];
  var actAsUserId = null;
  var myUserId = '';

  // Watchlist: the user's followed titles. watchlistKeys is a quick membership set ("type:tmdbId"),
  // watchlistEntries the full list (rendered when the "My list" toggle is on).
  var watchlistKeys = {};
  var watchlistEntries = [];
  var showWatchlist = false;

  var MIN_YEAR = 1900;
  var MAX_YEAR = new Date().getFullYear();

  var filters = {
    mediaType: 'movie',
    genres: [],
    minYear: MIN_YEAR,
    maxYear: MAX_YEAR,
    minRating: 0,
    maxRating: 10,
    sortBy: 'popularity',
    watchProviders: '',
    originalLanguage: '',
    originCountry: '',
    personId: 0,
    personName: ''
  };

  // Last-loaded genre list (id+name), kept so the active-filter bar can label genre chips by name.
  var loadedGenres = [];

  // Curated codes for the language / country filters (labels are localized via Intl.DisplayNames).
  var FILTER_LANGUAGES = ['en', 'fr', 'es', 'it', 'de', 'pt', 'ja', 'ko', 'zh', 'hi'];
  var FILTER_COUNTRIES = ['US', 'FR', 'ES', 'IT', 'GB', 'DE', 'JP', 'KR', 'CA', 'IN'];

  function fillCodeSelect(select, codes, type, current) {
    select.innerHTML = '';
    var allOpt = document.createElement('option');
    allOpt.value = '';
    allOpt.textContent = t('filter_all');
    select.appendChild(allOpt);
    var names = null;
    try { names = new Intl.DisplayNames([fullLocale()], { type: type }); } catch (e) { names = null; }
    codes.forEach(function (code) {
      var opt = document.createElement('option');
      opt.value = code;
      var label = code;
      try { if (names) { label = names.of(code) || code; } } catch (e) { label = code; }
      opt.textContent = label;
      select.appendChild(opt);
    });
    select.value = current || '';
  }

  function fullLocale() {
    return lib.contentLocale(cfgLang, navigator.language || 'en-US');
  }

  function shortLang() {
    return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US');
  }

  function loadConfigLang() {
    return apiGet('JellyCrowd/Settings/Language')
      .then(function (d) { if (d) { if (d.Language) { cfgLang = String(d.Language).toLowerCase(); } commentsEnabled = d.CommentsEnabled === true; reqScope = lib.normalizeRequestScope(d); } })
      .catch(function () { /* keep 'auto' on failure */ });
  }

  function t(key) {
    return Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;
  }

  function pluginUrl(path) {
    if (window.ApiClient && typeof window.ApiClient.getUrl === 'function') {
      return window.ApiClient.getUrl(path);
    }
    return '/' + path;
  }

  function apiGet(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'GET', url: pluginUrl(path), dataType: 'json' });
    }
    return fetch(pluginUrl(path)).then(function (r) {
      if (!r.ok) {
        var err = new Error('HTTP ' + r.status);
        err.status = r.status;
        throw err;
      }
      return r.json();
    });
  }

  function apiPost(path, body) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({
        type: 'POST',
        url: pluginUrl(path),
        data: JSON.stringify(body),
        contentType: 'application/json',
        dataType: 'json'
      });
    }
    return fetch(pluginUrl(path), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    }).then(function (r) {
      if (!r.ok) {
        var err = new Error('HTTP ' + r.status);
        err.status = r.status;
        throw err;
      }
      return r.json();
    });
  }

  function loadStrings() {
    // Keep the labels already in hand when the catalog cannot be read: replacing them with an empty
    // object turns every button in the view into its raw key.
    return lib.fetchStrings(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (loaded) { if (loaded) { strings = loaded; } });
  }

  // ---------- cards ----------

  function navigateToItem(itemId) {
    var hash = '#/details?id=' + itemId;
    if (window.ApiClient && typeof window.ApiClient.serverId === 'function') {
      hash += '&serverId=' + window.ApiClient.serverId();
    }
    window.location.hash = hash;
  }

  // A disabled "request" button carrying the quota-exceeded explanation.
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

  function renderCard(item) {
    var card = document.createElement('div');
    card.className = 'jellycrowd-card jellycrowd-card-clickable';
    // Both available and not-yet-available titles open the details popup (available media gets a
    // "Add to my library" action + a visible link into Jellyfin from there).
    // Keyboard-accessible: it's a clickable card acting as a button.
    card.setAttribute('role', 'button');
    card.setAttribute('tabindex', '0');
    card.setAttribute('aria-label', item.Title || '');
    card.addEventListener('click', function () { openModal(item); });
    card.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openModal(item); }
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
    var rating = lib.formatRating(item.VoteAverage);
    var year = lib.yearOf(item);
    var hoverTitle = document.createElement('div');
    hoverTitle.className = 'jellycrowd-hover-title';
    hoverTitle.textContent = item.Title || '';
    var hoverMeta = document.createElement('div');
    hoverMeta.className = 'jellycrowd-hover-meta';
    hoverMeta.textContent = [rating ? '★ ' + rating : '', year].filter(Boolean).join('  ·  ');
    hover.appendChild(hoverTitle);
    hover.appendChild(hoverMeta);
    posterWrap.appendChild(hover);

    posterWrap.appendChild(watchlistStar(item, false));

    card.appendChild(posterWrap);

    // Every card gets a "Details" button: the modal is where requesting (or claiming an available
    // title into your library, plus the link into Jellyfin) happens.
    var button = document.createElement('button');
    button.className = 'jellycrowd-request';
    button.type = 'button';
    button.textContent = t('details_button');
    button.addEventListener('click', function (e) {
      e.stopPropagation();
      openModal(item);
    });
    card.appendChild(button);

    return card;
  }

  // Load whether the current user is an admin (and, if so, the user list) for "request on behalf of".
  // Per-user scope (authenticated): whether this account is a "child" account. Child accounts get the
  // age-filtered catalog, no free-text search, and no reviews.
  function loadScope() {
    return apiGet('JellyCrowd/Settings/Visibility')
      .then(function (d) { isChild = !!(d && d.IsChild); if (isChild) { commentsEnabled = false; } })
      .catch(function () { /* not authenticated / best-effort */ });
  }

  function loadAdmin() {
    if (!(window.ApiClient && typeof window.ApiClient.getCurrentUser === 'function')) {
      return Promise.resolve();
    }
    return window.ApiClient.getCurrentUser().then(function (user) {
      isAdmin = !!(user && user.Policy && user.Policy.IsAdministrator);
      myUserId = user ? user.Id : '';
      if (isAdmin && typeof window.ApiClient.getUsers === 'function') {
        return window.ApiClient.getUsers().then(function (users) { adminUsers = users || []; }).catch(function () { /* ignore */ });
      }
      return null;
    }).catch(function () { /* ignore */ });
  }

  // POST without expecting a JSON body back (watchlist add/remove return 200/204).
  function apiPostNoResult(path, body) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path), data: JSON.stringify(body), contentType: 'application/json' });
    }
    return fetch(pluginUrl(path), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      .then(function (r) { if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; } });
  }

  function loadWatchlist() {
    return apiGet('JellyCrowd/Watchlist').then(function (list) {
      watchlistEntries = list || [];
      watchlistKeys = {};
      watchlistEntries.forEach(function (e) { watchlistKeys[e.MediaType + ':' + e.TmdbId] = true; });
    }).catch(function () { /* best-effort */ });
  }

  function watchlistKey(item) { return item.MediaType + ':' + item.TmdbId; }

  function toggleWatchlist(item, refresh) {
    var key = watchlistKey(item);
    var on = !!watchlistKeys[key];
    var body = { TmdbId: item.TmdbId, MediaType: item.MediaType, Title: item.Title, PosterPath: item.PosterPath, ReleaseDate: item.ReleaseDate };
    return apiPostNoResult(on ? 'JellyCrowd/Watchlist/Remove' : 'JellyCrowd/Watchlist', body).then(function () {
      if (on) {
        delete watchlistKeys[key];
        watchlistEntries = watchlistEntries.filter(function (e) { return (e.MediaType + ':' + e.TmdbId) !== key; });
      } else {
        watchlistKeys[key] = true;
        watchlistEntries.unshift(body);
      }
      if (refresh) { refresh(); }
      if (showWatchlist) { resetFeed(); }
    }).catch(function () { /* ignore */ });
  }

  // Star toggle button. inline=true for the modal (text), else a corner icon for cards.
  function watchlistStar(item, inline) {
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = inline ? 'jellycrowd-star-inline' : 'jellycrowd-star';
    function refresh() {
      var on = !!watchlistKeys[watchlistKey(item)];
      btn.classList.toggle('jellycrowd-star-on', on);
      var label = on ? t('watchlist_remove') : t('watchlist_add');
      btn.textContent = inline ? ((on ? '★ ' : '☆ ') + label) : (on ? '★' : '☆');
      btn.title = label;
    }
    refresh();
    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      btn.disabled = true;
      toggleWatchlist(item, refresh).then(function () { btn.disabled = false; });
    });
    return btn;
  }

  // Route a request payload: to ForUser (admin acting as someone), else the normal endpoint.
  function refreshHeaderQuota() {
    if (typeof window.jellyCrowdRefreshQuota === 'function') { window.jellyCrowdRefreshQuota(); }
  }

  function submitRequest(payload) {
    // A new request pre-charges the quota (provisional estimate) — refresh the header bar so it shows.
    var p = actAsUserId
      ? (function () {
        var forUser = {};
        Object.keys(payload).forEach(function (k) { forUser[k] = payload[k]; });
        forUser.UserId = actAsUserId;
        return apiPost('JellyCrowd/Requests/ForUser', forUser);
      })()
      : apiPost('JellyCrowd/Requests', payload);
    return p.then(function (r) { refreshHeaderQuota(); return r; });
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
      if (error && error.status === 409) {
        button.textContent = t('already_requested');
      } else if (error && error.status === 422) {
        // The request alone is larger than the user's whole quota: it could never be downloaded.
        button.textContent = t('request_too_large');
      } else if (error && error.status === 403) {
        button.textContent = t('requests_disabled');
      } else {
        button.disabled = false;
        button.textContent = t('request_button');
      }
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

  // "Request whole season": when the season has unreleased episodes, create one request per episode
  // (each scheduled at its air date); otherwise a single season request.
  function requestSeason(item, season, button, dateInput) {
    // Guard: a season with no usable number must never fall through to a Season-less request, which the
    // backend would treat as a whole-series add (monitoring every season in Sonarr). Refuse instead.
    if (!season || typeof season.SeasonNumber !== 'number') {
      return;
    }
    button.disabled = true;
    button.textContent = t('requesting');
    apiGet('JellyCrowd/Catalog/Episodes/' + item.TmdbId + '/' + season.SeasonNumber + '?language=' + encodeURIComponent(fullLocale()))
      .then(function (episodes) {
        // Only real, numbered episodes can be requested per-episode. An entry with no/0 episode number
        // (an unnumbered/announced placeholder) would otherwise create a stray Episode-less request that
        // shows as a separate "· Sn" row next to the aggregated season.
        var valid = (episodes || []).filter(function (e) { return e.EpisodeNumber != null && e.EpisodeNumber > 0; });
        var hasFuture = valid.some(function (e) {
          return e.AirDate && new Date(e.AirDate + 'T00:00:00').getTime() > Date.now();
        });
        if (valid.length && hasFuture) {
          return Promise.all(valid.map(function (e) { return postEpisode(item, season.SeasonNumber, e).catch(function () { /* skip dups */ }); }))
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

  // Optional "desired date" picker (defaults to today): when the user wants the request fulfilled.
  // Reserved for future Servarr/custom download-script scheduling.
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

  // ---------- modal ----------

  function externalLink(href, text) {
    var a = document.createElement('a');
    a.href = href;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.textContent = text;
    return a;
  }

  var CAST_PROFILE_BASE = 'https://image.tmdb.org/t/p/w185';

  // A horizontal scrolling strip of cast members (photo + name + character).
  function renderCast(container, cast) {
    container.innerHTML = '';
    container.style.display = '';
    var title = document.createElement('h4');
    title.className = 'jellycrowd-cast-title';
    title.textContent = t('cast');
    container.appendChild(title);

    var strip = document.createElement('div');
    strip.className = 'jellycrowd-cast-strip';
    cast.forEach(function (member) {
      var cell = document.createElement('div');
      cell.className = 'jellycrowd-cast-cell';
      // Click a cast member → filter the catalog by that person.
      if (member.Id) {
        cell.classList.add('jellycrowd-link');
        cell.title = t('see_filmography');
        cell.addEventListener('click', function () { applyPersonFilter(member.Id, member.Name); });
      }
      var photo = document.createElement('div');
      photo.className = 'jellycrowd-cast-photo';
      if (member.ProfilePath) {
        var img = document.createElement('img');
        img.loading = 'lazy';
        img.alt = member.Name || '';
        img.src = CAST_PROFILE_BASE + member.ProfilePath;
        photo.appendChild(img);
      } else {
        photo.classList.add('jellycrowd-cast-photo-empty');
        photo.textContent = (member.Name || '?').slice(0, 1);
      }
      cell.appendChild(photo);
      var name = document.createElement('div');
      name.className = 'jellycrowd-cast-name';
      name.textContent = member.Name || '';
      cell.appendChild(name);
      if (member.Character) {
        var role = document.createElement('div');
        role.className = 'jellycrowd-cast-role';
        role.textContent = member.Character;
        cell.appendChild(role);
      }
      strip.appendChild(cell);
    });
    container.appendChild(strip);
  }

  // Preview of a saga before requesting it: the full list with each film's status, the count that
  // will actually be requested, and explicit Confirm/Cancel buttons (sendAll fires the requests).
  function renderSagaPreview(container, parts, pending, sendAll) {
    container.innerHTML = '';
    container.style.display = '';

    var heading = document.createElement('div');
    heading.className = 'jellycrowd-saga-heading';
    heading.textContent = t('saga_preview_title');
    container.appendChild(heading);

    var list = document.createElement('div');
    list.className = 'jellycrowd-saga-list';
    parts.forEach(function (p) {
      var row = document.createElement('div');
      row.className = 'jellycrowd-saga-row';
      var name = document.createElement('span');
      name.className = 'jellycrowd-saga-name';
      var year = lib.yearOf(p);
      name.textContent = (p.Title || '') + (year ? ' (' + year + ')' : '');
      // N16: each saga entry is clickable → opens that film's own popup.
      if (p.TmdbId) {
        name.classList.add('jellycrowd-link');
        name.title = t('details_button');
        name.addEventListener('click', function () { openModal(p); });
      }
      var status = document.createElement('span');
      status.className = 'jellycrowd-saga-status';
      if (p.Available) { status.textContent = t('available_badge'); status.classList.add('jellycrowd-saga-have'); }
      else { status.textContent = t('saga_to_request'); }
      row.appendChild(name);
      row.appendChild(status);
      list.appendChild(row);
    });
    container.appendChild(list);

    var actions = document.createElement('div');
    actions.className = 'jellycrowd-saga-actions';
    var cancel = document.createElement('button');
    cancel.type = 'button';
    cancel.className = 'jellycrowd-request jellycrowd-request-secondary';
    cancel.textContent = t('cancel');
    cancel.addEventListener('click', function () { container.style.display = 'none'; container.innerHTML = ''; });
    var confirm = document.createElement('button');
    confirm.type = 'button';
    confirm.className = 'jellycrowd-request';
    if (!pending.length) {
      confirm.disabled = true;
      confirm.textContent = t('saga_nothing_to_request');
    } else {
      confirm.textContent = t('saga_confirm').replace('{n}', pending.length);
      confirm.addEventListener('click', function () {
        confirm.disabled = true;
        cancel.disabled = true;
        confirm.textContent = t('requesting');
        sendAll().then(function () { confirm.textContent = t('requested'); })
          .catch(function () { confirm.disabled = false; cancel.disabled = false; confirm.textContent = t('saga_confirm').replace('{n}', pending.length); });
      });
    }
    actions.appendChild(cancel);
    actions.appendChild(confirm);
    container.appendChild(actions);
  }

  function renderSeasonRequests(container, item, seasons, dateInput, requested) {
    var req = requested || { seasons: {}, episodes: {} };
    container.innerHTML = '';
    // Drop the "Specials" pseudo-season (TMDB numbers it 0): the popup lists real seasons only.
    (seasons || []).filter(function (season) { return season && season.SeasonNumber !== 0; }).forEach(function (season) {
      var row = document.createElement('div');
      row.className = 'jellycrowd-season-row';

      var label = document.createElement('span');
      label.textContent = lib.seasonLabel(season, t);
      row.appendChild(label);

      var actions = document.createElement('span');
      actions.className = 'jellycrowd-season-actions';

      // Toggle to reveal the season's episodes (each requestable individually) — only when episode
      // requests are allowed.
      var episodesBox = document.createElement('div');
      episodesBox.className = 'jellycrowd-episodes';
      episodesBox.style.display = 'none';
      if (reqScope.allowEpisode) {
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
      }

      // Whole-season request — only when season requests are allowed.
      if (reqScope.allowSeason) {
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
      }

      row.appendChild(actions);
      container.appendChild(row);
      container.appendChild(episodesBox);
    });
  }

  // ---------- reviews (rating 1–10, shown as half-stars; anonymous to non-admins) ----------

  // Read-only star display for a 1–10 value (5 stars; each star = 2 points, half-star = 1).
  function starsDisplay(value) {
    var wrap = document.createElement('span');
    wrap.style.cssText = 'display:inline-flex;gap:.05em;font-size:1.05em;line-height:1;vertical-align:middle;';
    for (var i = 0; i < 5; i++) {
      var pct = Math.max(0, Math.min(100, (Number(value) - i * 2) / 2 * 100));
      var star = document.createElement('span');
      star.style.cssText = 'position:relative;display:inline-block;width:1em;color:#888;';
      star.textContent = '★';
      var fill = document.createElement('span');
      fill.style.cssText = 'position:absolute;left:0;top:0;overflow:hidden;white-space:nowrap;color:#f5c518;width:' + pct + '%;';
      fill.textContent = '★';
      star.appendChild(fill);
      wrap.appendChild(star);
    }
    return wrap;
  }

  // Interactive half-star input (value 1–10). Hover previews the rating under the cursor; click sets it.
  function starInput(initial) {
    var wrap = document.createElement('span');
    wrap.className = 'jellycrowd-star-input';
    wrap.style.cssText = 'display:inline-flex;gap:.1em;font-size:1.7em;line-height:1;cursor:pointer;';
    var value = initial || 0;
    var fills = [];
    // Paint the stars filled up to `shown` (defaults to the committed value).
    function render(shown) {
      var v = (shown === undefined || shown === null) ? value : shown;
      fills.forEach(function (f, idx) {
        f.style.width = Math.max(0, Math.min(100, (v - idx * 2) / 2 * 100)) + '%';
      });
    }
    function valueAt(star, idx, clientX) {
      var r = star.getBoundingClientRect();
      var leftHalf = (clientX - r.left) < r.width / 2;
      return idx * 2 + (leftHalf ? 1 : 2);
    }
    for (var i = 0; i < 5; i++) {
      (function (idx) {
        var star = document.createElement('span');
        star.style.cssText = 'position:relative;display:inline-block;width:1em;color:#888;transition:transform .05s;';
        star.textContent = '★';
        var fill = document.createElement('span');
        fill.style.cssText = 'position:absolute;left:0;top:0;overflow:hidden;white-space:nowrap;color:#f5c518;width:0;';
        fill.textContent = '★';
        star.appendChild(fill);
        // Live preview as the cursor moves across the star (shows exactly what a click would set).
        star.addEventListener('mousemove', function (e) { render(valueAt(star, idx, e.clientX)); });
        star.addEventListener('click', function (e) { value = valueAt(star, idx, e.clientX); render(); });
        fills.push(fill);
        wrap.appendChild(star);
      })(i);
    }
    // Leaving the whole control restores the committed value.
    wrap.addEventListener('mouseleave', function () { render(); });
    render();
    wrap.getValue = function () { return value; };
    wrap.setValue = function (v) { value = v || 0; render(); };
    return wrap;
  }

  function renderReviewAverage(el, dto) {
    el.innerHTML = '';
    if (!dto || !dto.Count) {
      el.textContent = t('no_ratings');
      el.style.opacity = '.7';
      return;
    }
    el.style.opacity = '1';
    el.appendChild(starsDisplay(dto.Average));
    var num = document.createElement('span');
    num.style.marginLeft = '.4em';
    num.textContent = dto.Average.toFixed(1) + '/10 · ' + dto.Count + ' ' + t('ratings_count');
    el.appendChild(num);
  }

  function renderReviews(listEl, reviews) {
    listEl.innerHTML = '';
    if (!reviews || !reviews.length) {
      var empty = document.createElement('div');
      empty.className = 'jellycrowd-request-sub';
      empty.textContent = t('comments_empty');
      listEl.appendChild(empty);
      return;
    }
    reviews.forEach(function (c) {
      var row = document.createElement('div');
      row.className = 'jellycrowd-comment';
      var head = document.createElement('div');
      head.className = 'jellycrowd-comment-head';
      if (c.Rating > 0) { head.appendChild(starsDisplay(c.Rating)); }
      // Author name is only present for admins; everyone else sees anonymous reviews.
      if (c.UserName) {
        var who = document.createElement('span');
        who.className = 'jellycrowd-comment-author';
        who.style.marginLeft = '.5em';
        who.textContent = c.UserName;
        head.appendChild(who);
      }
      var when = document.createElement('span');
      when.className = 'jellycrowd-comment-date';
      when.textContent = c.CreatedAt ? new Date(c.CreatedAt).toLocaleDateString() : '';
      head.appendChild(when);

      if (isAdmin) {
        head.appendChild(commentAction(t('comment_hide'), 'JellyCrowd/Comments/' + c.Id + '/Hide', row));
        head.appendChild(commentAction(t('comment_delete'), 'JellyCrowd/Comments/' + c.Id + '/Delete', row));
      } else if (c.Mine) {
        head.appendChild(commentAction(t('comment_delete'), 'JellyCrowd/Comments/' + c.Id + '/DeleteMine', row));
      }
      row.appendChild(head);

      if (c.Text) {
        var text = document.createElement('div');
        text.className = 'jellycrowd-comment-text';
        text.textContent = c.Text;
        row.appendChild(text);
      }
      listEl.appendChild(row);
    });
  }

  function commentAction(label, path, row) {
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'jellycrowd-comment-action';
    btn.textContent = label;
    btn.addEventListener('click', function () {
      btn.disabled = true;
      apiPostNoResult(path).then(function () { row.remove(); }).catch(function () { btn.disabled = false; });
    });
    return btn;
  }

  function buildCommentsSection(item, metaEl) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-comments';
    var title = document.createElement('h4');
    title.className = 'jellycrowd-comments-title';
    title.textContent = t('reviews');
    section.appendChild(title);

    var avg = document.createElement('div');
    avg.className = 'jellycrowd-reviews-average';
    avg.style.cssText = 'margin:.2em 0 .6em;display:flex;align-items:center;flex-wrap:wrap;gap:.1em;';
    section.appendChild(avg);

    var list = document.createElement('div');
    list.className = 'jellycrowd-comments-list';
    section.appendChild(list);

    // Your review: star rating (required) + optional text.
    var form = document.createElement('div');
    form.className = 'jellycrowd-comment-form';
    var rateRow = document.createElement('div');
    rateRow.style.cssText = 'display:flex;align-items:center;gap:.6em;margin-bottom:.3em;';
    var rateLabel = document.createElement('span');
    rateLabel.textContent = t('your_review');
    var stars = starInput(0);
    rateRow.appendChild(rateLabel);
    rateRow.appendChild(stars);
    var input = document.createElement('textarea');
    input.className = 'jellycrowd-comment-input';
    input.rows = 2;
    input.placeholder = t('review_text_placeholder');
    var post = document.createElement('button');
    post.type = 'button';
    post.className = 'jellycrowd-request';
    post.textContent = t('review_submit');
    post.addEventListener('click', function () {
      var rating = stars.getValue();
      if (rating < 1) { post.textContent = t('rating_required'); setTimeout(function () { post.textContent = t('review_submit'); }, 1500); return; }
      post.disabled = true;
      apiPost('JellyCrowd/Comments', { MediaType: item.MediaType, TmdbId: item.TmdbId, Title: item.Title || '', Text: input.value.trim(), Rating: rating })
        .then(function () { post.disabled = false; reload(); })
        .catch(function () { post.disabled = false; });
    });
    form.appendChild(rateRow);
    form.appendChild(input);
    form.appendChild(post);
    section.appendChild(form);

    function reload() {
      apiGet('JellyCrowd/Comments/' + item.MediaType + '/' + item.TmdbId)
        .then(function (dto) {
          dto = dto || { Average: 0, Count: 0, Reviews: [] };
          renderReviewAverage(avg, dto);
          renderReviews(list, dto.Reviews || []);
          if (metaEl && dto.Count) {
            var chip = metaEl.querySelector('.jellycrowd-internal-rating');
            if (!chip) { chip = document.createElement('span'); chip.className = 'jellycrowd-internal-rating'; metaEl.appendChild(chip); }
            chip.textContent = '⬤ ' + dto.Average.toFixed(1) + '/10';
            chip.title = t('reviews') + ' (' + dto.Count + ')';
          }
          var mine = (dto.Reviews || []).filter(function (r) { return r.Mine; })[0];
          if (mine) { stars.setValue(mine.Rating); input.value = mine.Text || ''; post.textContent = t('review_update'); }
        })
        .catch(function () { /* reviews are best-effort */ });
    }

    reload();
    return section;
  }

  // "Report a problem": a link that reveals a small reason form and submits a report.
  // The "Report a problem" button (placed on the genres row). When clicked it reveals a full-width
  // report form inserted right after `anchorEl` so the textarea isn't cramped in the genres line.
  function buildReportSection(item, anchorEl) {
    var link = document.createElement('button');
    link.type = 'button';
    link.className = 'jellycrowd-report-link';
    link.textContent = '⚠ ' + t('report_problem');
    link.addEventListener('click', function () {
      link.disabled = true;
      var wrap = document.createElement('div');
      wrap.className = 'jellycrowd-report';
      var form = document.createElement('div');
      form.className = 'jellycrowd-comment-form';
      var typeSel = document.createElement('select');
      typeSel.className = 'jellycrowd-report-type';
      [['bug', 'report_type_bug'], ['subtitles', 'report_type_subtitles'], ['audio', 'report_type_audio'], ['quality', 'report_type_quality'], ['other', 'report_type_other']].forEach(function (o) {
        var opt = document.createElement('option');
        opt.value = o[0];
        opt.textContent = t(o[1]);
        typeSel.appendChild(opt);
      });
      var input = document.createElement('textarea');
      input.className = 'jellycrowd-comment-input';
      input.rows = 2;
      input.placeholder = t('report_placeholder');
      var send = document.createElement('button');
      send.type = 'button';
      send.className = 'jellycrowd-request';
      send.textContent = t('report_send');
      send.addEventListener('click', function () {
        var msg = input.value.trim();
        if (!msg) { return; }
        send.disabled = true;
        apiPost('JellyCrowd/Reports', { MediaType: item.MediaType, TmdbId: item.TmdbId, Title: item.Title, Message: msg, Type: typeSel.value })
          .then(function () { wrap.textContent = t('report_thanks'); })
          .catch(function () { send.disabled = false; });
      });
      form.appendChild(typeSel);
      form.appendChild(input);
      form.appendChild(send);
      wrap.appendChild(form);
      anchorEl.insertAdjacentElement('afterend', wrap);
    });
    return link;
  }

  // Admin-only block in the media detail popup: every request for this title (who wanted it, status, scope).
  function appendAdminMediaInfo(container, item) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-admin-mediainfo';
    var head = document.createElement('div');
    head.className = 'jellycrowd-admin-mediainfo-title';
    section.appendChild(head);
    container.appendChild(section);
    apiGet('JellyCrowd/Requests/Media/' + item.MediaType + '/' + item.TmdbId)
      .then(function (list) {
        if (!list || !list.length) { section.remove(); return; }
        var users = {};
        list.forEach(function (r) { if (r.UserName) { users[r.UserName] = true; } });
        head.textContent = t('admin_media_requests').replace('{n}', String(Object.keys(users).length));
        var table = document.createElement('table');
        table.className = 'jellycrowd-admin-table';
        var tbody = document.createElement('tbody');
        list.forEach(function (r) {
          var tr = document.createElement('tr');
          function td(x, cls) { var c = document.createElement('td'); c.textContent = x; if (cls) { c.className = cls; } return c; }
          var scope = (r.Season != null) ? ' · S' + r.Season + (r.Episode != null ? 'E' + r.Episode : '') : '';
          tr.appendChild(td((r.UserName || '—') + scope));
          tr.appendChild(td(r.Status || '', 'jellycrowd-admin-sub'));
          tr.appendChild(td(r.RequestedAt ? new Date(r.RequestedAt).toLocaleDateString() : '', 'jellycrowd-admin-sub'));
          tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        section.appendChild(table);
      })
      .catch(function () { section.remove(); });
  }

  function openModal(item) {
    var overlay = document.createElement('div');
    overlay.className = 'jellycrowd-modal-overlay';

    var modal = document.createElement('div');
    modal.className = 'jellycrowd-modal';
    modal.setAttribute('role', 'dialog');
    modal.setAttribute('aria-modal', 'true');
    modal.setAttribute('aria-label', item.Title || t('details_button'));
    if (item.BackdropPath) {
      modal.style.backgroundImage = 'url("' + BACKDROP_BASE + item.BackdropPath + '")';
    }

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'jellycrowd-modal-close';
    close.setAttribute('aria-label', t('close'));
    close.textContent = '✕';

    var body = document.createElement('div');
    body.className = 'jellycrowd-modal-body';

    // Left column: poster + the request/claim controls underneath it (keeps the popup short — the
    // request action no longer pushes everything down the right column / off-screen).
    var leftCol = document.createElement('div');
    leftCol.className = 'jellycrowd-modal-left';
    if (item.PosterPath) {
      var poster = document.createElement('img');
      poster.className = 'jellycrowd-modal-poster';
      poster.loading = 'lazy';
      poster.alt = item.Title || '';
      poster.src = POSTER_BASE + item.PosterPath;
      leftCol.appendChild(poster);
    }
    var requestHost = document.createElement('div');
    requestHost.className = 'jellycrowd-modal-request';
    leftCol.appendChild(requestHost);
    body.appendChild(leftCol);

    var content = document.createElement('div');
    content.className = 'jellycrowd-modal-content';

    // Title on the left, "Report a problem" pushed to the right of the same line.
    var titleRow = document.createElement('div');
    titleRow.className = 'jellycrowd-modal-title-row';
    var title = document.createElement('h2');
    title.className = 'jellycrowd-modal-title';
    title.textContent = lib.formatTitle(item);
    titleRow.appendChild(title);
    titleRow.appendChild(buildReportSection(item, titleRow));
    content.appendChild(titleRow);

    var meta = document.createElement('div');
    meta.className = 'jellycrowd-modal-meta';
    var rating = lib.formatRating(item.VoteAverage);
    if (rating) {
      var ratingSpan = document.createElement('span');
      ratingSpan.textContent = '★ ' + rating;
      meta.appendChild(ratingSpan);
    }
    if (item.Available) {
      var availSpan = document.createElement('span');
      availSpan.textContent = t('available_badge');
      meta.appendChild(availSpan);
    }
    if (item.ReleaseDate) {
      var relSpan = document.createElement('span');
      var relDate = new Date(item.ReleaseDate);
      relSpan.textContent = isNaN(relDate.getTime()) ? item.ReleaseDate : relDate.toLocaleDateString();
      meta.appendChild(relSpan);
    }
    content.appendChild(meta);
    meta.appendChild(watchlistStar(item, true));

    var genresEl = document.createElement('div');
    genresEl.className = 'jellycrowd-modal-genres';
    content.appendChild(genresEl);

    var overview = document.createElement('p');
    overview.className = 'jellycrowd-modal-overview';
    overview.textContent = item.Overview || t('no_overview');
    content.appendChild(overview);

    // Director + original title (filled by the details enrichment below).
    var creditsLine = document.createElement('div');
    creditsLine.className = 'jellycrowd-modal-credits';
    content.appendChild(creditsLine);

    // External links (TMDB/IMDb) sit between the synopsis and the cast.
    var links = document.createElement('div');
    links.className = 'jellycrowd-modal-links';
    links.appendChild(externalLink('https://www.themoviedb.org/' + item.MediaType + '/' + item.TmdbId, t('view_tmdb')));
    content.appendChild(links);

    // Cast strip (filled by the details enrichment below; hidden until then).
    var castEl = document.createElement('div');
    castEl.className = 'jellycrowd-cast';
    castEl.style.display = 'none';
    content.appendChild(castEl);

    // Reviews section (admin opt-in). Passes meta so the internal average rating can also appear next to
    // the TMDB rating. For a movie it sits in the right column under the cast; for a series it is moved
    // below the full-width season picker (appended further down) so the requestable seasons come first.
    var commentsSection = commentsEnabled ? buildCommentsSection(item, meta) : null;
    if (commentsSection && item.MediaType !== 'tv') {
      content.appendChild(commentsSection);
    }

    // Request controls live under the poster (left column) to keep the popup short — except a TV
    // season picker, which needs full width: it goes in its own section spanning under the body.
    // TV availability is per-season, so a series ALWAYS uses the full-width season picker — even when
    // some seasons are already in the library — so the other seasons stay requestable (N34). Only movies
    // use the whole-title available/claim flow.
    var seasonsSection = null;
    var reqTarget = requestHost;
    if (item.MediaType === 'tv') {
      seasonsSection = document.createElement('div');
      seasonsSection.className = 'jellycrowd-modal-seasons-section';
      reqTarget = seasonsSection;
    }

    // Admin-only "request on behalf of" selector. Applies to every request control in this modal.
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
      reqTarget.appendChild(adminRow);
    }

    if (isAdmin) { appendAdminMediaInfo(content, item); }

    var dateInput = null;
    if (item.MediaType === 'tv') {
      // A series is always season-driven. If it's already (partly) in the library, still offer a quick
      // Jellyfin link, then list every season so the missing ones can be requested.
      if (item.Available && item.JellyfinItemId) {
        var openRow = document.createElement('div');
        openRow.className = 'jellycrowd-modal-actions';
        var openSeriesBtn = document.createElement('button');
        openSeriesBtn.className = 'jellycrowd-request jellycrowd-open-jellyfin';
        openSeriesBtn.type = 'button';
        openSeriesBtn.textContent = t('open_in_jellyfin');
        openSeriesBtn.addEventListener('click', function () { navigateToItem(item.JellyfinItemId); });
        openRow.appendChild(openSeriesBtn);
        reqTarget.appendChild(openRow);
      }

      if (!quotaExceeded) {
        var tvDateRow = buildDesiredDateRow();
        dateInput = tvDateRow.input;
        reqTarget.appendChild(tvDateRow.row);
      }

      // Whole-series request (one click for the entire show). Only when the admin allows it and the show
      // isn't already fully in the library.
      if (reqScope.allowSeries && !quotaExceeded && !item.Available) {
        var seriesBtn = document.createElement('button');
        seriesBtn.className = 'jellycrowd-request';
        seriesBtn.type = 'button';
        seriesBtn.textContent = t('request_series');
        seriesBtn.addEventListener('click', function () { requestItem(item, seriesBtn, null, dateInput); });
        reqTarget.appendChild(seriesBtn);
      }

      // The per-season / per-episode list, only when at least one of those granularities is allowed.
      if (reqScope.allowSeason || reqScope.allowEpisode) {
        var seasonsEl = document.createElement('div');
        seasonsEl.className = 'jellycrowd-seasons';
        reqTarget.appendChild(seasonsEl);
        Promise.all([
          apiGet('JellyCrowd/Catalog/Seasons/' + item.TmdbId + '?language=' + encodeURIComponent(fullLocale())),
          loadRequestedKeys(item.TmdbId)
        ])
          .then(function (res) { renderSeasonRequests(seasonsEl, item, res[0], dateInput, res[1]); })
          .catch(function () { /* seasons are best-effort */ });
      }
    } else if (!item.Available) {
      if (!quotaExceeded) {
        var dateRow = buildDesiredDateRow();
        dateInput = dateRow.input;
        reqTarget.appendChild(dateRow.row);
      }

      if (quotaExceeded) {
        reqTarget.appendChild(blockedRequestButton());
      } else {
        var requestButton = document.createElement('button');
        requestButton.className = 'jellycrowd-request';
        requestButton.type = 'button';
        requestButton.textContent = t('request_button');
        requestButton.addEventListener('click', function () { requestItem(item, requestButton, null, dateInput); });
        reqTarget.appendChild(requestButton);
      }
    } else {
      // Already in the library: offer a visible link into Jellyfin, then let the user claim it into
      // their own media (shared ownership: starts the expiry clock + lets them request deletion).
      var actions = document.createElement('div');
      actions.className = 'jellycrowd-modal-actions';

      if (item.JellyfinItemId) {
        var openBtn = document.createElement('button');
        openBtn.className = 'jellycrowd-request jellycrowd-open-jellyfin';
        openBtn.type = 'button';
        openBtn.textContent = t('open_in_jellyfin');
        openBtn.addEventListener('click', function () { navigateToItem(item.JellyfinItemId); });
        actions.appendChild(openBtn);
      }

      // Owning an already-present title still costs its size on disk, so a full quota blocks it exactly
      // like a request: the button is shown disabled with the reason rather than letting the click fail.
      var claimNote = document.createElement('div');
      claimNote.className = 'jellycrowd-request-sub';
      claimNote.textContent = quotaExceeded ? t('claim_quota_blocked') : t('claim_quota_warning');

      var claimBtn = document.createElement('button');
      claimBtn.className = 'jellycrowd-request jellycrowd-request-secondary';
      claimBtn.type = 'button';
      claimBtn.textContent = t('add_to_my_media');
      claimBtn.title = quotaExceeded ? t('claim_quota_blocked') : t('claim_quota_warning');
      if (quotaExceeded) {
        claimBtn.classList.add('jellycrowd-request-blocked');
        claimBtn.disabled = true;
      }
      claimBtn.addEventListener('click', function () {
        claimBtn.disabled = true;
        apiPost('JellyCrowd/Requests/Claim', {
          TmdbId: item.TmdbId,
          MediaType: item.MediaType,
          Title: item.Title,
          PosterPath: item.PosterPath,
          ReleaseDate: item.ReleaseDate
        }).then(function () {
          claimBtn.textContent = t('added');
          refreshHeaderQuota();
        }).catch(function (error) {
          if (error && error.status === 409) { claimBtn.textContent = t('already_yours'); }
          else if (error && error.status === 422) {
            // The server measured the title against what is left: it does not fit. Say so where the
            // warning was, and leave the button blocked — retrying changes nothing until space is freed.
            claimBtn.textContent = t('quota_exceeded');
            claimBtn.title = t('claim_quota_blocked');
            claimBtn.classList.add('jellycrowd-request-blocked');
            claimNote.textContent = t('claim_quota_blocked');
          } else { claimBtn.disabled = false; }
        });
      });
      actions.appendChild(claimBtn);
      reqTarget.appendChild(actions);
      reqTarget.appendChild(claimNote);
    }

    body.appendChild(content);

    // Enrich with full details (genres, runtime, IMDb link).
    apiGet('JellyCrowd/Catalog/Details/' + item.MediaType + '/' + item.TmdbId + '?language=' + encodeURIComponent(fullLocale()))
      .then(function (details) {
        if (!details) {
          return;
        }
        (details.Genres || []).forEach(function (name) {
          var chip = document.createElement('span');
          chip.className = 'jellycrowd-chip';
          chip.textContent = name;
          genresEl.appendChild(chip);
        });
        if (details.Cast && details.Cast.length) {
          renderCast(castEl, details.Cast);
        }
        if (details.Runtime) {
          var rt = document.createElement('span');
          rt.textContent = details.Runtime + ' ' + t('runtime_min');
          meta.appendChild(rt);
        }
        if (details.ImdbId) {
          links.appendChild(externalLink('https://www.imdb.com/title/' + details.ImdbId, t('view_imdb')));
        }

        // Director / creator + writer (clickable → filmography) + original title, between synopsis and links.
        creditsLine.innerHTML = '';
        function appendPeople(label, people) {
          if (!people || !people.length) { return; }
          if (creditsLine.childNodes.length) { creditsLine.appendChild(document.createTextNode(' · ')); }
          creditsLine.appendChild(document.createTextNode(label + ' : '));
          people.forEach(function (p, i) {
            if (i > 0) { creditsLine.appendChild(document.createTextNode(', ')); }
            if (p.Id) {
              var a = document.createElement('span');
              a.className = 'jellycrowd-link';
              a.textContent = p.Name;
              a.title = t('see_filmography');
              a.addEventListener('click', function () { applyPersonFilter(p.Id, p.Name); });
              creditsLine.appendChild(a);
            } else {
              creditsLine.appendChild(document.createTextNode(p.Name));
            }
          });
        }
        appendPeople(t('director'), details.Directors);
        appendPeople(t('writer'), details.Writers);
        if (details.OriginalTitle) {
          if (creditsLine.childNodes.length) { creditsLine.appendChild(document.createTextNode(' · ')); }
          creditsLine.appendChild(document.createTextNode(t('original_title') + ' : ' + details.OriginalTitle));
        }

        // "Request whole saga": for a movie that belongs to a TMDB collection, request every part.
        if (item.MediaType === 'movie' && details.CollectionId && !item.Available && !quotaExceeded) {
          var sagaBtn = document.createElement('button');
          sagaBtn.className = 'jellycrowd-request';
          sagaBtn.type = 'button';
          sagaBtn.style.margin = '.5em 0 0'; // stacks under the plain "Request" button (left column)
          sagaBtn.textContent = t('request_saga');
          var sagaPreview = document.createElement('div');
          sagaPreview.className = 'jellycrowd-saga-preview';
          sagaPreview.style.display = 'none';

          // First click previews the saga (list + how many will actually be requested); the user must
          // then confirm before anything is sent — requesting a whole franchise blind was too risky.
          sagaBtn.addEventListener('click', function () {
            sagaBtn.disabled = true;
            sagaBtn.textContent = t('loading');
            apiGet('JellyCrowd/Catalog/Collection/' + details.CollectionId + '?language=' + encodeURIComponent(fullLocale()))
              .then(function (parts) {
                parts = parts || [];
                var pending = parts.filter(function (p) { return !p.Available; });
                renderSagaPreview(sagaPreview, parts, pending, function () {
                  return Promise.all(pending.map(function (p) {
                    return submitRequest({
                      TmdbId: p.TmdbId,
                      MediaType: 'movie',
                      Title: p.Title,
                      PosterPath: p.PosterPath,
                      ReleaseDate: p.ReleaseDate,
                      Season: null,
                      Episode: null,
                      DesiredAt: null
                    }).catch(function () { /* skip dups / errors */ });
                  }));
                });
                sagaBtn.style.display = 'none'; // replaced by the preview's own confirm/cancel
              })
              .catch(function () { sagaBtn.disabled = false; sagaBtn.textContent = t('request_saga'); });
          });
          reqTarget.appendChild(sagaBtn);
          reqTarget.appendChild(sagaPreview);
        }
      })
      .catch(function () { /* details are best-effort */ });

    modal.appendChild(close);
    modal.appendChild(body);
    if (seasonsSection) { modal.appendChild(seasonsSection); } // full-width TV season picker under the body
    if (commentsSection && item.MediaType === 'tv') {
      // For a series, reviews go under the seasons (so the requestable seasons come first) — full width,
      // on the same solid dark background that now runs to the bottom of the popup.
      var reviewsSection = document.createElement('div');
      reviewsSection.className = 'jellycrowd-modal-reviews-section';
      reviewsSection.appendChild(commentsSection);
      modal.appendChild(reviewsSection);
    }

    // N17: "Related media" strip at the very bottom (TMDB recommendations), each poster clickable.
    var relatedSection = document.createElement('div');
    relatedSection.className = 'jellycrowd-modal-related-section';
    relatedSection.style.display = 'none';
    modal.appendChild(relatedSection);

    overlay.appendChild(modal);
    document.body.appendChild(overlay);
    // Remember what had focus (the card/Details button that opened this) so we can restore it on close —
    // otherwise keyboard/TV-remote users lose their place when the dialog goes away.
    var opener = document.activeElement;
    // Move keyboard focus into the dialog (the close button) so Esc / Tab work from here.
    try { close.focus(); } catch (e) { /* focus is best-effort */ }

    apiGet('JellyCrowd/Catalog/Related/' + item.MediaType + '/' + item.TmdbId + '?language=' + encodeURIComponent(fullLocale()))
      .then(function (related) {
        if (!related || !related.length) { return; }
        var heading = document.createElement('div');
        heading.className = 'jellycrowd-modal-related-heading';
        heading.textContent = t('related_media');
        relatedSection.appendChild(heading);
        var strip = document.createElement('div');
        strip.className = 'jellycrowd-related-strip';
        related.slice(0, 12).forEach(function (r) {
          var card = renderCard(r);            // clicking opens its modal…
          card.addEventListener('click', dismiss); // …and closes the current one (no stacked overlays).
          strip.appendChild(card);
        });
        relatedSection.appendChild(strip);
        relatedSection.style.display = '';
      })
      .catch(function () { /* related is best-effort */ });

    function dismiss() {
      overlay.remove();
      document.removeEventListener('keydown', onKey);
      // Return focus to whatever opened the dialog so keyboard/remote navigation resumes in place.
      try { if (opener && opener.focus) { opener.focus(); } } catch (e) { /* best-effort */ }
    }
    function onKey(e) {
      if (e.key === 'Escape') {
        dismiss();
        return;
      }
      // Focus trap: keep Tab / Shift+Tab cycling inside the dialog instead of leaking to the page behind.
      // Recomputed on each Tab so async content (related titles, reviews) is included once it loads.
      lib.handleTrapKeydown(e, modal);
    }
    close.addEventListener('click', dismiss);
    overlay.addEventListener('click', function (e) {
      if (e.target === overlay) {
        dismiss();
      }
    });
    document.addEventListener('keydown', onKey);
  }

  // ---------- feed (infinite scroll + interleaved category rows) ----------

  var REGION = (fullLocale().split('-')[1] || 'US').toUpperCase();
  var PROVIDER_LOGO_BASE = 'https://image.tmdb.org/t/p/w92';
  // Fallback list used if the TMDB providers endpoint is unavailable.
  var PLATFORMS = [
    { Id: 8, Name: 'Netflix' },
    { Id: 119, Name: 'Prime Video' },
    { Id: 337, Name: 'Disney+' },
    { Id: 350, Name: 'Apple TV+' },
    { Id: 1899, Name: 'Max' }
  ];
  var providers = [];

  function loadProviders() {
    return apiGet('JellyCrowd/Catalog/Providers/' + filters.mediaType + '?region=' + encodeURIComponent(REGION) + '&language=' + encodeURIComponent(fullLocale()))
      .then(function (list) { providers = (list && list.length) ? list.slice(0, 15) : []; })
      .catch(function () { providers = []; });
  }

  function platformList() {
    return providers.length ? providers : PLATFORMS;
  }

  var searchQuery = '';
  var gridPage = 0;
  var feedLoading = false;
  var feedExhausted = false;
  var feedObserver = null;

  function setMessage(text) {
    var el = document.getElementById('jcMessage');
    if (text) {
      el.textContent = text;
      el.hidden = false;
    } else {
      el.hidden = true;
    }
  }

  function showError(error) {
    setMessage(t(lib.errorKey(error && error.status)));
  }

  function hasActiveFilters() {
    return filters.genres.length > 0 || filters.minYear > MIN_YEAR || filters.maxYear < MAX_YEAR
      || filters.minRating > 0 || filters.maxRating < 10 || filters.sortBy !== 'popularity'
      || !!filters.watchProviders || !!filters.originalLanguage || !!filters.originCountry
      || filters.personId > 0;
  }

  function baseDiscover() {
    return 'JellyCrowd/Catalog/Discover?mediaType=' + lib.discoverMediaType(filters)
      + '&language=' + encodeURIComponent(fullLocale())
      + '&region=' + encodeURIComponent(REGION);
  }

  function pagePath(page) {
    if (searchQuery) {
      return 'JellyCrowd/Catalog/Search?query=' + encodeURIComponent(searchQuery)
        + '&language=' + encodeURIComponent(fullLocale()) + '&page=' + page;
    }
    var p = baseDiscover() + '&sortBy=' + encodeURIComponent(filters.sortBy) + '&page=' + page;
    if (filters.genres.length) { p += '&genres=' + encodeURIComponent(filters.genres.join(',')); }
    if (filters.minYear > MIN_YEAR) { p += '&minYear=' + filters.minYear; }
    if (filters.maxYear < MAX_YEAR) { p += '&maxYear=' + filters.maxYear; }
    if (filters.minRating > 0) { p += '&minRating=' + filters.minRating; }
    if (filters.maxRating < 10) { p += '&maxRating=' + filters.maxRating; }
    if (filters.watchProviders) {
      p += '&watchProviders=' + encodeURIComponent(filters.watchProviders) + '&watchRegion=' + encodeURIComponent(REGION);
    }
    if (filters.originalLanguage) { p += '&originalLanguage=' + encodeURIComponent(filters.originalLanguage); }
    if (filters.originCountry) { p += '&originCountry=' + encodeURIComponent(filters.originCountry); }
    if (filters.personId) { p += '&withPeople=' + filters.personId; }
    return p;
  }

  function providerRowPath(id) {
    return baseDiscover() + '&sortBy=popularity&page=1&watchProviders=' + id + '&watchRegion=' + encodeURIComponent(REGION);
  }

  function feedEl() { return document.getElementById('jcFeed'); }

  function appendGridBlock(page) {
    var grid = document.getElementById('jcGridMain');
    // First page only: show the shape of what is loading. Later pages append below what is already
    // on screen, where a placeholder would just be noise.
    if (grid && grid.childElementCount === 0) {
      grid.appendChild(lib.buildSkeletons(document, 'card', 12));
    }

    return apiGet(pagePath(page)).then(function (items) {
      lib.clearSkeletons(grid);
      if (!items || items.length === 0) {
        feedExhausted = true;
        if (grid && grid.childElementCount === 0 && !feedEl().querySelector('.jellycrowd-row')) {
          setMessage(t('no_results'));
        }
        return;
      }
      setMessage('');
      items.forEach(function (item) { grid.appendChild(renderCard(item)); });
    }).catch(function (e) {
      lib.clearSkeletons(grid);
      feedExhausted = true;
      if (page === 1) { showError(e); }
    });
  }

  function appendRowBlock(def) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-row';
    var header = document.createElement('div');
    header.className = 'jellycrowd-row-header';
    var title = document.createElement('h3');
    title.className = 'jellycrowd-row-title';
    title.textContent = def.title;
    header.appendChild(title);
    // "See more →" jumps to the full grid filtered to this row (rows that map to a discover filter).
    if (def.apply) {
      var more = document.createElement('button');
      more.type = 'button';
      more.className = 'jellycrowd-row-more';
      more.textContent = t('see_more');
      more.addEventListener('click', function () { def.apply(); resetFeed(); });
      header.appendChild(more);
    }
    section.appendChild(header);
    var strip = document.createElement('div');
    strip.className = 'jellycrowd-row-strip';
    section.appendChild(strip);
    feedEl().appendChild(section);

    if (def.kind === 'platforms') {
      platformList().forEach(function (platform) {
        var chip = document.createElement('button');
        chip.type = 'button';
        chip.className = 'jellycrowd-provider-chip';
        if (platform.LogoPath) {
          var logo = document.createElement('img');
          logo.className = 'jellycrowd-provider-logo';
          logo.loading = 'lazy';
          logo.alt = platform.Name || '';
          logo.src = PROVIDER_LOGO_BASE + platform.LogoPath;
          chip.appendChild(logo);
        }
        var label = document.createElement('span');
        label.textContent = platform.Name;
        chip.appendChild(label);
        chip.addEventListener('click', function () {
          filters.watchProviders = String(platform.Id);
          resetFeed();
        });
        strip.appendChild(chip);
      });
      return;
    }

    apiGet(def.path)
      .then(function (items) {
        if (!items || items.length === 0) { section.remove(); return; }
        items.slice(0, 20).forEach(function (item) { strip.appendChild(renderCard(item)); });
      })
      .catch(function () { section.remove(); });
  }

  function buildRowQueue() {
    var sciFi = filters.mediaType === 'tv' ? '10765' : '878';
    // "For you" first (auto-removed when there are no recommendations / seeds). No "see more": the
    // recommendations endpoint isn't a discover filter we can paginate as a grid.
    var queue = [
      { kind: 'path', title: t('row_popular'), path: 'JellyCrowd/Catalog/Popular?language=' + encodeURIComponent(fullLocale()) },
      { kind: 'path', title: t('row_foryou'), path: 'JellyCrowd/Catalog/Recommendations?language=' + encodeURIComponent(fullLocale()) }
    ];
    queue.push({ kind: 'platforms', title: t('streaming_platforms') });
    platformList().slice(0, 4).forEach(function (platform) {
      queue.push({
        kind: 'path',
        title: platform.Name,
        path: providerRowPath(platform.Id),
        apply: function () { filters.watchProviders = String(platform.Id); }
      });
    });
    queue.push({
      kind: 'path',
      title: t('row_toprated'),
      path: baseDiscover() + '&sortBy=rating&page=1',
      apply: function () { filters.sortBy = 'rating'; }
    });
    queue.push({
      kind: 'path',
      title: t('row_scifi'),
      path: baseDiscover() + '&sortBy=popularity&page=1&genres=' + sciFi,
      apply: function () { filters.genres = [sciFi]; }
    });
    return queue;
  }

  function sentinelVisible() {
    var rect = document.getElementById('jcSentinel').getBoundingClientRect();
    return rect.top < (window.innerHeight || document.documentElement.clientHeight) + 400;
  }

  function loadNext() {
    if (feedLoading || feedExhausted) {
      return;
    }
    feedLoading = true;
    gridPage++;
    appendGridBlock(gridPage).then(function () {
      feedLoading = false;
      if (!feedExhausted && sentinelVisible()) {
        loadNext();
      }
    });
  }

  function ensureObserver() {
    if (feedObserver) {
      return;
    }
    feedObserver = new IntersectionObserver(function (entries) {
      if (entries[0].isIntersecting) {
        loadNext();
      }
    }, { rootMargin: '400px' });
    feedObserver.observe(document.getElementById('jcSentinel'));
  }

  function resetFeed() {
    feedLoading = false;
    feedExhausted = false;
    gridPage = 0;
    feedEl().innerHTML = '';
    renderActiveFilters();

    // "My list" mode: render the user's watchlist entries (no TMDB query / infinite scroll).
    if (showWatchlist) {
      feedExhausted = true;
      document.getElementById('jcSectionTitle').textContent = t('watchlist_title');
      var listGrid = document.createElement('div');
      listGrid.className = 'jellycrowd-grid';
      listGrid.id = 'jcGridMain';
      feedEl().appendChild(listGrid);
      if (!watchlistEntries.length) {
        setMessage(t('watchlist_empty'));
      } else {
        setMessage('');
        watchlistEntries.forEach(function (entry) { listGrid.appendChild(renderCard(entry)); });
      }
      return;
    }

    var rows;
    if (searchQuery) {
      document.getElementById('jcSectionTitle').textContent = t('results_title');
      rows = [];
    } else if (filters.personId) {
      document.getElementById('jcSectionTitle').textContent = t('filmography_of') + ' ' + filters.personName;
      rows = [];
    } else {
      document.getElementById('jcSectionTitle').textContent = t('browse_title');
      rows = hasActiveFilters() ? [] : buildRowQueue();
    }

    // Category rows are rendered once, up top; the grid below is a single element that all pages fill.
    rows.forEach(function (def) { appendRowBlock(def); });
    var grid = document.createElement('div');
    grid.className = 'jellycrowd-grid';
    grid.id = 'jcGridMain';
    feedEl().appendChild(grid);

    setMessage(t('loading'));
    ensureObserver();
    loadNext();
  }

  function search(query) {
    searchQuery = query || '';
    if (!searchQuery) {
      filters.watchProviders = '';
    } else {
      // A search is a fresh intent: leave any active person/filmography filter.
      filters.personId = 0;
      filters.personName = '';
    }
    resetFeed();
  }

  // Renders a removable chip per active filter above the feed, so the user can always see what's
  // narrowing the catalog and clear any single one (the X) — the Reset chip clears everything at once.
  function renderActiveFilters() {
    var bar = document.getElementById('jcActiveFilters');
    if (!bar) { return; }
    bar.innerHTML = '';
    var chips = [];

    if (showWatchlist) {
      chips.push({ label: '★ ' + t('watchlist_title'), clear: function () {
        showWatchlist = false;
        var ml = document.getElementById('jcMyList');
        if (ml) { ml.classList.remove('jellycrowd-chip-active'); }
      } });
    }
    if (searchQuery) {
      chips.push({ label: '“' + searchQuery + '”', clear: function () {
        searchQuery = '';
        var input = document.getElementById('jcSearchInput');
        if (input) { input.value = ''; }
      } });
    }
    if (filters.personId) {
      chips.push({ label: '👤 ' + (filters.personName || t('filmography_of')), clear: function () {
        filters.personId = 0;
        filters.personName = '';
      } });
    }
    filters.genres.forEach(function (gid) {
      var match = loadedGenres.filter(function (g) { return String(g.Id) === String(gid); })[0];
      chips.push({ label: match ? match.Name : ('#' + gid), clear: function () {
        var idx = filters.genres.indexOf(gid);
        if (idx >= 0) { filters.genres.splice(idx, 1); }
        renderGenres(loadedGenres);
      } });
    });
    if (filters.minYear > MIN_YEAR || filters.maxYear < MAX_YEAR) {
      chips.push({ label: filters.minYear + '–' + filters.maxYear, clear: function () {
        filters.minYear = MIN_YEAR;
        filters.maxYear = MAX_YEAR;
        setupYearSlider();
      } });
    }
    if (filters.minRating > 0 || filters.maxRating < 10) {
      chips.push({ label: '★ ' + filters.minRating + '–' + filters.maxRating, clear: function () {
        filters.minRating = 0;
        filters.maxRating = 10;
        setupRatingSlider();
      } });
    }
    if (filters.sortBy && filters.sortBy !== 'popularity') {
      chips.push({ label: t('filters_sort'), clear: function () {
        filters.sortBy = 'popularity';
        var sort = document.getElementById('jcSort');
        if (sort) { sort.value = 'popularity'; }
      } });
    }
    if (filters.watchProviders) {
      chips.push({ label: t('streaming_platforms'), clear: function () { filters.watchProviders = ''; } });
    }
    if (filters.originalLanguage) {
      chips.push({ label: t('filters_language'), clear: function () {
        filters.originalLanguage = '';
        var lang = document.getElementById('jcLang');
        if (lang) { lang.value = ''; }
      } });
    }
    if (filters.originCountry) {
      chips.push({ label: t('filters_country'), clear: function () {
        filters.originCountry = '';
        var country = document.getElementById('jcCountry');
        if (country) { country.value = ''; }
      } });
    }

    if (!chips.length) {
      bar.hidden = true;
      return;
    }
    bar.hidden = false;
    chips.forEach(function (c) {
      var chip = document.createElement('span');
      chip.className = 'jellycrowd-active-chip';
      var label = document.createElement('span');
      label.textContent = c.label;
      chip.appendChild(label);
      var x = document.createElement('button');
      x.type = 'button';
      x.className = 'jellycrowd-active-chip-x';
      x.textContent = '✕';
      x.setAttribute('aria-label', t('filters_reset') + ': ' + c.label);
      x.addEventListener('click', function () { c.clear(); resetFeed(); });
      chip.appendChild(x);
      bar.appendChild(chip);
    });
  }

  // ---------- filters UI ----------

  function dualSlider(container, opts) {
    container.innerHTML = '';
    var track = document.createElement('div');
    track.className = 'jellycrowd-dual-track';
    var fill = document.createElement('div');
    fill.className = 'jellycrowd-dual-fill';

    function makeInput(value) {
      var input = document.createElement('input');
      input.type = 'range';
      input.min = String(opts.min);
      input.max = String(opts.max);
      input.step = String(opts.step);
      input.value = String(value);
      return input;
    }

    var low = makeInput(opts.low);
    var high = makeInput(opts.high);

    var values = document.createElement('div');
    values.className = 'jellycrowd-dual-values';
    var lowLabel = document.createElement('span');
    var highLabel = document.createElement('span');
    values.appendChild(lowLabel);
    values.appendChild(highLabel);

    container.appendChild(track);
    container.appendChild(fill);
    container.appendChild(low);
    container.appendChild(high);
    container.appendChild(values);

    function pct(v) {
      return ((v - opts.min) / (opts.max - opts.min)) * 100;
    }
    function refresh(fire) {
      var pair = lib.orderPair(Number(low.value), Number(high.value));
      fill.style.left = pct(pair[0]) + '%';
      fill.style.width = (pct(pair[1]) - pct(pair[0])) + '%';
      lowLabel.textContent = opts.format(pair[0]);
      highLabel.textContent = opts.format(pair[1]);
      if (fire) {
        opts.onChange(pair[0], pair[1]);
      }
    }
    low.addEventListener('input', function () { refresh(false); });
    high.addEventListener('input', function () { refresh(false); });
    low.addEventListener('change', function () { refresh(true); });
    high.addEventListener('change', function () { refresh(true); });
    refresh(false);
  }

  function setupYearSlider() {
    dualSlider(document.getElementById('jcYear'), {
      min: MIN_YEAR, max: MAX_YEAR, step: 1, low: filters.minYear, high: filters.maxYear,
      format: function (v) { return String(Math.round(v)); },
      onChange: function (lo, hi) { filters.minYear = Math.round(lo); filters.maxYear = Math.round(hi); resetFeed(); }
    });
  }

  function setupRatingSlider() {
    dualSlider(document.getElementById('jcRating'), {
      min: 0, max: 10, step: 0.5, low: filters.minRating, high: filters.maxRating,
      format: function (v) { return Number(v).toFixed(1); },
      onChange: function (lo, hi) { filters.minRating = lo; filters.maxRating = hi; resetFeed(); }
    });
  }

  function buildSort() {
    var select = document.getElementById('jcSort');
    select.innerHTML = '';
    [['popularity', 'sort_popularity'], ['rating', 'sort_rating'], ['release', 'sort_release']].forEach(function (pair) {
      var opt = document.createElement('option');
      opt.value = pair[0];
      opt.textContent = t(pair[1]);
      select.appendChild(opt);
    });
    select.value = filters.sortBy;
    select.addEventListener('change', function () { filters.sortBy = select.value; resetFeed(); });
  }

  function buildLanguageCountryFilters() {
    var langSelect = document.getElementById('jcLang');
    fillCodeSelect(langSelect, FILTER_LANGUAGES, 'language', filters.originalLanguage);
    langSelect.addEventListener('change', function () { filters.originalLanguage = langSelect.value; resetFeed(); });

    var countrySelect = document.getElementById('jcCountry');
    fillCodeSelect(countrySelect, FILTER_COUNTRIES, 'region', filters.originCountry);
    countrySelect.addEventListener('change', function () { filters.originCountry = countrySelect.value; resetFeed(); });
  }

  function renderGenres(genres) {
    loadedGenres = genres || [];
    var container = document.getElementById('jcGenres');
    container.innerHTML = '';
    (genres || []).forEach(function (genre) {
      var chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'jellycrowd-chip';
      if (filters.genres.indexOf(String(genre.Id)) >= 0) { chip.classList.add('jellycrowd-chip-active'); }
      chip.textContent = genre.Name;
      chip.addEventListener('click', function () {
        var id = String(genre.Id);
        var idx = filters.genres.indexOf(id);
        if (idx >= 0) {
          filters.genres.splice(idx, 1);
          chip.classList.remove('jellycrowd-chip-active');
        } else {
          filters.genres.push(id);
          chip.classList.add('jellycrowd-chip-active');
        }
        resetFeed();
      });
      container.appendChild(chip);
    });
  }

  function loadGenres() {
    apiGet('JellyCrowd/Catalog/Genres/' + filters.mediaType + '?language=' + encodeURIComponent(fullLocale()))
      .then(renderGenres)
      .catch(function () { renderGenres([]); });
  }

  function setMediaType(type) {
    // Switching the browse type leaves any filmography view (a person filter is movie-only, so keeping
    // it while flipping to Séries would wrongly return the unfiltered catalog).
    var leavingPerson = !!filters.personId;
    if (filters.mediaType === type && !leavingPerson) {
      return;
    }
    filters.personId = 0;
    filters.personName = '';
    filters.mediaType = type;
    filters.genres = [];
    filters.watchProviders = '';
    document.getElementById('jcTypeMovie').classList.toggle('jellycrowd-chip-active', type === 'movie');
    document.getElementById('jcTypeTv').classList.toggle('jellycrowd-chip-active', type === 'tv');
    loadGenres();
    loadProviders().then(resetFeed);
  }

  function resetFilters() {
    filters.genres = [];
    filters.minYear = MIN_YEAR;
    filters.maxYear = MAX_YEAR;
    filters.minRating = 0;
    filters.maxRating = 10;
    filters.sortBy = 'popularity';
    filters.watchProviders = '';
    filters.originalLanguage = '';
    filters.originCountry = '';
    filters.personId = 0;
    filters.personName = '';
    searchQuery = '';
    showWatchlist = false;
    document.getElementById('jcMyList').classList.remove('jellycrowd-chip-active');
    document.getElementById('jcSort').value = 'popularity';
    document.getElementById('jcLang').value = '';
    document.getElementById('jcCountry').value = '';
    document.getElementById('jcSearchInput').value = '';
    setupYearSlider();
    setupRatingSlider();
    loadGenres();
    resetFeed();
  }

  // Apply the movies/series scope to the type toggle: hide a disabled type, lock the media type when only
  // one is offered, and hide the whole type filter when there's no choice to make.
  function applyMediaTypeScope() {
    var movieTab = document.getElementById('jcTypeMovie');
    var tvTab = document.getElementById('jcTypeTv');
    var typeFilter = document.getElementById('jcFilterType');
    if (movieTab) { movieTab.style.display = reqScope.movies ? '' : 'none'; }
    if (tvTab) { tvTab.style.display = reqScope.series ? '' : 'none'; }
    if (!reqScope.movies) { filters.mediaType = 'tv'; }
    else if (!reqScope.series) { filters.mediaType = 'movie'; }
    if (typeFilter && (!reqScope.movies || !reqScope.series)) { typeFilter.style.display = 'none'; }
    if (movieTab) { movieTab.classList.toggle('jellycrowd-chip-active', filters.mediaType === 'movie'); }
    if (tvTab) { tvTab.classList.toggle('jellycrowd-chip-active', filters.mediaType === 'tv'); }
  }

  // Child accounts: hide free-text search (they browse the age-filtered catalog only). The catalog feed
  // and reviews are already gated server-side / by commentsEnabled.
  function applyChildScope() {
    if (!isChild) { return; }
    var form = document.getElementById('jcSearchForm');
    if (form) { form.style.display = 'none'; }
  }

  // Filter the catalog by a TMDB person (cast/director click): show that person's filmography.
  function applyPersonFilter(personId, personName) {
    if (!personId) { return; }
    // Filmography lands on the Movies tab (TMDB has no person filter for TV); with movies off, there's
    // nowhere to show it, so the cast/director click is a no-op.
    if (!reqScope.movies) { return; }
    filters.personId = personId;
    filters.personName = personName || '';
    // A filmography is movies (TMDB has no person filter for TV), so land on the Movies tab: the click
    // can come from a show's cast, and leaving the tab on Séries would look like it did nothing.
    filters.mediaType = 'movie';
    var movieTab = document.getElementById('jcTypeMovie');
    var tvTab = document.getElementById('jcTypeTv');
    if (movieTab) { movieTab.classList.add('jellycrowd-chip-active'); }
    if (tvTab) { tvTab.classList.remove('jellycrowd-chip-active'); }
    searchQuery = '';
    showWatchlist = false;
    var ml = document.getElementById('jcMyList');
    if (ml) { ml.classList.remove('jellycrowd-chip-active'); }
    var input = document.getElementById('jcSearchInput');
    if (input) { input.value = ''; }
    // Close any open detail modal, and make sure the catalog view is the one showing (the popup can be
    // opened from the My requests view too).
    var ov = document.querySelector('.jellycrowd-modal-overlay');
    if (ov) { ov.remove(); }
    if (typeof window.jellyCrowdShowView === 'function') { window.jellyCrowdShowView('catalog'); }
    resetFeed();
  }

  function applyStaticText() {
    document.getElementById('jcLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
    document.getElementById('jcTitle').textContent = t('app_title');
    document.getElementById('jcSearchInput').placeholder = t('search_placeholder');
    document.getElementById('jcSearchButton').textContent = t('search_button');
    document.getElementById('jcLabelType').textContent = t('filters_type');
    document.getElementById('jcTypeMovie').textContent = t('type_movies');
    document.getElementById('jcTypeTv').textContent = t('type_shows');
    document.getElementById('jcLabelGenres').textContent = t('filters_genres');
    document.getElementById('jcLabelYear').textContent = t('filters_year');
    document.getElementById('jcLabelRating').textContent = t('filters_rating');
    document.getElementById('jcLabelSort').textContent = t('filters_sort');
    document.getElementById('jcLabelLang').textContent = t('filters_language');
    document.getElementById('jcLabelCountry').textContent = t('filters_country');
    document.getElementById('jcMyList').textContent = t('watchlist_title');
    document.getElementById('jcReset').textContent = t('filters_reset');
  }

  function init() {
    loadConfigLang().then(loadStrings).then(loadAdmin).then(loadScope).then(function () {
      applyStaticText();
      applyMediaTypeScope();
      applyChildScope();
      buildSort();
      buildLanguageCountryFilters();
      setupYearSlider();
      setupRatingSlider();

      document.getElementById('jcTypeMovie').addEventListener('click', function () { setMediaType('movie'); });
      document.getElementById('jcTypeTv').addEventListener('click', function () { setMediaType('tv'); });
      document.getElementById('jcReset').addEventListener('click', resetFilters);
      document.getElementById('jcMyList').addEventListener('click', function () {
        showWatchlist = !showWatchlist;
        document.getElementById('jcMyList').classList.toggle('jellycrowd-chip-active', showWatchlist);
        resetFeed();
      });

      document.getElementById('jcSearchForm').addEventListener('submit', function (e) {
        e.preventDefault();
        search(document.getElementById('jcSearchInput').value.trim());
      });

      apiGet('JellyCrowd/Quota/Me')
        .then(function (q) {
          quotaExceeded = lib.quotaFull(q);
        })
        .catch(function () { /* quota check is best-effort */ })
        .then(loadWatchlist)
        .then(loadProviders)
        .then(function () {
          loadGenres();
          resetFeed();
        });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  // Let other views (e.g. My requests) open this media-detail popup.
  if (typeof window.jellyCrowdRegisterDetailOpener === 'function') {
    window.jellyCrowdRegisterDetailOpener(openModal);
  }
})();
