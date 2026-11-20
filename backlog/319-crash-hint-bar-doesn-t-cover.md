# 319 Crash hint bar doesn't cover the header

As a desktop user, I want the crash hint after an unclean exit to sit where it doesn't cover the header, so that I can still reach the header's controls before I dismiss the hint.

## What

The crash hint (.crash-hint in src/Context.Desktop/wwwroot/app.css) is a fixed bar that can overlap the header until it is dismissed. Show it in the layout flow (for example as a strip above or below the header that pushes content down) so nothing is covered, in light and dark mode, keyboard and screen reader friendly.

## Why

UI and UX reviews of job 237: the fixed bar can hide header content until the user dismisses it; the builder left it because moving it changes the layout beyond the job 237 spec.

## Notes

Proposed by job 237-crash-log-hint (review-ui 1, review-ux 1).

Proposed by job 237-crash-log-hint (`factory/jobs/237-crash-log-hint/`).
