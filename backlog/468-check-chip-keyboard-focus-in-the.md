# 468 Check chip keyboard focus in the desktop web view on macOS

As a keyboard user on macOS, I want to reach and open tag, link and mention chips with the keyboard, so that I don't need the mouse.

Category: ux

## What

Run the checklist below on macOS in the desktop app and tick each step.

## Checklist

- [ ] Open a note with a tag chip, a link chip and a mention chip.
- [ ] With System Settings > Keyboard > Keyboard navigation off, press Tab: note whether the chips are skipped.
- [ ] Turn Keyboard navigation on and press Tab: each chip gets focus, in order.
- [ ] The focus ring is visible in light and in dark mode.
- [ ] Enter on a focused chip opens it; a click still opens it.
- [ ] With a chip focused, Cmd+S saves and Cmd+K opens the palette.
- [ ] If any step fails, add a fix item that names the step and what you saw.

## Why

Focus behaviour depends on the OS web view, which bUnit and Jint can't show.

## Notes

- From job 142.
- Split from #155 (one item per OS).
