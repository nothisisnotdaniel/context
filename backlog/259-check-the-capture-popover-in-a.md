# 259 Check the Capture popover in a narrow window

As a Context user, I want the top bar Capture popover to fit inside a narrow window, so that I can always see and use the whole capture box.

## What

Open the desktop app in a narrow window (about 400px wide) and open the Capture popover from the top bar icon. The 320px input (anchored right: 8px, capped by max-width: calc(100vw - 48px)) must stay fully inside the window, in light and dark mode. Fix the popover position or width only if it overflows.

## Why

Review-ui of job 199 could not check this by eye; an overflowing popover would cut off the capture box on small windows.

## Notes

Proposed by job 199 (review-ui round 1 nit).

Proposed by job 199-capture-icon-top-bar (`factory/jobs/199-capture-icon-top-bar/`).
