# 309 Check the narrower Pick a day popover in light and dark mode

As the maintainer, I want to look at the Pick a day popover once in light and dark mode, so that I know the narrower layout has no clipped week numbers or cramped day cells.

Category: ui

## What

Manual check, left by job 226: open Pick a day in the desktop app in light and in dark mode. Expected: the popover is as wide as the month header and grid with week numbers, no empty space at the sides, day cells readable and easy to click, today and the selected day still clear, keyboard navigation works. If it fails, add a fix item.

## Why

The UI and UX reviewers of job 226 noted that bunit has no layout engine, so the real width and look of the popover were not checked.

## Notes

Proposed by job 226-narrow-day-pick.

Proposed by job 226-narrow-day-pick (`factory/jobs/226-narrow-day-pick/`).
