/*
 * Jelly Crowd — pure, framework-free helpers shared by catalog.js.
 * UMD wrapper so the same file works as a browser global (JellyCrowdLib)
 * and as a CommonJS module under `node --test` (see tests/js/).
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.JellyCrowdLib = factory();
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // Pick the 2-letter catalog language from a full locale, falling back to 'en'.
  function pickLang(locale, supported) {
    var code = String(locale || 'en').slice(0, 2).toLowerCase();
    return supported.indexOf(code) >= 0 ? code : 'en';
  }

  // Resolve the effective 2-letter language: an admin-forced language wins when supported,
  // otherwise 'auto' (or any unsupported value) follows the user's locale.
  function resolveLang(configLang, supported, userLocale) {
    var c = String(configLang || 'auto').toLowerCase();
    if (c !== 'auto' && supported.indexOf(c) >= 0) {
      return c;
    }
    return pickLang(userLocale, supported);
  }

  // Map a 2-letter language to a full TMDB locale; falls back to the user's locale in 'auto' mode.
  function contentLocale(configLang, userLocale) {
    var map = { en: 'en-US', fr: 'fr-FR' };
    var c = String(configLang || 'auto').toLowerCase();
    if (map[c]) {
      return map[c];
    }
    return userLocale || 'en-US';
  }

  // Format a Date (or date-parseable value) as a local 'YYYY-MM-DD' string for <input type="date">.
  function isoDate(value) {
    var d = (value instanceof Date) ? value : new Date(value);
    var month = String(d.getMonth() + 1);
    var day = String(d.getDate());
    return d.getFullYear()
      + '-' + (month.length < 2 ? '0' + month : month)
      + '-' + (day.length < 2 ? '0' + day : day);
  }

  // Extract the 4-digit year from a TMDB date string, or '' when absent.
  function yearOf(item) {
    return item && item.ReleaseDate ? String(item.ReleaseDate).slice(0, 4) : '';
  }

  // "Title (Year)" when a year is known, otherwise just the title.
  function formatTitle(item) {
    var title = (item && item.Title) || '';
    var year = yearOf(item);
    return year ? title + ' (' + year + ')' : title;
  }

  // Map an HTTP error status to the i18n key used for the message.
  function errorKey(status) {
    return status === 503 ? 'error_not_configured' : 'error_generic';
  }

  // Format a TMDB vote average as a one-decimal string, or '' when there is no rating.
  function formatRating(vote) {
    var n = Number(vote);
    return n > 0 ? n.toFixed(1) : '';
  }

  // Map a request status (numeric enum or string, as serialized by the API) to its i18n key.
  function statusLabelKey(status) {
    var map = {
      '0': 'status_pending', 'pending': 'status_pending',
      '1': 'status_approved', 'approved': 'status_approved',
      '2': 'status_denied', 'denied': 'status_denied',
      '3': 'status_available', 'available': 'status_available'
    };
    return map[String(status).toLowerCase()] || 'status_pending';
  }

  // Admin sort order for a request status: Pending, then Approved, then Available, then Denied.
  function statusRank(status) {
    var order = {
      '0': 0, 'pending': 0,
      '1': 1, 'approved': 1,
      '3': 2, 'available': 2,
      '2': 3, 'denied': 3
    };
    var key = String(status).toLowerCase();
    return Object.prototype.hasOwnProperty.call(order, key) ? order[key] : 99;
  }

  // Return [min, max] from two numbers (used to keep dual-slider bounds ordered).
  function orderPair(a, b) {
    var x = Number(a);
    var y = Number(b);
    return x <= y ? [x, y] : [y, x];
  }

  // Human-readable byte size (binary units).
  function formatBytes(bytes) {
    var n = Number(bytes) || 0;
    var units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    var i = 0;
    while (n >= 1024 && i < units.length - 1) {
      n /= 1024;
      i++;
    }
    return (i === 0 ? n : n.toFixed(1)) + ' ' + units[i];
  }

  // Usage percentage clamped to 0..100; 0 when the quota is unlimited (<= 0).
  function quotaPercent(used, quota) {
    var u = Number(used) || 0;
    var q = Number(quota) || 0;
    if (q <= 0) {
      return 0;
    }
    var p = (u / q) * 100;
    if (p < 0) {
      return 0;
    }
    return p > 100 ? 100 : p;
  }

  return {
    pickLang: pickLang,
    resolveLang: resolveLang,
    contentLocale: contentLocale,
    isoDate: isoDate,
    yearOf: yearOf,
    formatTitle: formatTitle,
    formatRating: formatRating,
    errorKey: errorKey,
    statusLabelKey: statusLabelKey,
    statusRank: statusRank,
    orderPair: orderPair,
    formatBytes: formatBytes,
    quotaPercent: quotaPercent
  };
});
