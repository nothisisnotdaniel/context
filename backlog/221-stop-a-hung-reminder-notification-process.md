# 221 Stop a hung reminder notification process

As a Context user, I want the desktop app to give up on a notification helper that hangs, so that one stuck reminder can't pile up processes or block later reminders.

Category: bug

## What

In SystemNotifier, wait for the osascript (macOS) or powershell (Windows) process with a timeout and kill it if it doesn't exit, then log once and carry on. Make sure the todo is still marked as notified only as the spec says. Add tests with a fake process runner.

## Why

Today the helper process is started and not bounded, so a hung helper could stay around and stack up with each reminder.

## Notes

Proposed by job 178-timed-reminders (review-security round 1, nit 1).

Proposed by job 178-timed-reminders (`factory/jobs/178-timed-reminders/`).
