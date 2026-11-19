# 383 Mail digest: keep Read mail disabled while a timed-out read still runs

As the user, I want Read mail to stay disabled until the previous Outlook read has really ended, so that clicking again during Outlook's security prompt doesn't start a second read.

## What

After a TimedOut in DesktopState.cs (~l.521), the COM call keeps running on its LongRunning thread, but Read mail is enabled again. Track the running task and keep the button disabled (with a status line) until it ends, and add a test.

## Why

Security review of job 286 (nit 3): repeated clicks can stack COM calls behind Outlook's guard prompt.

## Notes

Proposed by job 286-mail-digest (review-security 3).

Proposed by job 286-mail-digest (`factory/jobs/286-mail-digest/`).
