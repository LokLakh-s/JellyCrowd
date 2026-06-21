/*
 * Jelly Crowd — "My requests" page. Lists the current user's requests with their status.
 * Reuses pure helpers from catalog.lib.js; strings follow the Jellyfin/browser language.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w154';
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';
  var statusTimer = null;        // live status polling interval
  var STATUS_POLL_MS = 2000;
  var lastSignature = null;      // fingerprint of the rendered list, to re-render only on change
  var allowRetry = false;        // admin opt-in: regular users may trigger a manual retry-search
  var isAdmin = false;           // current user is an administrator

  function shortLang() {
    return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US');
  }

  function loadConfigLang() {
    return apiGet('JellyCrowd/Settings/Language')
      .then(function (d) {
        if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); }
        allowRetry = !!(d && d.AllowUserRetrySearch === true);
      })
      .catch(function () { /* keep 'auto' on failure */ });
  }

  function resolveAdmin() {
    return apiGet('JellyCrowd/Settings/Visibility')
      .then(function (d) { isAdmin = !!(d && d.IsAdmin === true); })
      .catch(function () { /* non-admin / unavailable */ });
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
    var el = document.getElementById('jcReqMessage');
    if (text) {
      el.textContent = text;
      el.hidden = false;
    } else {
      el.hidden = true;
    }
  }

  function renderRow(request) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-request-row';
    row.dataset.reqId = request.Id;

    if (request.PosterPath) {
      var poster = document.createElement('img');
      poster.className = 'jellycrowd-request-poster';
      poster.loading = 'lazy';
      poster.alt = request.Title || '';
      poster.src = POSTER_BASE + request.PosterPath;
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
    titleEl.textContent = lib.formatTitle(request)
      + (request.Season ? ' · S' + request.Season : '')
      + (request.Episode ? 'E' + request.Episode : '');
    // Available titles deep-link to their Jellyfin details page (the overlay closes on hashchange).
    var available = (request.Status === 3 || request.Status === 'Available');
    if (available && request.JellyfinItemId) {
      titleEl.classList.add('jellycrowd-link');
      titleEl.title = t('open_in_jellyfin');
      titleEl.addEventListener('click', function () { openInJellyfin(request.JellyfinItemId); });
    }
    main.appendChild(titleEl);

    var dateParts = [];
    if (request.RequestedAt) {
      dateParts.push(t('requested_on') + ' ' + new Date(request.RequestedAt).toLocaleDateString());
    }
    if (available && request.AvailableAt) {
      dateParts.push(t('available_on') + ' ' + new Date(request.AvailableAt).toLocaleDateString());
    }
    if (dateParts.length) {
      var sub = document.createElement('div');
      sub.className = 'jellycrowd-request-sub';
      sub.textContent = dateParts.join(' · ');
      main.appendChild(sub);
    }
    row.appendChild(main);

    var status = document.createElement('span');
    if (request.DeletionRequestedAt) {
      status.className = 'jellycrowd-status jellycrowd-status-denied';
      status.textContent = t('deletion_requested');
    } else {
      var key = lib.statusLabelKey(request.Status);
      status.className = 'jellycrowd-status jellycrowd-status-' + key.replace('status_', '');
      status.textContent = t(key);
    }
    row.appendChild(status);

    // A dispatched-but-still-approved request that carries a backend error is "blocked" (not found /
    // backend issue) — surfaced distinctly from a normal in-progress request, with the reason on hover.
    var approved = (request.Status === 1 || request.Status === 'Approved');
    if (approved && request.DispatchError && !request.DeletionRequestedAt) {
      var blocked = document.createElement('span');
      blocked.className = 'jellycrowd-status jellycrowd-status-denied';
      blocked.textContent = t('status_blocked');
      blocked.title = request.DispatchError;
      row.appendChild(blocked);
    }

    // Show a "scheduled for <date>" hint when fulfillment is deferred to a future (release) date.
    var desired = request.DesiredAt ? new Date(request.DesiredAt) : null;
    if (desired && desired.getTime() > Date.now() && !request.DeletionRequestedAt
        && (request.Status === 0 || request.Status === 'Pending' || request.Status === 1 || request.Status === 'Approved')) {
      var scheduled = document.createElement('span');
      scheduled.className = 'jellycrowd-status jellycrowd-status-scheduled';
      scheduled.textContent = t('scheduled_for') + ' ' + desired.toLocaleDateString();
      row.appendChild(scheduled);
    }

    // Cancellable while pending or approved (an approved request also asks the backend to stop).
    var cancellable = (request.Status === 0 || request.Status === 'Pending'
      || request.Status === 1 || request.Status === 'Approved');
    if (cancellable && !request.DeletionRequestedAt) {
      var cancel = document.createElement('button');
      cancel.type = 'button';
      cancel.className = 'jellycrowd-request';
      cancel.textContent = t('cancel');
      cancel.addEventListener('click', function () {
        cancel.disabled = true;
        apiPost('JellyCrowd/Requests/' + request.Id + '/Cancel')
          .then(function () { row.remove(); })
          .catch(function () { cancel.disabled = false; });
      });
      row.appendChild(cancel);
    }

    // Approved requests can have their search re-triggered (Radarr/Sonarr) — useful when a release was
    // not found yet or the dispatch had failed. Admin-only unless the admin opted users in.
    if (approved && !request.DeletionRequestedAt && (allowRetry || isAdmin)) {
      var retry = document.createElement('button');
      retry.type = 'button';
      retry.className = 'jellycrowd-request';
      retry.textContent = t('retry_search');
      retry.addEventListener('click', function () {
        retry.disabled = true;
        apiPost('JellyCrowd/Requests/' + request.Id + '/Retry')
          .then(function () { lastSignature = ''; tick(); })
          .catch(function () { retry.disabled = false; });
      });
      row.appendChild(retry);
    }

    return row;
  }

  function renderQuota(info) {
    var el = document.getElementById('jcQuota');
    el.innerHTML = '';
    if (!info) {
      return;
    }

    // Clicking the quota opens the "My media" view to manage/delete owned titles.
    el.classList.add('jellycrowd-quota-clickable');
    el.title = t('my_media_title');
    el.onclick = function () {
      if (typeof window.jellyCrowdShowView === 'function') {
        window.jellyCrowdShowView('mymedia');
      } else {
        window.location.hash = '#/userpluginsettings.html?pageUrl=' + encodeURIComponent('/JellyCrowd/Web/mymedia.html');
      }
    };

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
      // Grade green -> yellow -> red by fill level (overrides the static CSS colour).
      fill.style.background = lib.quotaColor(percent);
      track.appendChild(fill);
      el.appendChild(track);
    }
  }

  // Apply live download statuses (from the Radarr/Sonarr queue) onto the matching rows. Stale
  // badges are cleared first so a finished download stops showing progress.
  function applyDownloadStatuses(statuses) {
    var list = document.getElementById('jcReqList');
    if (!list) {
      return;
    }

    Array.prototype.forEach.call(list.querySelectorAll('.jellycrowd-dl'), function (el) { el.remove(); });
    (statuses || []).forEach(function (s) {
      var row = list.querySelector('.jellycrowd-request-row[data-req-id="' + s.RequestId + '"]');
      if (!row) {
        return;
      }

      var key = lib.downloadStateKey(s.State);
      var badge = document.createElement('span');
      badge.className = 'jellycrowd-status jellycrowd-dl jellycrowd-dl-' + s.State;
      var label = key ? t(key) : s.State;
      if (s.State === 'downloading' || s.State === 'importing') {
        label += ' ' + Math.round(s.Percent || 0) + '%';
        // Final size is known up front with debrid (RDT) — show it while downloading.
        if (s.SizeBytes > 0) {
          label += ' · ' + lib.formatBytes(s.SizeBytes);
        }
        if (s.TimeLeft) {
          label += ' · ' + s.TimeLeft;
        }
      }
      badge.textContent = label;
      // Insert before the Cancel button when present, otherwise at the end.
      var cancelBtn = row.querySelector('button.jellycrowd-request');
      row.insertBefore(badge, cancelBtn || null);
    });
  }

  function pollDownloadStatus() {
    // Cache-buster: ApiClient/the browser would otherwise serve a cached GET, freezing the
    // progress until a full page reload.
    apiGet('JellyCrowd/Requests/Mine/DownloadStatus?ts=' + Date.now())
      .then(applyDownloadStatuses)
      .catch(function () { /* best-effort */ });
  }

  function render(requests) {
    var list = document.getElementById('jcReqList');
    list.innerHTML = '';

    if (!requests || requests.length === 0) {
      setMessage(t('no_requests'));
      return;
    }

    setMessage('');
    requests.forEach(function (request) { list.appendChild(renderRow(request)); });
  }

  // Compact fingerprint of the list so the live tick only re-renders when something actually changed
  // (status flip, new/removed request, deletion flag) — avoids rebuilding rows every few seconds.
  function signatureOf(requests) {
    return (requests || []).map(function (r) {
      return r.Id + ':' + r.Status + ':' + (r.DeletionRequestedAt ? 1 : 0);
    }).join('|');
  }

  function refreshQuota() {
    apiGet('JellyCrowd/Quota/Me?ts=' + Date.now())
      .then(renderQuota)
      .catch(function () { /* quota bar is best-effort */ });
  }

  // Live tick: pick up status transitions (e.g. Approved -> Available) without a page reload and
  // refresh download progress. Re-renders only on a real change, to avoid flicker.
  function tick() {
    apiGet('JellyCrowd/Requests/Mine?ts=' + Date.now())
      .then(function (requests) {
        var sig = signatureOf(requests);
        if (sig !== lastSignature) {
          lastSignature = sig;
          render(requests);
          refreshQuota();
        }
        pollDownloadStatus();
      })
      .catch(function () { /* best-effort; keep the current view */ });
  }

  // Full refresh used on first load and whenever the overlay re-shows this view.
  function refresh() {
    refreshQuota();
    apiGet('JellyCrowd/Requests/Mine?ts=' + Date.now())
      .then(function (requests) {
        lastSignature = signatureOf(requests);
        render(requests);
        pollDownloadStatus();
      })
      .catch(function () { setMessage(t('error_generic')); });
  }

  function init() {
    loadConfigLang().then(resolveAdmin).then(loadStrings).then(function () {
      document.getElementById('jcReqLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
      document.getElementById('jcReqTitle').textContent = t('my_requests_title');
      setMessage(t('loading'));

      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('requests', refresh);
      }

      // Poll the live download status while this view is loaded (single interval).
      if (statusTimer) {
        clearInterval(statusTimer);
      }
      statusTimer = setInterval(tick, STATUS_POLL_MS);

      refresh();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
