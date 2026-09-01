/*
 * Jelly Crowd — "Preferences" page: how Jelly Crowd reaches this user. The settings used to live three
 * levels deep behind an unlabelled gear inside the bell panel, where nobody found them; they now have a
 * page of their own, reachable from the avatar menu next to Jellyfin's own preferences.
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

  function apiAjax(method, path, body) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      var opts = { type: method, url: pluginUrl(path) };
      if (body !== undefined) {
        opts.data = JSON.stringify(body);
        opts.contentType = 'application/json';
      }
      if (method === 'GET') { opts.dataType = 'json'; }
      return window.ApiClient.ajax(opts);
    }

    var init = { method: method };
    if (body !== undefined) {
      init.headers = { 'Content-Type': 'application/json' };
      init.body = JSON.stringify(body);
    }
    return fetch(pluginUrl(path), init).then(function (r) {
      if (!r.ok) { throw new Error('HTTP ' + r.status); }
      return method === 'GET' ? r.json() : r;
    });
  }

  function apiGet(path) { return apiAjax('GET', path); }

  function loadStrings() {
    return fetch(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .catch(function () { return {}; })
      .then(function (loaded) { strings = loaded || {}; });
  }

  function setMessage(text, isError) {
    var el = document.getElementById('jcPrefsMessage');
    if (!el) { return; }
    if (text) {
      el.textContent = text;
      el.hidden = false;
      el.classList.toggle('jellycrowd-message-error', isError === true);
    } else {
      el.hidden = true;
    }
  }

  // ---------- form building ----------

  function group(titleKey, hintKey) {
    var section = document.createElement('fieldset');
    section.className = 'jellycrowd-prefs-group';
    var legend = document.createElement('legend');
    legend.textContent = t(titleKey);
    section.appendChild(legend);
    if (hintKey) {
      var hint = document.createElement('p');
      hint.className = 'jellycrowd-field-hint';
      hint.textContent = t(hintKey);
      section.appendChild(hint);
    }

    return section;
  }

  function textField(labelKey, type, value, placeholder, hintKey) {
    var wrap = document.createElement('label');
    wrap.className = 'jellycrowd-prefs-field';
    var label = document.createElement('span');
    label.className = 'jellycrowd-prefs-label';
    label.textContent = t(labelKey);
    var input = document.createElement('input');
    input.type = type;
    input.value = value || '';
    input.placeholder = placeholder || '';
    input.className = 'jellycrowd-prefs-input';
    wrap.appendChild(label);
    wrap.appendChild(input);
    if (hintKey) {
      var hint = document.createElement('span');
      hint.className = 'jellycrowd-field-hint';
      hint.textContent = t(hintKey);
      wrap.appendChild(hint);
    }

    return { wrap: wrap, input: input };
  }

  function toggle(labelKey, checked, hintKey) {
    var wrap = document.createElement('label');
    wrap.className = 'jellycrowd-prefs-toggle';
    var box = document.createElement('input');
    box.type = 'checkbox';
    box.checked = !!checked;
    var text = document.createElement('span');
    text.className = 'jellycrowd-prefs-toggle-text';
    var title = document.createElement('span');
    title.textContent = t(labelKey);
    text.appendChild(title);
    if (hintKey) {
      var hint = document.createElement('span');
      hint.className = 'jellycrowd-field-hint';
      hint.textContent = t(hintKey);
      text.appendChild(hint);
    }

    wrap.appendChild(box);
    wrap.appendChild(text);
    return { wrap: wrap, input: box };
  }

  function button(labelKey, variant) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'jellycrowd-prefs-button' + (variant ? ' ' + variant : '');
    b.textContent = t(labelKey);
    return b;
  }

  // ---------- page ----------

  function render(prefs) {
    var form = document.getElementById('jcPrefsForm');
    var p = prefs || {};
    var loadedEmail = p.Email || ''; // the e-mail lives on the Profile screen now; preserved here on save
    form.innerHTML = '';
    // The sub-tab bar goes in the section (full width) rather than inside the max-width form, so it renders
    // on a single centred line exactly like on the native settings pages.
    var section = form.parentNode;
    if (section) {
      var oldBar = section.querySelector('.jcSettingsTabs');
      if (oldBar && oldBar.parentNode) { oldBar.parentNode.removeChild(oldBar); }
      section.insertBefore(buildSettingsBar(), section.firstChild);
    }

    var master = toggle('notif_enabled', p.Enabled !== false, 'prefs_enabled_hint');
    var masterGroup = group('prefs_group_delivery');
    masterGroup.appendChild(master.wrap);

    var ntfy = textField('notif_ntfy_topic', 'text', p.NtfyTopic, 'my-topic', 'prefs_ntfy_hint');
    masterGroup.appendChild(ntfy.wrap);
    form.appendChild(masterGroup);

    // Notifications need a destination: without an e-mail (set on the Profile screen) the master switch is
    // disabled, with a tooltip pointing there. ntfy alone is not enough to flip it on.
    var hasEmail = !!loadedEmail;
    if (!hasEmail) {
      master.input.checked = false;
      master.input.disabled = true;
      master.wrap.title = t('set_email_required');
    }

    var cats = group('notif_categories', 'prefs_categories_hint');
    var unreleased = toggle('notif_cat_unreleased', p.NotifyAvailableUnreleased);
    var released = toggle('notif_cat_released', p.NotifyAvailableReleased);
    var decisions = toggle('notif_cat_decisions', p.NotifyDecisions);
    var quota = toggle('notif_cat_quota', p.NotifyQuotaExpiry);
    [unreleased, released, decisions, quota].forEach(function (c) { cats.appendChild(c.wrap); });
    form.appendChild(cats);

    var actions = document.createElement('div');
    actions.className = 'jellycrowd-prefs-actions';
    var save = button('save', 'jellycrowd-prefs-primary');
    actions.appendChild(save);
    form.appendChild(actions);

    // The whole point of the master switch is that it governs the rest; show that rather than explain it.
    function syncEnabled() {
      var on = master.input.checked && hasEmail;
      [ntfy.input, unreleased.input, released.input, decisions.input, quota.input]
        .forEach(function (el) { el.disabled = !on; });
      cats.classList.toggle('jellycrowd-prefs-off', !on);
    }

    master.input.addEventListener('change', syncEnabled);
    syncEnabled();

    function payload() {
      return {
        Enabled: master.input.checked,
        Email: loadedEmail,
        NtfyTopic: ntfy.input.value.trim(),
        NotifyAvailableUnreleased: unreleased.input.checked,
        NotifyAvailableReleased: released.input.checked,
        NotifyDecisions: decisions.input.checked,
        NotifyQuotaExpiry: quota.input.checked
      };
    }

    save.addEventListener('click', function () {
      save.disabled = true;
      setMessage(t('prefs_saving'));
      apiAjax('POST', 'JellyCrowd/Notifications/Mine/Prefs', payload())
        .then(function () { setMessage(t('saved')); })
        .catch(function () { setMessage(t('prefs_save_failed'), true); })
        .then(function () { save.disabled = false; });
    });
  }

  // Settings sub-tab bar (same look/behaviour as on the native preference pages): navigating to a native
  // screen sets the hash, which closes this overlay and lands there; Notifications is the active tab.
  function buildSettingsBar() {
    var uid = (window.ApiClient && window.ApiClient.getCurrentUserId && window.ApiClient.getCurrentUserId()) || '';
    var q = uid ? ('?userId=' + encodeURIComponent(uid)) : '';
    var bar = document.createElement('div');
    bar.className = 'jcSettingsTabs';

    [
      ['profile', t('avm_profile'), '#/userprofile' + q],
      ['quickconnect', t('avm_quickconnect'), '#/quickconnect' + q],
      ['display', t('avm_display'), '#/mypreferencesdisplay' + q],
      ['playback', t('avm_playback'), '#/mypreferencesplayback' + q],
      ['subtitles', t('avm_subtitles'), '#/mypreferencessubtitles' + q],
      ['controls', t('avm_controls'), '#/mypreferencescontrols' + q],
      ['notifications', t('set_notifications'), null]
    ].forEach(function (tb) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'jcSettingsTab' + (tb[0] === 'notifications' ? ' jcSettingsTab-active' : '');
      b.textContent = tb[1];
      if (tb[2]) { b.addEventListener('click', function () { window.location.hash = tb[2]; }); }
      bar.appendChild(b);
    });
    return bar;
  }

  function load() {
    setMessage(t('loading'));
    return apiGet('JellyCrowd/Notifications/Mine/Prefs')
      .then(function (p) { render(p); setMessage(''); })
      .catch(function () { setMessage(t('error_generic'), true); });
  }

  function init() {
    var logo = document.getElementById('jcPrefsLogo');
    if (logo) { logo.src = pluginUrl('JellyCrowd/Web/logo.png'); }
    loadConfigLang()
      .then(loadStrings)
      .then(function () {
        var title = document.getElementById('jcPrefsTitle');
        if (title) { title.textContent = t('set_notifications'); }
        return load();
      });
  }

  init();
})();
