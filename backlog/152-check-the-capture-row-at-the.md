# 152 Check the capture row at the minimum window size

As a Context user, I want the capture row to fit the smallest desktop window, so that the box and button stay usable.

Category: ui

## What

Open the desktop app, shrink the window to its minimum size, and check that the capture row wraps cleanly and nothing is cut off; report anything wrong as a backlog item.

## Why

Review-ui and review-ux (job 140): nobody looked at the capture row in a real window at minimum size.

## Notes

Raised by job 140-desktop-quick-capture.

## Manual verification

Windows check on 2026-11-24 at `29b1b32`: #250 removed the original row and #253 added the current Capture icon and popover. At the native minimum the window had no client height. At 120 and 400 CSS pixels wide only 31 pixels of the focused field were visible. Both themes reproduced the clipping. Technical fix [#503](503-keep-capture-visible-in-narrow-desktop.md) covers the follow-up with #259. Repeat this check after the fix.
