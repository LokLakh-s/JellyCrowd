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

  // Admin-only "request on behalf of" support. When an admin picks a user in a modal, requests made
  // from that modal are created for that user (via Requests/ForUser); empty = the admin themselves.
  var isAdmin = false;
  var adminUsers = [];
  var actAsUserId = null;

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
    originCountry: ''
  };

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
      .then(function (d) { if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); } })
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
    return fetch(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .catch(function () { return {}; })
      .then(function (loaded) { strings = loaded || {}; });
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
    card.addEventListener('click', function () {
      if (item.Available && item.JellyfinItemId) {
        navigateToItem(item.JellyfinItemId);
      } else {
        openModal(item);
      }
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

    if (!item.Available) {
      // The poster button opens the details modal rather than requesting directly: users must see
      // the media details (and pick a season for shows) before sending a request from the modal.
      var button = document.createElement('button');
      button.className = 'jellycrowd-request';
      button.type = 'button';
      button.textContent = t('details_button');
      button.addEventListener('click', function (e) {
        e.stopPropagation();
        openModal(item);
      });
      card.appendChild(button);
    }

    return card;
  }

  // Load whether the current user is an admin (and, if so, the user list) for "request on behalf of".
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
  function submitRequest(payload) {
    if (actAsUserId) {
      var forUser = {};
      Object.keys(payload).forEach(function (k) { forUser[k] = payload[k]; });
      forUser.UserId = actAsUserId;
      return apiPost('JellyCrowd/Requests/ForUser', forUser);
    }
    return apiPost('JellyCrowd/Requests', payload);
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

      // Toggle to reveal the season's episodes (each requestable individually).
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
    content.appendChild(meta);
    meta.appendChild(watchlistStar(item, true));

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
          .catch(function () { /* seasons are best-effort */ });
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
    } else {
      // Already in the library: let the user claim it into their own media (shared ownership).
      var claimBtn = document.createElement('button');
      claimBtn.className = 'jellycrowd-request';
      claimBtn.type = 'button';
      claimBtn.textContent = t('add_to_my_media');
      claimBtn.title = t('claim_quota_warning');
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
        }).catch(function (error) {
          if (error && error.status === 409) { claimBtn.textContent = t('already_yours'); }
          else { claimBtn.disabled = false; }
        });
      });
      content.appendChild(claimBtn);
      var claimNote = document.createElement('div');
      claimNote.className = 'jellycrowd-request-sub';
      claimNote.textContent = t('claim_quota_warning');
      content.appendChild(claimNote);
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
        if (details.Runtime) {
          var rt = document.createElement('span');
          rt.textContent = details.Runtime + ' ' + t('runtime_min');
          meta.appendChild(rt);
        }
        if (details.ImdbId) {
          links.appendChild(externalLink('https://www.imdb.com/title/' + details.ImdbId, t('view_imdb')));
        }
      })
      .catch(function () { /* details are best-effort */ });

    modal.appendChild(close);
    modal.appendChild(body);
    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    function dismiss() {
      overlay.remove();
      document.removeEventListener('keydown', onKey);
    }
    function onKey(e) {
      if (e.key === 'Escape') {
        dismiss();
      }
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
      || !!filters.watchProviders || !!filters.originalLanguage || !!filters.originCountry;
  }

  function baseDiscover() {
    return 'JellyCrowd/Catalog/Discover?mediaType=' + filters.mediaType
      + '&language=' + encodeURIComponent(fullLocale());
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
    return p;
  }

  function providerRowPath(id) {
    return baseDiscover() + '&sortBy=popularity&page=1&watchProviders=' + id + '&watchRegion=' + encodeURIComponent(REGION);
  }

  function feedEl() { return document.getElementById('jcFeed'); }

  function appendGridBlock(page) {
    var grid = document.getElementById('jcGridMain');
    return apiGet(pagePath(page)).then(function (items) {
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
      feedExhausted = true;
      if (page === 1) { showError(e); }
    });
  }

  function appendRowBlock(def) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-row';
    var title = document.createElement('h3');
    title.className = 'jellycrowd-row-title';
    title.textContent = def.title;
    section.appendChild(title);
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
    // "For you" first (auto-removed when there are no recommendations / seeds).
    var queue = [{ kind: 'path', title: t('row_foryou'), path: 'JellyCrowd/Catalog/Recommendations?language=' + encodeURIComponent(fullLocale()) }];
    queue.push({ kind: 'platforms', title: t('streaming_platforms') });
    platformList().slice(0, 4).forEach(function (platform) {
      queue.push({ kind: 'path', title: platform.Name, path: providerRowPath(platform.Id) });
    });
    queue.push({ kind: 'path', title: t('row_toprated'), path: baseDiscover() + '&sortBy=rating&page=1' });
    queue.push({ kind: 'path', title: t('row_scifi'), path: baseDiscover() + '&sortBy=popularity&page=1&genres=' + sciFi });
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
    }
    resetFeed();
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
    var container = document.getElementById('jcGenres');
    container.innerHTML = '';
    (genres || []).forEach(function (genre) {
      var chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'jellycrowd-chip';
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
    if (filters.mediaType === type) {
      return;
    }
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
    loadConfigLang().then(loadStrings).then(loadAdmin).then(function () {
      applyStaticText();
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
          quotaExceeded = !!(q && !q.Unlimited && q.QuotaBytes > 0 && q.UsedBytes >= q.QuotaBytes);
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
})();
