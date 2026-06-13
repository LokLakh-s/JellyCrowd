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

  function isAvailable(request) {
    return request.Status === 3 || request.Status === 'Available';
  }

  function renderRow(request) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-request-row';

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
    main.textContent = lib.formatTitle(request) + (request.Season ? ' · S' + request.Season : '');
    row.appendChild(main);

    if (request.DeletionRequestedAt) {
      var flagged = document.createElement('span');
      flagged.className = 'jellycrowd-status jellycrowd-status-denied';
      flagged.textContent = t('deletion_requested');
      row.appendChild(flagged);
    } else {
      var button = document.createElement('button');
      button.className = 'jellycrowd-request';
      button.type = 'button';
      button.textContent = t('request_deletion');
      button.addEventListener('click', function () {
        button.disabled = true;
        apiPost('JellyCrowd/Requests/' + request.Id + '/RequestDeletion')
          .then(function () {
            row.removeChild(button);
            var flagged = document.createElement('span');
            flagged.className = 'jellycrowd-status jellycrowd-status-denied';
            flagged.textContent = t('deletion_requested');
            row.appendChild(flagged);
          })
          .catch(function () { button.disabled = false; });
      });
      row.appendChild(button);
    }

    return row;
  }

  function render(requests) {
    var media = (requests || []).filter(isAvailable);
    var list = document.getElementById('jcMediaList');
    list.innerHTML = '';

    if (media.length === 0) {
      setMessage(t('no_media'));
      return;
    }

    setMessage('');
    media.forEach(function (request) { list.appendChild(renderRow(request)); });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      document.getElementById('jcMediaLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
      document.getElementById('jcMediaTitle').textContent = t('my_media_title');
      setMessage(t('loading'));
      apiGet('JellyCrowd/Requests/Mine')
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
