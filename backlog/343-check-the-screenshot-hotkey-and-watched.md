# 343 Check the screenshot hotkey and watched folder on macOS

As the developer, I want to try the screenshot capture by hand on macOS before the next release, so that the native hotkey code and the Screen Recording permission are known to work.

## What

On macOS: 1) Control+Option+Shift+S opens the area picker and the shot lands in the inbox with a linked attachment. 2) macOS asks for Screen Recording permission for Context. 3) Set the watched folder in Settings to CleanShot X's (or ShareX's on Windows) save folder and check a new screenshot is imported. 4) The app still starts normally on macOS and Windows. If any step fails, add a fix item naming the failure (hotkey registration in MacGlobalHotkey.cs, or the picker temp file in MacScreenCapture.cs).

## Why

Job 260 was merged on the human's approval with these checks open: the Carbon P/Invoke hotkey and the screencapture launch have no automated test, so a wrong signature would only show at run time.

## Notes

From the job 260-screenshot-to-inbox approver escalation.
