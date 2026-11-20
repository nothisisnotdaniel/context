# 415 Name the current theme when a theme save fails

As a Context user, I want the error shown after a failed theme save to say which theme is still active, so that I know what the app is using without looking at the buttons.

Category: ux

## What

After a failed save in the Settings theme picker, the red failure line shows the fixed SettingsNotSavedText and does not name the theme that is still saved. Consider adding the current theme name to that line (or a second line), keeping the existing text for other settings.

## Why

The UX review of job 341 noted that the red line gives no hint which theme is current; the filled pressed button shows it, but the message alone does not. Without it a user who only reads the message may not know the theme did not change.

## Notes

From 341 ux review. Proposed by job 341-theme-setting (factory/jobs/341-theme-setting/). AC-4 of that job fixes the failure text, so this needs a spec change.

Proposed by job 341-theme-setting (`factory/jobs/341-theme-setting/`).
