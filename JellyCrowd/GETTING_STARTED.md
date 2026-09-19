# Jelly Crowd — User Guide

Welcome! Jelly Crowd adds a discovery catalog and a media-request system **right inside Jellyfin**.
There's no extra account or app — you use your usual Jellyfin login.

> This guide is for **users**. For server-side setup, see [`CONFIGURATION.md`](CONFIGURATION.md).

**Install (admin, once):** in Jellyfin, go to *Dashboard → Plugins → Repositories → Add* and paste
`https://raw.githubusercontent.com/LokLakh-s/JellyCrowd/main/manifest.json`, then install **Jelly Crowd**
from the catalog and restart. Updates then arrive automatically.

## Where it lives

Once signed in to Jellyfin, a Jelly Crowd navigation bar appears at the top, next to **Home**:

- **Catalog** — browse / search movies and shows.
- **Calendar** — releases calendar (Month / Week / Day).
- **My requests** — track your in-progress requests.
- The **My library** area (right side) shows your **storage quota**; click it to manage your media.
- The 🔔 **bell** holds your notifications.
- Your **avatar menu** (top right) has *Settings* and **Report a problem** — see
  [Reporting a problem](#reporting-a-problem).

## Requesting a movie or show

1. Open **Catalog**, search for a title (or browse the rows / filters: genres, years, rating, platform…).
2. Click a poster to open its **detail popup** (synopsis, clickable cast, director/writer, TMDB/IMDb links).
3. Click **Request** (or **Request season** for a show).
   - You can pick a **desired date**: a title that isn't out yet will be fetched automatically on release.
   - **Request whole saga**: for a movie that belongs to a collection, request every missing part at once.
4. Depending on the configuration, your request is **auto-approved** or held **pending** an administrator.

The request shows up immediately in **My requests**. Your quota only goes up once the media actually
becomes **available** (counted at its real file size) — a pending request doesn't use any quota yet. If a
new request *would* push you over your quota, it's held until space frees up.

### Adding a title that's already there

On a title the server already has, the detail popup offers **Add to my library** instead of *Request*.
It costs you nothing to download — the file exists — but it **becomes yours**, so it counts against your
quota at its real size on disk, and you can then ask for its deletion or let it expire.

Because the file is already there, there is nothing to wait for: if it doesn't fit in what's left of your
quota, the button is **refused** rather than queued, and tells you to free some space first. When your
quota is already full, the button is shown greyed out with the reason.

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
  Expiry only drops your ownership — **the file stays** in the shared library.
- If your library has grown **past** your quota, **Keep (renew)** is greyed out until you free space: your
  oldest media then expires on its own until you're back under the limit. Being exactly *at* your quota is
  not over it — renewing still works there.

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

When several episodes of a season you requested become available at once, they arrive as a **single
grouped notification** per channel (e.g. "6 episodes now available") rather than one message per episode.

## Reporting a problem

Two ways in, both reaching the administrators the same way:

- **About a title** — open its detail popup and click **⚠ Report a problem** at the top: wrong version,
  missing subtitles, wrong audio language… The title is attached to your report automatically.
- **About anything else** — open your **avatar menu** (top right) and pick **Report a problem**: playback
  trouble, an account or access issue, or any question for the administrators. No title is attached.

Pick a category, describe the issue, send. Administrators are notified **immediately** (in their own
notifications, and on whichever channels the server has configured), and they're reminded automatically
while your report stays open. When one of them resolves it, their answer arrives in your 🔔 bell.
