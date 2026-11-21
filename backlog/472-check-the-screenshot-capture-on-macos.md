# 472 Check the screenshot capture on macOS

As the developer, I want to try screenshot capture by hand on macOS, so that it is known to work before the next release.

Category: test

## What

Run the checklist below on macOS in the desktop app and tick each step.

## Checklist

- [ ] Press Control+Option+Shift+S: the area picker opens.
- [ ] macOS asks for Screen Recording permission for Context the first time.
- [ ] Pick an area: the shot lands in the inbox with a linked attachment.
- [ ] Settings: set the watched folder to CleanShot X's save folder. Take a CleanShot screenshot: it is imported into the inbox.
- [ ] Quit and start again: the app starts normally and the hotkey still works.
- [ ] If any step fails, add a fix item that names the step and what you saw. (`MacGlobalHotkey.cs` for the hotkey, `MacScreenCapture.cs` for the picker temp file)

## Why

Job 260 was merged with these checks open: the native hotkey and screencapture launch have no automated test.

## Notes

- From the job 260-screenshot-to-inbox approver escalation.
- Split from #343 (one item per OS).
