# Jelly Crowd — User Guide

Welcome! Jelly Crowd adds a discovery catalog and a media-request system **right inside Jellyfin**.
There's no extra account or app — you use your usual Jellyfin login.

> This guide is for **users**. For server-side setup, see [`CONFIGURATION.md`](CONFIGURATION.md).

## Where it lives

Once signed in to Jellyfin, a Jelly Crowd navigation bar appears at the top, next to **Home**:

- **Catalog** — browse / search movies and shows.
- **Calendar** — releases calendar (Month / Week / Day).
- **My requests** — track your in-progress requests.
- The **My library** area (right side) shows your **storage quota**; click it to manage your media.
- The 🔔 **bell** holds your notifications.

## Requesting a movie or show

1. Open **Catalog**, search for a title (or browse the rows / filters: genres, years, rating, platform…).
2. Click a poster to open its **detail popup** (synopsis, clickable cast, director/writer, TMDB/IMDb links).
3. Click **Request** (or **Request season** for a show).
   - You can pick a **desired date**: a title that isn't out yet will be fetched automatically on release.
   - **Request whole saga**: for a movie that belongs to a collection, request every missing part at once.
4. Depending on the configuration, your request is **auto-approved** or held **pending** an administrator.

The request shows up immediately in **My requests** and pre-reserves the matching space in your quota.

## Tracking your requests

In **My requests**, each row shows its state, sorted by priority (active first):

| State | Meaning |
|---|---|
| **Pending** | waiting for admin approval |
| **Approved** | accepted; search/download started |
| **Downloading** | being fetched (with % and size) |
| **Unreleased** | scheduled for its release date (a season shows the next episode's date) |
| **Available** | ready to watch in Jellyfin |
| **Blocked** | backend issue; retried automatically (an admin can re-trigger it) |

A **season in progress** appears as a **single line** (episode counter + next-episode date).
You can **cancel** a request while it isn't available yet.

> ⏱️ Progress and availability are **not real-time**: they refresh with a small delay. A title may be
> playable in Jellyfin before it's marked "Available" here.

## Your library & quota

The **My library** area shows `used / quota`. Click it to open your library:

- **Movies** appear as rows; **shows** are **expandable** (series › season › episode).
- You can see the **size** per movie, per season, and the series total.
- A **Delete** button at each level (whole series, one season, one episode): deletion frees your quota
  and removes the media (after a short retention delay).
- **Expiry**: media you don't re-watch eventually expires (freeing quota); **Keep (renew)** resets the timer.

If the admin enabled **adaptive quotas**, your quota can **grow** when you watch regularly (★ badge) and
shrink after a long period of inactivity (⏳ badge).

## Reviews & ratings

On a title's detail view (catalog popup **or** the native Jellyfin page) you can leave a **star rating**
and an optional comment. The server **average** is shown. Reviews are **anonymous** to other users
(only the admin sees authors, for moderation).

## Notifications

The 🔔 bell lists your in-app notifications (request approved/denied, media available, quota warnings…).
In its **settings** (⚙) you can enable delivery by **email / ntfy** and pick the categories you care
about (everything is off by default — in-app alerts always stay on).

## Reporting a problem

On an available title's detail view, **Report a problem** lets you flag an issue (wrong version, missing
subtitles, wrong audio language…) to the administrator.
