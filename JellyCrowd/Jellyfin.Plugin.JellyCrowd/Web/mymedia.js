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

  function flaggedBadge() {
    var flagged = document.createElement('span');
    flagged.className = 'jellycrowd-status jellycrowd-status-denied';
    flagged.textContent = t('deletion_requested');
    return flagged;
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
      row.appendChild(flaggedBadge());
    } else {
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

  function render(media) {
    var list = document.getElementById('jcMediaList');
    list.innerHTML = '';

    if (!media || media.length === 0) {
      setMessage(t('no_media'));
      return;
    }

    setMessage('');
    media.forEach(function (item) { list.appendChild(renderRow(item)); });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      document.getElementById('jcMediaLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
      document.getElementById('jcMediaTitle').textContent = t('my_media_title');
      setMessage(t('loading'));

      apiGet('JellyCrowd/Quota/Me')
        .then(renderQuota)
        .catch(function () { /* quota bar is best-effort */ });

      apiGet('JellyCrowd/Quota/MyMedia')
        .then(render)
        .catch(function () { setMessage(t('error_generic')); });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
