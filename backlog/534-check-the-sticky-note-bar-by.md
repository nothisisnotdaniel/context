# 534 Check the sticky note bar by eye on macOS

Category: ui

## What

On macOS, open a long note in the desktop app and scroll it, in light and dark themes. Check that the bar above the note (.editor-bar) stays at the top and that nothing shows through in the 24px side padding of the column. Check that the fixed 44px height holds with a locked note (no format bar) and a long title. Check that the format bar does not wrap and overflow the bar in a narrow window, and that the top bar popovers draw over the sticky bar. Fix the layout if any of these fail.

## Why

Job 489 made the note bar sticky. The UI and UX reviews could only read the CSS, and the spec manual macOS check was not run, so the real rendering is unchecked.

## Notes

Proposed by job 489-middle-bar-sticky.001d9c2090bd4cb388735a46c69f941d (`factory/jobs/489-middle-bar-sticky.001d9c2090bd4cb388735a46c69f941d/`).
