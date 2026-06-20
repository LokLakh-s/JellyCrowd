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

  // Group catalog items by their release date (YYYY-MM-DD), returning date-ascending groups.
  // Items without a parseable date are dropped. Used by the releases calendar.
  function groupByReleaseDate(items) {
    var byDate = {};
    var dates = [];
    (items || []).forEach(function (item) {
      var date = item && item.ReleaseDate ? String(item.ReleaseDate).slice(0, 10) : '';
      if (date.length !== 10) {
        return;
      }
      if (!Object.prototype.hasOwnProperty.call(byDate, date)) {
        byDate[date] = [];
        dates.push(date);
      }
      byDate[date].push(item);
    });
    dates.sort();
    return dates.map(function (date) { return { date: date, items: byDate[date] }; });
  }

  // Build a Monday-first month grid as an array of weeks; each week has 7 entries that are either
  // null (padding) or { day: 1..31, iso: 'YYYY-MM-DD' }. `month` is 0-based (0 = January).
  function buildMonthMatrix(year, month) {
    function pad(n) { return n < 10 ? '0' + n : String(n); }
    var startDow = (new Date(Date.UTC(year, month, 1)).getUTCDay() + 6) % 7; // Mon=0 … Sun=6
    var daysInMonth = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
    var weeks = [];
    var week = [];
    var i;
    for (i = 0; i < startDow; i++) { week.push(null); }
    for (var day = 1; day <= daysInMonth; day++) {
      week.push({ day: day, iso: year + '-' + pad(month + 1) + '-' + pad(day) });
      if (week.length === 7) { weeks.push(week); week = []; }
    }
    if (week.length > 0) {
      while (week.length < 7) { week.push(null); }
      weeks.push(week);
    }
    return weeks;
  }

  // Map a live download state (from the Radarr/Sonarr queue) to its i18n key, or '' when unknown.
  function downloadStateKey(state) {
    var map = {
      queued: 'dl_queued',
      downloading: 'dl_downloading',
      importing: 'dl_importing',
      completed: 'dl_completed',
      warning: 'dl_warning',
      missing: 'dl_missing',
      unreleased: 'dl_unreleased'
    };
    return Object.prototype.hasOwnProperty.call(map, String(state).toLowerCase())
      ? map[String(state).toLowerCase()]
      : '';
  }

  // Compact countdown until a target time, e.g. "3d 4h", "5h 10m", "12m". Returns '' when the target
  // is in the past, missing or invalid (caller then shows an "imminent" label).
  function deletionCountdown(targetMs, nowMs) {
    var diff = Number(targetMs) - Number(nowMs);
    if (!isFinite(diff) || diff <= 0) {
      return '';
    }
    var minutes = Math.floor(diff / 60000);
    var days = Math.floor(minutes / 1440);
    minutes -= days * 1440;
    var hours = Math.floor(minutes / 60);
    minutes -= hours * 60;
    if (days > 0) {
      return days + 'd ' + hours + 'h';
    }
    if (hours > 0) {
      return hours + 'h ' + minutes + 'm';
    }
    return minutes + 'm';
  }

  // From the user's requests, the set of seasons/episodes already actively requested for one title.
  // Returns { seasons: {N: true}, episodes: {'N:M': true} }. Denied requests are ignored.
  function requestedKeys(requests, tmdbId) {
    var seasons = {};
    var episodes = {};
    (requests || []).forEach(function (r) {
      if (!r || r.TmdbId !== tmdbId) {
        return;
      }
      if (r.Status === 2 || r.Status === 'Denied' || r.Status === 'denied') {
        return;
      }
      if (r.Season === null || r.Season === undefined) {
        return;
      }
      if (r.Episode === null || r.Episode === undefined) {
        seasons[r.Season] = true;
      } else {
        episodes[r.Season + ':' + r.Episode] = true;
      }
    });
    return { seasons: seasons, episodes: episodes };
  }

  // Build the Jellyfin web details-page hash for a library item, e.g. "#/details?id=ABC&serverId=XYZ".
  // Returns '' when no item id is given. serverId is optional.
  function jellyfinDetailsHash(itemId, serverId) {
    if (!itemId) {
      return '';
    }
    var hash = '#/details?id=' + encodeURIComponent(itemId);
    if (serverId) {
      hash += '&serverId=' + encodeURIComponent(serverId);
    }
    return hash;
  }

  // Quota fill colour, grading from green (empty) through yellow (half) to red (full).
  // Interpolates the HSL hue 120 -> 0 across 0..100%.
  function quotaColor(percent) {
    var p = Number(percent) || 0;
    if (p < 0) { p = 0; }
    if (p > 100) { p = 100; }
    return 'hsl(' + (120 - p * 1.2) + ', 70%, 45%)';
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
    downloadStateKey: downloadStateKey,
    jellyfinDetailsHash: jellyfinDetailsHash,
    deletionCountdown: deletionCountdown,
    requestedKeys: requestedKeys,
    orderPair: orderPair,
    formatBytes: formatBytes,
    quotaPercent: quotaPercent,
    quotaColor: quotaColor,
    groupByReleaseDate: groupByReleaseDate,
    buildMonthMatrix: buildMonthMatrix
  };
});
