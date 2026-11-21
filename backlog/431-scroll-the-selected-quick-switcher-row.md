# 431 Scroll the selected quick switcher row into view

As a Context user, I want the quick switcher to keep the selected row visible when I move with the arrow keys, so that I can see what Enter will open.

Category: ux

## What

After ArrowUp or ArrowDown in QuickSwitcher, scroll the selected row into view (scrollIntoView through EditorInterop or a small JS call). This needs new JS interop, so it gets its own job.

## Why

Raised by the ux review of job 364: the list scrolls (up to 50 rows, max-height 50vh), but the arrow keys do not scroll, so the selection can go off-screen.

## Notes

Proposed by job 364-quick-switcher (`factory/jobs/364-quick-switcher/`).
