<div align="center">

<img src="logo.png" width="120" alt="Jelly Crowd logo" />

# Jelly Crowd

### ⚡ One plugin to rule them all

A complete **media request & discovery platform** for Jellyfin — catalog, requests, quotas,
notifications, full UI branding and built-in stats — all in one plugin, **no add-ons required**.

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11%2B-00A4DC?logo=jellyfin&logoColor=white)](https://jellyfin.org)
[![Dependencies](https://img.shields.io/badge/dependencies-none-2ea44f)](#-requirements)
[![Self-hosted](https://img.shields.io/badge/distribution-self--hosted-6f42c1)](#-installation)
[![License](https://img.shields.io/badge/license-proprietary-blue)](./LICENSE)

</div>

---

> [!TIP]
> Think **Overseerr + Tautulli + a theme engine**, fused into your Jellyfin UI — requests, quotas,
> statistics and branding, with nothing else to install.

## 📸 Screenshots

<p align="center"><img src="screenshots/catalog.png" alt="Discovery catalog" width="90%" /></p>
<p align="center"><sub><b>Discovery catalog</b> — browse, filter and request, right inside Jellyfin</sub></p>

<p align="center"><img src="screenshots/detail.png" alt="Title detail" width="90%" /></p>
<p align="center"><sub><b>Title detail</b> — cast, seasons, reviews & one-click requests</sub></p>

<p align="center"><img src="screenshots/calendar.png" alt="Release calendar" width="90%" /></p>
<p align="center"><sub><b>Release calendar</b> — what's coming, and when (month / week / day)</sub></p>

<p align="center"><img src="screenshots/dashboard.png" alt="Personal dashboard" width="90%" /></p>
<p align="center"><sub><b>Personal dashboard</b> — watch time, top titles, activity & storage</sub></p>

<p align="center"><img src="screenshots/stats.png" alt="Admin statistics" width="90%" /></p>
<p align="center"><sub><b>Admin statistics</b> — activity chart, top media, most-active users & live now-playing</sub></p>

<p align="center"><img src="screenshots/branding.png" alt="Branding settings" width="90%" /></p>
<p align="center"><sub><b>Branding</b> — logo, favicon, colours, font, layout presets & menu links (URL or upload)</sub></p>

## ✨ Features

### 👤 For your users

- 🎬 **Discovery catalog** — browse, filter and search a TMDB catalog of movies & shows, with rich detail popups (cast, crew, ratings, TMDB/IMDb links).
- 📥 **Media requests** — request whole movies & shows, single seasons or individual episodes, with a desired date for titles that aren't out yet.
- 📡 **Live download status** — *My requests* shows real progress: queued, downloading %/ETA, importing, available.
- ⏭️ **Skip Intro** — a **native** Skip Intro button appears once a show's intro is detected. Opt-in.
- ⏭️ **Skip Outro** — a **native** Skip Outro button on movies & episodes, stopping short of any post-credits scene. Opt-in.
- 🔎 **Search by cast** — search an actor or director's name and get their whole filmography.
- 🔖 **Watchlist & "For you"** — follow titles with ★, and get suggestions based on your requests and watchlist.
- 📅 **Release calendar** — upcoming releases in a month / week / day view.
- 🎞️ **Request a whole saga** — request every missing part of a movie collection at once.
- ⭐ **Ratings & reviews** — rate titles (1–10) and leave reviews, shown anonymously to others.
- 🕓 **Watch history** — a personal, permanent history you can clear or turn off; it's never wiped automatically.
- 🙋 **Personal dashboard** — your watch time, top titles, activity chart, watch-time-by-library and storage.
- 📽️ **Local intros** — a pre-roll video before movies and the first episode of a series. Opt-in.
- 🩹 **Problem reports** — flag an issue on a title (wrong version, missing subtitles…); the admin follows up.
- 🔔 **Notifications** — in-app alerts for your requests, with optional email / ntfy delivery you control.
- 📉 **Storage quota bar** — a live gauge in the header shows how much of your space you've used.

### 🛠️ For admins

- 🤖 **Radarr / Sonarr fulfilment** — approved requests are added and searched automatically (movies via Radarr, shows via Sonarr, TVDB resolved for you).
- 🎨 **UI branding** — restyle the whole Jellyfin UI: logo, favicon, colours, font, background, custom CSS, layout presets and custom left-menu links.
- 📊 **Statistics** — playback analytics: top media & users, a plays / watch-time activity chart, and live *Now playing*.
- ✅ **Approval workflow** — approve or deny from a queue; requesters are notified of every status change.
- ⚙️ **Auto-approval rules** — auto-approve by estimated size, a genre allow-list and trusted-user overrides.
- 🎚️ **Request scope** — run a movies-only or series-only instance, and choose which granularities users can request (whole series, seasons, single episodes).
- 💾 **Per-user quotas** — a storage budget per user, counted only when media becomes available, at its real size.
- 📈 **Adaptive quota** — optionally grow a user's quota as they watch more, with a grace period when they go quiet.
- 🧑‍🤝‍🧑 **User groups** — manage users together: shared settings they inherit, one-click Jellyfin library access, and group-targeted announcements.
- 📢 **Announcements** — post a header banner to everyone or to specific groups.
- ⏳ **Media expiry** — reclaim space by expiring unwatched media after a configurable window.
- 🔀 **Notification channels** — route events to Discord, email, Telegram, ntfy, Gotify, Pushover, Slack or a webhook, per event.
- 🔗 **Webhook backend** — prefer your own automation? POST each approved request to a webhook.
- 📜 **Script backend** — or run a local script per request, with the details on stdin and as environment variables.
- 🗂️ **Manual-import aware** — drop a file in by hand and Jelly Crowd marks the request available and asks Radarr/Sonarr to import it.
- ♻️ **Stalled-download recovery** — detects stuck downloads and re-searches automatically.
- 👥 **Shared ownership** — when several users request the same title they all own it, and it's removed only once nobody does.
- 🏷️ **Manual ownership** — assign an existing library title's ownership to a user, so it counts toward their quota.
- 🧑‍💼 **Request on behalf** — create a request for another user, straight from a title.
- 🛡️ **Review moderation** — hide, show or delete any community review.
- 🔒 **Access control** — hide the plugin while you set it up (*config mode*), with per-user enable / block overrides.
- 🩺 **Diagnostics & backup** — connectivity checks (TMDB, Radarr/Sonarr, indexers) and a one-click configuration backup.
- 📓 **Activity log** — a bounded, searchable log of admin, user and system events.

> [!NOTE]
> Everything is hosted by Jelly Crowd inside the Jellyfin web client — **no third-party plugin** is required.

## 📦 Requirements

- **Jellyfin 10.11+**
- A free **TMDB API key** (for the catalog)
- No other plugin

## 🚀 Installation

1. In Jellyfin: **Dashboard → Plugins → Repositories → Add**, and paste:

   ```text
   https://raw.githubusercontent.com/LokLakh-s/JellyCrowd/main/manifest.json
   ```

2. **Dashboard → Plugins → Catalog** → **Jelly Crowd** → **Install** → restart Jellyfin.
3. Open **Dashboard → Plugins → Jelly Crowd** and paste your **TMDB API key**.

> [!IMPORTANT]
> Updates are automatic — new versions appear in the catalog (or install themselves if auto-updates are on).

## 🛠️ Admin guide

<details>
<summary><b>Click to expand the admin guide</b></summary>

Paste your TMDB API key in **Dashboard → Plugins → Jelly Crowd**, then manage everything from the
**Admin** entry in the navbar (admin only). Its tabs:

- **Requests** — approve / deny / edit / delete, with live download status; retry a single blocked request
  or **retry all** stuck ones at once.
- **Stats** — totals, top media & users, activity chart, live *Now playing*, per-user drill-down.
- **Moderation** — triage user problem reports, and hide / show / delete community reviews.
- **Users** — per-user quota & policy overrides (with **bulk edit**), **user groups** (shared settings,
  Jellyfin library access and targeted announcements), who owns which media, and **assign** an existing
  title's ownership to a user.
- **Logs** — the plugin activity log.
- **Configurations**
  - **General** — default quota; approval mode (manual / auto, with size & genre rules); request rate
    limits; media-expiry window; adaptive quota; UI language; reviews on/off; stats capture on/off.
  - **Requests** — approval, rate limits, and the **request scope** (offer movies and/or TV; which granularities users may request).
  - **Notifications** — enable & configure each channel and pick which events go where; send test messages.
  - **Download** — fulfilment backend (none / Radarr+Sonarr / webhook / script), plus stalled-download recovery.
  - **Branding** — restyle the whole UI (images accept a URL **or** an upload).
  - **Diagnostics** — connectivity checks and a one-click configuration backup.

**Announcements** — post a banner from the header announcement icon: to everyone, or targeted at specific groups.

</details>

## 👤 User guide

<details>
<summary><b>Click to expand the user guide</b></summary>

Jelly Crowd adds navbar entries: **Catalog**, **Calendar**, **My requests**, **Dashboard**.

- **Catalog** — browse/search and **request** titles (whole show, season, or single episodes). Search a
  title, or an **actor/director's name** for their filmography. Follow titles with ★ to your **watchlist**,
  and get a **"For you"** row of suggestions.
- **Calendar** — upcoming releases of movies & shows.
- **My requests** — track statuses (pending → in progress → available); cancel pending ones; the
  **release date** is shown for unreleased titles.
- **My media** — what you own (request deletion to free your quota, or renew to keep it), plus your
  personal **watch history** — permanent, and yours to clear or turn off.
- **Dashboard** — your watch time, top watched, recent activity, request summary and storage usage.
- **Reviews** — rate & review titles (1–10), shown anonymously to others.

A **storage quota bar** in the header shows how much space you've used.

</details>

## 🐛 Support & bug reports

Found a bug or have an idea? [**Open an issue**](https://github.com/LokLakh-s/JellyCrowd/issues/new/choose)
and pick **Bug report** or **Feature request**. A good bug report (version, Jellyfin version, steps and what
you expected) gets fixed far faster.

Think you've found a **security vulnerability**? Please don't open a public issue — see [`SECURITY.md`](./SECURITY.md).

## 📜 License

Jelly Crowd is **proprietary software** — see [`LICENSE`](./LICENSE). You may install and run official
released builds as a Jellyfin plugin; no other rights are granted.
