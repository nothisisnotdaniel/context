# 350 Standup: catch feed and calendar errors while composing

As a desktop user, I want a broken Times feed or calendar file to give me a clear standup error instead of an unexpected failure, so that the daily note is never left half updated.

Category: bug

## What

Standup.Compose (feed fetch, calendar parse) runs outside the try/catch in DayEnd.RefreshStandup (src/Context.Core/DayEnd.cs), so its "never throws" doc comment relies on those callees never throwing. Move Compose inside the try/catch (or make the callees' failures explicit results) and add a test with a feed or calendar that throws, for both the desktop and the `context standup` CLI.

## Why

Security review of job 265: today DesktopActions.Run catches the exception, but the contract is fragile and the CLI path behaves differently from what the doc comment says.

## Notes

Proposed by job 265-standup-notes (review-security 2).

Proposed by job 265-standup-notes (`factory/jobs/265-standup-notes/`).
