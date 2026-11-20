# 370 Command bar: show a running line while a command runs

As the user, I want the command bar to show that my command is running, so that I know it was taken while a slow Times call is still working.

Category: ux

## What

After Enter, commandbar.js (src/Context.Desktop/wwwroot/commandbar/) shows nothing until the result arrives, and a Times call can take up to 10 s. Show a short "Running…" line at once and replace it with the result; keep Esc working.

## Why

UX review of job 282 (nit 2): a slow call looks like nothing happened.

## Notes

Proposed by job 282-command-bar (review-ux 2).

Proposed by job 282-command-bar (`factory/jobs/282-command-bar/`).
