# 473 Check the screenshot capture on Windows

As the developer, I want to try screenshot capture by hand on Windows, so that it is known to work before the next release.

Category: test

## What

Run the checklist below on Windows in the desktop app and tick each step.

## Checklist

- [ ] The app starts normally (Setup.exe or a Release build).
- [ ] Settings: set the watched folder to ShareX's save folder. Take a ShareX screenshot: it is imported into the inbox with a linked attachment.
- [ ] Take a second screenshot: it is imported once, not twice.
- [ ] Quit and start again: the watched folder is still set and new screenshots still arrive.
- [ ] If any step fails, add a fix item that names the step and what you saw.

## Why

Job 260 was merged with these checks open: the native hotkey and screencapture launch have no automated test.

## Notes

- From the job 260-screenshot-to-inbox approver escalation.
- Split from #343 (one item per OS).
