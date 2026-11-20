# 276 Feedback when a day pick is ignored while busy

As a Context user, I want to see why nothing happened when I pick a day while the app is still busy, so that the pick does not look lost.

## What

In Main.razor, a Pick a day choice (and /today) that arrives while another action is running is silently ignored after the popover closed. Show a short status or keep the pick pending until the running action ends; do the same for /today.

## Why

The ux review of job 212 noted that the popover closes and nothing opens, which looks like the pick failed.

## Notes

Proposed by job 212-calendar-day-editor (ux review nit).

Proposed by job 212-calendar-day-editor (`factory/jobs/212-calendar-day-editor/`).
