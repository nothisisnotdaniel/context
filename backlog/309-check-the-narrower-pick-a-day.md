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

## Windows verification, 2026-11-24

On develop `29b1b32c`, the actual Windows desktop top-bar calendar measured 1,000,018 px wide at both 1084 and 800 CSS px in Light and Dark. None of the six ISO week labels or 42 day targets was visible. Clicking the visible grid hit an empty cell. The current and selected metadata were correct, the arrow and month keys moved to the expected dates, and Enter opened the correct daily note. The default-width sidebar calendar displayed all 42 dates in a 229 px grid. Repeat the visual and pointer checks after [#506](506-size-the-top-bar-day-picker.md).

On the long fixture note, ArrowDown scrolled the page behind the calendar by 40 px and PageDown transiently by 622 px in both themes. That separate defect is tracked in [#277](277-arrow-and-page-keys-in-the.md).
