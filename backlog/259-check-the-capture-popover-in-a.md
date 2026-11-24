# 259 Check the Capture popover in a narrow window

As a Context user, I want the top bar Capture popover to fit inside a narrow window, so that I can always see and use the whole capture box.

Category: ui

## What

Open the desktop app in a narrow window (about 400px wide) and open the Capture popover from the top bar icon. The 320px input (anchored right: 8px, capped by max-width: calc(100vw - 48px)) must stay fully inside the window, in light and dark mode. Fix the popover position or width only if it overflows.

## Why

Review-ui of job 199 could not check this by eye; an overflowing popover would cut off the capture box on small windows.

## Notes

Proposed by job 199 (review-ui round 1 nit).

Proposed by job 199-capture-icon-top-bar (`factory/jobs/199-capture-icon-top-bar/`).

## Windows verification, 2026-11-24

At develop `29b1b32c`, a 416 × 539 native window gave a 400 × 500 CSS-pixel viewport in both Light and Dark. No part of the top-bar Capture icon was initially clickable. Tab reached the actual Capture icon and Enter opened its focused field. Only 31 of the field’s 320 px and 40 of the popover’s 338 px were visible. The existing right offset was 8 px and the width cap was already `calc(100vw - 48px)`. A complete test line reached the disposable inbox exactly once in each theme. At 1084 px the same icon opened the full 320 px input with a pointer click.

These are the native 400 px observations recorded with #152. Keep this check open and repeat it after [#503](503-keep-capture-visible-in-narrow-desktop.md), which already covers this exact interaction, width and both themes.
