/*
 * Jelly Crowd — "My media" page. Lists the user's available titles and lets them request deletion
 * (the media is removed from disk later by the scheduled task, after the admin-configured retention).
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w154';
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';

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

  // Navigate to a library item's Jellyfin details page (overlay auto-closes on hashchange).
  function openInJellyfin(itemId) {
    var serverId = (window.ApiClient && typeof window.ApiClient.serverId === 'function') ? window.ApiClient.serverId() : '';
    var hash = lib.jellyfinDetailsHash(itemId, serverId);
    if (hash) {
      window.location.hash = hash;
    }
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
        throw new Error('HTTP ' + r.status);
      }
      return r.json();
    });
  }

  function apiPost(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path) });
    }
    return fetch(pluginUrl(path), { method: 'POST' }).then(function (r) {
      if (!r.ok) {
        throw new Error('HTTP ' + r.status);
      }
      return r;
    });
  }

  function loadStrings() {
    return fetch(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .catch(function () { return {}; })
      .then(function (loaded) { strings = loaded || {}; });
  }

  function setMessage(text) {
    var el = document.getElementById('jcMediaMessage');
    if (text) {
      el.textContent = text;
      el.hidden = false;
    } else {
      el.hidden = true;
    }
  }

  function flaggedBadge(text) {
    var flagged = document.createElement('span');
    flagged.className = 'jellycrowd-status jellycrowd-status-denied';
    flagged.textContent = text || t('deletion_requested');
    return flagged;
  }

  // Badge text for a deletion-flagged item: "Deletes in 3d 4h", or an imminent label past the deadline.
  function deletionText(item) {
    var countdown = item.DeletionAt ? lib.deletionCountdown(new Date(item.DeletionAt).getTime(), Date.now()) : '';
    return countdown ? (t('deletes_in') + ' ' + countdown) : t('deletion_imminent');
  }

  function renderRow(item) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-request-row';

    if (item.PosterPath) {
      var poster = document.createElement('img');
      poster.className = 'jellycrowd-request-poster';
      poster.loading = 'lazy';
      poster.alt = item.Title || '';
      poster.src = POSTER_BASE + item.PosterPath;
      row.appendChild(poster);
    } else {
      var empty = document.createElement('div');
      empty.className = 'jellycrowd-request-poster';
      row.appendChild(empty);
    }

    var main = document.createElement('div');
    main.className = 'jellycrowd-request-main';
    var titleEl = document.createElement('div');
    titleEl.className = 'jellycrowd-request-title';
    titleEl.textContent = lib.formatTitle(item) + (item.Season ? ' · S' + item.Season : '');
    if (item.JellyfinItemId) {
      titleEl.classList.add('jellycrowd-link');
      titleEl.title = t('open_in_jellyfin');
      titleEl.addEventListener('click', function () { openInJellyfin(item.JellyfinItemId); });
    }
    main.appendChild(titleEl);
    row.appendChild(main);

    var size = document.createElement('span');
    size.className = 'jellycrowd-status jellycrowd-size';
    size.textContent = lib.formatBytes(item.SizeBytes || 0);
    row.appendChild(size);

    if (item.DeletionRequestedAt) {
      row.appendChild(flaggedBadge(deletionText(item)));
      var keep = document.createElement('button');
      keep.className = 'jellycrowd-request';
      keep.type = 'button';
      keep.textContent = t('keep_media');
      keep.addEventListener('click', function () {
        keep.disabled = true;
        apiPost('JellyCrowd/Requests/' + item.RequestId + '/CancelDeletion')
          .then(function () { reloadMedia(); })
          .catch(function () { keep.disabled = false; });
      });
      row.appendChild(keep);
    } else {
      // Expiry countdown ("Expires in 12d") + a one-click renew (re-claim) that resets it.
      if (item.ExpiresAt) {
        var exp = document.createElement('span');
        exp.className = 'jellycrowd-status jellycrowd-status-scheduled';
        var left = lib.deletionCountdown(new Date(item.ExpiresAt).getTime(), Date.now());
        exp.textContent = left ? (t('expires_in') + ' ' + left) : t('expires_in') + ' <1d';
        row.appendChild(exp);

        var renew = document.createElement('button');
        renew.className = 'jellycrowd-request';
        renew.type = 'button';
        renew.textContent = t('renew_ownership');
        renew.addEventListener('click', function () {
          renew.disabled = true;
          apiPost('JellyCrowd/Requests/Claim', { TmdbId: item.TmdbId, MediaType: item.MediaType, Title: item.Title, PosterPath: item.PosterPath })
            .then(function () { reloadMedia(); })
            .catch(function () { renew.disabled = false; });
        });
        row.appendChild(renew);
      }

      var button = document.createElement('button');
      button.className = 'jellycrowd-request';
      button.type = 'button';
      button.textContent = t('request_deletion');
      button.addEventListener('click', function () {
        button.disabled = true;
        apiPost('JellyCrowd/Requests/' + item.RequestId + '/RequestDeletion')
          .then(function () {
            row.removeChild(button);
            row.appendChild(flaggedBadge());
          })
          .catch(function () { button.disabled = false; });
      });
      row.appendChild(button);
    }

    return row;
  }

  function renderQuota(info) {
    var el = document.getElementById('jcMediaQuota');
    if (!el) {
      return;
    }
    el.innerHTML = '';
    if (!info) {
      return;
    }

    var unlimited = info.Unlimited || info.QuotaBytes <= 0;
    var label = document.createElement('div');
    label.className = 'jellycrowd-quota-label';
    label.style.color = '#fff';
    label.textContent = t('quota_storage') + ' : ' + lib.formatBytes(info.UsedBytes)
      + ' / ' + (unlimited ? t('quota_unlimited') : lib.formatBytes(info.QuotaBytes));
    el.appendChild(label);

    if (!unlimited) {
      var percent = lib.quotaPercent(info.UsedBytes, info.QuotaBytes);
      var track = document.createElement('div');
      track.className = 'jellycrowd-quota-track';
      var fill = document.createElement('div');
      fill.className = 'jellycrowd-quota-fill';
      fill.style.width = percent + '%';
      fill.style.background = lib.quotaColor(percent);
      track.appendChild(fill);
      el.appendChild(track);
    }
  }

  // ---------- N29: expandable series → season → episode tree with per-level deletion ----------

  function postAll(ids, action) {
    return Promise.all(ids.map(function (id) { return apiPost('JellyCrowd/Requests/' + id + '/' + action); }));
  }

  // Builds the right-hand action cell for a tree node covering `items`: a Keep button + countdown when
  // every covered request is already flagged for deletion, otherwise a Delete button (label per level).
  function nodeActions(items, deleteLabel) {
    var wrap = document.createElement('span');
    wrap.className = 'jellycrowd-season-actions';
    var ids = items.map(function (i) { return i.RequestId; });
    var allFlagged = items.length > 0 && items.every(function (i) { return i.DeletionRequestedAt; });

    if (allFlagged) {
      var flaggedItem = items.find(function (i) { return i.DeletionAt; }) || items[0];
      wrap.appendChild(flaggedBadge(deletionText(flaggedItem)));
      var keep = document.createElement('button');
      keep.className = 'jellycrowd-request';
      keep.type = 'button';
      keep.textContent = t('keep_media');
      keep.addEventListener('click', function () {
        keep.disabled = true;
        postAll(ids, 'CancelDeletion').then(reloadMedia).catch(function () { keep.disabled = false; });
      });
      wrap.appendChild(keep);
    } else {
      var del = document.createElement('button');
      del.className = 'jellycrowd-request';
      del.type = 'button';
      del.textContent = deleteLabel;
      del.addEventListener('click', function () {
        del.disabled = true;
        // Only flag the still-active (not-already-flagged) requests under this node.
        var active = items.filter(function (i) { return !i.DeletionRequestedAt; }).map(function (i) { return i.RequestId; });
        postAll(active.length ? active : ids, 'RequestDeletion').then(reloadMedia).catch(function () { del.disabled = false; });
      });
      wrap.appendChild(del);
    }

    return wrap;
  }

  function caretToggle(onToggle) {
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'jellycrowd-ep-toggle jellycrowd-tree-caret';
    btn.setAttribute('aria-expanded', 'false');
    btn.textContent = '▶';
    btn.addEventListener('click', function () {
      var open = btn.getAttribute('aria-expanded') === 'true';
      btn.setAttribute('aria-expanded', open ? 'false' : 'true');
      btn.textContent = open ? '▶' : '▼';
      onToggle(!open);
    });
    return btn;
  }

  // A series: header (poster, title, total size, delete-series) + collapsible season rows, each with
  // collapsible episode rows. Deletion at any level flags every request it covers.
  function renderSeriesNode(group) {
    var node = document.createElement('div');
    node.className = 'jellycrowd-tree-series';

    var header = document.createElement('div');
    header.className = 'jellycrowd-request-row';

    var childrenBox = document.createElement('div');
    childrenBox.className = 'jellycrowd-tree-children';
    childrenBox.style.display = 'none';
    header.appendChild(caretToggle(function (open) { childrenBox.style.display = open ? '' : 'none'; }));

    if (group.poster) {
      var poster = document.createElement('img');
      poster.className = 'jellycrowd-request-poster';
      poster.loading = 'lazy';
      poster.alt = group.title;
      poster.src = POSTER_BASE + group.poster;
      header.appendChild(poster);
    }

    var main = document.createElement('div');
    main.className = 'jellycrowd-request-main';
    var titleEl = document.createElement('div');
    titleEl.className = 'jellycrowd-request-title';
    titleEl.textContent = group.title;
    if (group.jellyfinItemId) {
      titleEl.classList.add('jellycrowd-link');
      titleEl.title = t('open_in_jellyfin');
      titleEl.addEventListener('click', function () { openInJellyfin(group.jellyfinItemId); });
    }
    main.appendChild(titleEl);
    header.appendChild(main);

    var size = document.createElement('span');
    size.className = 'jellycrowd-status jellycrowd-size';
    size.textContent = lib.formatBytes(group.totalSize || 0);
    header.appendChild(size);

    header.appendChild(nodeActions(group.items, t('delete_series')));
    node.appendChild(header);

    // Season rows (sorted), then episodes under each.
    group.seasons.forEach(function (season) {
      var seasonRow = document.createElement('div');
      seasonRow.className = 'jellycrowd-request-row jellycrowd-tree-season';

      var epBox = document.createElement('div');
      epBox.className = 'jellycrowd-tree-children';
      epBox.style.display = 'none';

      var hasEpisodes = season.episodes.length > 0;
      if (hasEpisodes) {
        seasonRow.appendChild(caretToggle(function (open) { epBox.style.display = open ? '' : 'none'; }));
      } else {
        var spacer = document.createElement('span');
        spacer.className = 'jellycrowd-tree-caret';
        seasonRow.appendChild(spacer);
      }

      var seasonMain = document.createElement('div');
      seasonMain.className = 'jellycrowd-request-main';
      var seasonLabel = document.createElement('div');
      seasonLabel.className = 'jellycrowd-request-title';
      seasonLabel.textContent = t('season_label') + ' ' + season.number;
      seasonMain.appendChild(seasonLabel);
      seasonRow.appendChild(seasonMain);
      var seasonSize = document.createElement('span');
      seasonSize.className = 'jellycrowd-status jellycrowd-size';
      seasonSize.textContent = lib.formatBytes(season.items.reduce(function (s, r) { return s + (r.SizeBytes || 0); }, 0));
      seasonRow.appendChild(seasonSize);
      seasonRow.appendChild(nodeActions(season.items, t('delete_season')));
      childrenBox.appendChild(seasonRow);

      season.episodes.forEach(function (ep) {
        var epRow = document.createElement('div');
        epRow.className = 'jellycrowd-request-row jellycrowd-tree-episode';
        var epMain = document.createElement('div');
        epMain.className = 'jellycrowd-request-main';
        var epLabel = document.createElement('div');
        epLabel.className = 'jellycrowd-request-title';
        epLabel.textContent = t('episode_label') + ' ' + ep.Episode;
        epMain.appendChild(epLabel);
        epRow.appendChild(epMain);
        epRow.appendChild(nodeActions([ep], t('request_deletion')));
        epBox.appendChild(epRow);
      });

      childrenBox.appendChild(epBox);
    });

    node.appendChild(childrenBox);
    return node;
  }

  // Groups a series' flat request list into { number, items, episodes }[] sorted by season number.
  function groupSeasons(items) {
    var map = {};
    items.forEach(function (i) {
      var key = (i.Season == null) ? 0 : i.Season;
      if (!map[key]) { map[key] = { number: key, items: [], episodes: [] }; }
      map[key].items.push(i);
      if (i.Episode != null) { map[key].episodes.push(i); }
    });
    return Object.keys(map)
      .map(function (k) {
        var s = map[k];
        s.episodes.sort(function (a, b) { return (a.Episode || 0) - (b.Episode || 0); });
        return s;
      })
      .sort(function (a, b) { return a.number - b.number; });
  }

  function render(media) {
    var list = document.getElementById('jcMediaList');
    list.innerHTML = '';

    if (!media || media.length === 0) {
      setMessage(t('no_media'));
      return;
    }

    setMessage('');

    // Movies render as flat rows; TV is grouped into a series → season → episode tree.
    var seriesGroups = {};
    var order = [];
    media.forEach(function (item) {
      if (item.MediaType !== 'tv') {
        list.appendChild(renderRow(item));
        return;
      }
      var key = String(item.TmdbId);
      if (!seriesGroups[key]) {
        seriesGroups[key] = { title: lib.formatTitle(item), poster: item.PosterPath, jellyfinItemId: item.JellyfinItemId, items: [] };
        order.push(key);
      }
      seriesGroups[key].items.push(item);
      if (item.JellyfinItemId && !seriesGroups[key].jellyfinItemId) { seriesGroups[key].jellyfinItemId = item.JellyfinItemId; }
    });

    order.forEach(function (key) {
      var g = seriesGroups[key];
      list.appendChild(renderSeriesNode({
        title: g.title,
        poster: g.poster,
        jellyfinItemId: g.jellyfinItemId,
        items: g.items,
        // SizeBytes is now per-season/episode, so the series total is the sum of its rows.
        totalSize: g.items.reduce(function (sum, r) { return sum + (r.SizeBytes || 0); }, 0),
        seasons: groupSeasons(g.items)
      }));
    });
  }

  function reloadMedia() {
    apiGet('JellyCrowd/Quota/Me')
      .then(renderQuota)
      .catch(function () { /* quota bar is best-effort */ });

    apiGet('JellyCrowd/Quota/MyMedia')
      .then(render)
      .catch(function () { setMessage(t('error_generic')); });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      document.getElementById('jcMediaLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
      document.getElementById('jcMediaTitle').textContent = t('my_media_title');
      setMessage(t('loading'));
      reloadMedia();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
