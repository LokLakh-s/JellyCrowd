# Security Policy

## Supported versions

Only the **latest released version** of Jelly Crowd is supported. Please update before reporting an issue.

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Instead, use GitHub's private vulnerability reporting: on this repository, open the **Security** tab and
choose **Report a vulnerability**. That keeps the details private until a fix is available.

Please include:

- the Jelly Crowd and Jellyfin versions,
- a description of the issue and its impact,
- steps to reproduce, if you have them.

We'll acknowledge your report as soon as we can and keep you posted on the fix. Thanks for helping keep
Jelly Crowd and its users safe.

## What is in scope

Jelly Crowd runs inside Jellyfin, serves its own web UI to every user of the server and holds an admin
surface. The interesting classes of problem are therefore:

- **Authorization** — any route under `/JellyCrowd/` that answers a non-admin where it should not, or that
  lets one user read or act on another user's requests, quotas, media ownership or statistics. A handful
  of endpoints are anonymous on purpose (the branding, the language, the guide, the static web assets);
  each says so in its own XML documentation. Anything anonymous that returns more than that is a bug.
- **Injection** — Jelly Crowd injects its shell into Jellyfin's `index.html` through its own middleware,
  and renders admin-supplied branding (custom CSS, links, logos) and user-supplied text (reviews, reports)
  into that UI.
- **Credential exposure** — the plugin configuration holds a TMDB key, SMTP credentials, Radarr/Sonarr API
  keys and notification webhooks. None of it should ever reach a non-admin.
- **Path traversal** — the guide serves images from a folder an administrator controls.

Findings in Jellyfin itself belong upstream, at <https://github.com/jellyfin/jellyfin>.
