# 473 Check the screenshot capture on Windows

As the developer, I want to try screenshot capture by hand on Windows, so that it is known to work before the next release.

Category: test

## What

Run the checklist below on Windows in the desktop app and tick each step.

## Checklist

- [x] The app starts normally (Setup.exe or a Release build).
- [ ] Settings: set the watched folder to ShareX's save folder. Take a ShareX screenshot: it is imported into the inbox with a linked attachment.
- [ ] Take a second screenshot: it is imported once, not twice.
- [x] Quit and start again: the watched folder is still set and new screenshots still arrive.
- [ ] If any step fails, add a fix item that names the step and what you saw.

## Why

Job 260 was merged with these checks open: the native hotkey and screencapture launch have no automated test.

## Notes

- From the job 260-screenshot-to-inbox approver escalation.
- Split from #343 (one item per OS).

## Windows verification

On 2026-11-24, the Windows Release build at `29b1b32c` started twice with an isolated profile. Settings saved ShareX's initially empty local folder. After quitting and restarting Context, that folder was still displayed and a new image arrived. Three different 420 × 180 captures of a synthetic window, taken using the original ShareX 22.4.0 capture library with the window handle, each produced one inbox link and one byte-identical attachment. Restarting preserved the first two entries exactly once.

The ordinary ShareX desktop-app capture still needs two screenshots on an interactive Windows test display. On the isolated background desktop, the portable app's configured-window command reported Windows error 5 while reading the desktop surface, before writing its first image. The window-targeted captures verify the Context import and restart. No Context fix was identified.
