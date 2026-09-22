## What this changes

<!-- What the change does, and why it is worth doing. Link the issue it answers, if there is one. -->

## How it was verified

<!-- The suites you ran, and anything you exercised by hand against a real Jellyfin. -->

- [ ] `dotnet build -c Release` is clean (warnings fail the build on the plugin project)
- [ ] `dotnet test -c Release` passes
- [ ] `node --test tests/js/*.test.js` passes, if front-end logic changed

## Checklist

- [ ] New behaviour comes with tests — endpoints cover a failure case, services cover their logic,
      front-end logic lives in a tested `Web/*.lib.js` (see [CONTRIBUTING.md](../CONTRIBUTING.md))
- [ ] New user-visible strings are in `Web/strings/en.json` (and ideally `fr.json`) — nothing hard-coded
- [ ] Commits are signed off (`git commit -s`, Developer Certificate of Origin)
