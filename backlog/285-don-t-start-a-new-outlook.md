# 285 Don't start a new Outlook read while an earlier one hangs

As a desktop user, I want the app to wait for a hanging Outlook read instead of starting another one on every day step, so that threads don't pile up while Outlook shows a prompt.

Category: bug

## What

In DesktopState.cs OutlookDay/Start, every new day the user steps to starts another LongRunning thread, even while an earlier COM call is still hanging (for example at the object-model guard prompt). Reuse or skip while one is in flight, and keep the cache and status behaviour clear.

## Why

The security review of job 217 noted that a hanging Outlook starts a new thread on each day step and nothing cancels them.

## Notes

Proposed by job 217-outlook-com-source (review-security nit 2).

Proposed by job 217-outlook-com-source (`factory/jobs/217-outlook-com-source/`).
