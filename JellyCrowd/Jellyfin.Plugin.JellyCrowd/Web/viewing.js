/*
 * Jelly Crowd — "Viewing" settings page: the preferred version each request is fetched in, the subtitles
 * added when a request becomes available, and the titles removed from Continue watching (to put back).
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';

  function shortLang() {
    return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US');
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

  function apiAjax(method, path, body) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      var opts = { type: method, url: pluginUrl(path), dataType: 'json' };
      if (body !== undefined) {
        opts.data = JSON.stringify(body);
        opts.contentType = 'application/json';
      }
      if (method !== 'GET' && body === undefined) { delete opts.dataType; }
      return window.ApiClient.ajax(opts);
    }

    var init = { method: method };
    if (body !== undefined) {
      init.headers = { 'Content-Type': 'application/json' };
      init.body = JSON.stringify(body);
    }
    return fetch(pluginUrl(path), init).then(function (r) {
      if (!r.ok) { throw new Error('HTTP ' + r.status); }
      return r.status === 204 ? null : r.json();
    });
  }

  function loadConfigLang() {
    return apiAjax('GET', 'JellyCrowd/Settings/Language')
      .then(function (d) { if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); } })
      .catch(function () { /* keep 'auto' on failure */ });
  }

  function loadStrings() {
    return lib.fetchStrings(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (loaded) { if (loaded) { strings = loaded; } });
  }

  function setMessage(text, isError) {
    var el = document.getElementById('jcViewingMessage');
    if (!el) { return; }
    el.hidden = !text;
    el.textContent = text || '';
    el.classList.toggle('jellycrowd-message-error', isError === true);
  }

  function languageName(code) {
    var key = 'lang_' + code;
    var name = t(key);
    return name === key ? code : name;
  }

  function label(key, dub) {
    return t(key).replace('{language}', languageName(dub));
  }

  function group(titleKey, hintText) {
    var section = document.createElement('fieldset');
    section.className = 'jellycrowd-prefs-group';
    var legend = document.createElement('legend');
    legend.textContent = t(titleKey);
    section.appendChild(legend);
    if (hintText) {
      var hint = document.createElement('p');
      hint.className = 'jellycrowd-field-hint';
      hint.textContent = hintText;
      section.appendChild(hint);
    }
    return section;
  }

  function renderVersion(form, prefs) {
    var g = group('viewing_version_group', t('viewing_version_hint'));
    var radios = [];
    ['', 'original', 'dubbed', 'subtitled'].forEach(function (version) {
      var wrap = document.createElement('label');
      wrap.className = 'jellycrowd-prefs-toggle';
      var input = document.createElement('input');
      input.type = 'radio';
      input.name = 'jcVersion';
      input.value = version;
      input.checked = (prefs.LanguagePreference || '') === version;
      var text = document.createElement('span');
      text.className = 'jellycrowd-prefs-toggle-text';
      text.textContent = label(lib.versionLabelKey(version, prefs.DubLanguage), prefs.DubLanguage);
      wrap.appendChild(input);
      wrap.appendChild(text);
      g.appendChild(wrap);
      radios.push(input);
    });
    var note = document.createElement('p');
    note.className = 'jellycrowd-field-hint';
    note.textContent = t('viewing_playback_note');
    g.appendChild(note);
    form.appendChild(g);
    return function () {
      var picked = radios.filter(function (r) { return r.checked; })[0];
      return picked ? picked.value : '';
    };
  }

  function renderSubtitles(form, prefs) {
    var picked = (prefs.SubtitleLanguages || []).slice();
    var g = group('viewing_subtitles_group', t('viewing_subtitles_hint'));
    var chips = document.createElement('div');
    chips.className = 'jellycrowd-chips';
    var add = document.createElement('select');
    add.className = 'jellycrowd-prefs-input';

    function draw() {
      chips.innerHTML = '';
      picked.forEach(function (code) {
        var chip = document.createElement('button');
        chip.type = 'button';
        chip.className = 'jellycrowd-chip';
        chip.textContent = languageName(code) + ' ×';
        chip.title = t('viewing_subtitles_remove');
        chip.addEventListener('click', function () { picked = lib.toggleSubtitleLanguage(picked, code); draw(); });
        chips.appendChild(chip);
      });
      add.innerHTML = '';
      var placeholder = document.createElement('option');
      placeholder.value = '';
      placeholder.textContent = t('viewing_subtitles_add');
      add.appendChild(placeholder);
      lib.SUBTITLE_LANGUAGES.filter(function (c) { return picked.indexOf(c) < 0; }).forEach(function (code) {
        var opt = document.createElement('option');
        opt.value = code;
        opt.textContent = languageName(code);
        add.appendChild(opt);
      });
      add.disabled = picked.length >= lib.MAX_SUBTITLE_LANGUAGES;
    }

    add.addEventListener('change', function () {
      if (add.value) { picked = lib.toggleSubtitleLanguage(picked, add.value); draw(); }
    });
    draw();
    g.appendChild(chips);
    g.appendChild(add);
    form.appendChild(g);
    return function () { return picked.slice(); };
  }

  function renderRemovals(form, removals) {
    var g = group('viewing_continue_group', t('viewing_continue_hint'));
    var list = document.createElement('div');
    list.className = 'jellycrowd-list';
    function empty() {
      var p = document.createElement('p');
      p.className = 'jellycrowd-field-hint';
      p.textContent = t('viewing_continue_empty');
      list.appendChild(p);
    }
    (removals || []).forEach(function (entry) {
      var row = document.createElement('div');
      row.className = 'jellycrowd-inline-row';
      var name = document.createElement('span');
      name.className = 'jellycrowd-inline-row-title';
      name.textContent = entry.Title || '';
      var back = document.createElement('button');
      back.type = 'button';
      back.className = 'jellycrowd-request';
      back.textContent = t('viewing_continue_restore');
      back.addEventListener('click', function () {
        back.disabled = true;
        apiAjax('POST', 'JellyCrowd/ContinueWatching/Unhide/' + entry.ItemId)
          .then(function () {
            if (row.parentNode) { row.parentNode.removeChild(row); }
            if (!list.querySelector('.jellycrowd-inline-row')) { empty(); }
          })
          .catch(function () { back.disabled = false; setMessage(t('cw_failed'), true); });
      });
      row.appendChild(name);
      row.appendChild(back);
      list.appendChild(row);
    });
    if (!removals || !removals.length) { empty(); }
    g.appendChild(list);
    form.appendChild(g);
  }

  function render(prefs, removals) {
    var form = document.getElementById('jcViewingForm');
    form.innerHTML = '';
    var section = form.parentNode;
    if (section) {
      var oldBar = section.querySelector('.jcSettingsTabs');
      if (oldBar && oldBar.parentNode) { oldBar.parentNode.removeChild(oldBar); }
      var uid = (window.ApiClient && window.ApiClient.getCurrentUserId && window.ApiClient.getCurrentUserId()) || '';
      section.insertBefore(lib.buildSettingsBar(document, t, 'viewing', uid, window.jellyCrowdShowView), section.firstChild);
    }

    var p = prefs || {};
    var readVersion = p.LanguagePreferencesAvailable ? renderVersion(form, p) : null;
    var readSubtitles = p.SubtitleDownloadsAvailable ? renderSubtitles(form, p) : null;
    if (readVersion || readSubtitles) {
      var actions = document.createElement('div');
      actions.className = 'jellycrowd-prefs-actions';
      var save = document.createElement('button');
      save.type = 'button';
      save.className = 'jellycrowd-prefs-button jellycrowd-prefs-primary';
      save.textContent = t('save');
      save.addEventListener('click', function () {
        save.disabled = true;
        setMessage(t('prefs_saving'));
        apiAjax('POST', 'JellyCrowd/Viewing/Mine', {
          LanguagePreference: readVersion ? readVersion() : (p.LanguagePreference || ''),
          SubtitleLanguages: readSubtitles ? readSubtitles() : (p.SubtitleLanguages || [])
        })
          .then(function () { setMessage(t('saved')); })
          .catch(function () { setMessage(t('prefs_save_failed'), true); })
          .then(function () { save.disabled = false; });
      });
      actions.appendChild(save);
      form.appendChild(actions);
    }

    renderRemovals(form, removals);
  }

  function load() {
    setMessage(t('loading'));
    return Promise.all([
      apiAjax('GET', 'JellyCrowd/Viewing/Mine'),
      apiAjax('GET', 'JellyCrowd/ContinueWatching/Hidden').catch(function () { return []; })
    ])
      .then(function (r) { render(r[0], r[1]); setMessage(''); })
      .catch(function () { setMessage(t('error_generic'), true); });
  }

  function init() {
    var logo = document.getElementById('jcViewingLogo');
    if (logo) { logo.src = pluginUrl('JellyCrowd/Web/logo.png'); }
    loadConfigLang()
      .then(loadStrings)
      .then(function () {
        var title = document.getElementById('jcViewingTitle');
        if (title) { title.textContent = t('set_viewing'); }
        if (typeof window.jellyCrowdRegisterRefresh === 'function') {
          window.jellyCrowdRegisterRefresh('viewing', load);
        }
        return load();
      });
  }

  init();
})();
