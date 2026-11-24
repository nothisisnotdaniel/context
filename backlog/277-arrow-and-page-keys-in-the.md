# 277 Arrow and Page keys in the Pick a day calendar do not scroll the page

As a desktop user moving through the Pick a day calendar with the keyboard, I want arrow and Page keys to move only the focused day, so that the page behind it does not scroll as well.

Category: bug

## What

Call preventDefault for ArrowLeft/Right/Up/Down, PageUp, PageDown, Home and End on the day buttons in TopBar.razor (a JS key filter or the existing interop), with a test.

## Why

Reviewers on job 213 found that a scrollable ancestor may scroll while the user moves around the calendar.

## Notes

Proposed by job 213-month-calendar-picker (`factory/jobs/213-month-calendar-picker/`).

## Windows reproduction, 2026-11-24

During #309 on develop `29b1b32c`, opening the top-bar calendar over a 6,932 px long synthetic daily note reproduced the background scroll in both themes. ArrowDown moved the main pane by 40 px. PageDown briefly moved it by 622 px and moved the popover top from y=34 to y=-588 before focus scrolled it back. The focused date changed correctly. Re-run with a scrollable main pane and observe the scroll events as well as the final position.
