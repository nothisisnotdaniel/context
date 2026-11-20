# 055 Timed reminders

As a Context user, I want todos with a time and a notification when they're due, so that time-bound tasks don't slip by.

## What

A todo can have a time as well as a date; the desktop app shows a system notification when it is due (once, also after the app was closed at that time, then marked as notified). Old todos without a time work as before.

## Why

A due date alone doesn't remind me at 14:00 to call someone. A notification that fires once, even after the app was closed, closes that gap.

## Notes

CLI: extend the existing `context todo add` with an optional time after the date (for example `context todo add 2026-11-16 14:00 Call Anna`), and show the time in `context todos` and its `--json` (a new `time` field, `null` when there is none). Scripts and the command bar (#72) add todos this way. No new command.
