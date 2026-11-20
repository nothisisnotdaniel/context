# 288 Don't let a blocked Outlook delay every refresh

As a desktop user, I want the snapshot and sidebar to refresh promptly even when Outlook is blocked, so that the rest of the app stays responsive.

Category: bug

## What

DesktopState.cs Refresh waits for Outlook (up to OutlookTimeout, 10 s) before it builds the snapshot. Build the snapshot without waiting, using the last Outlook result, and update it when Outlook answers.

## Why

The code review of job 217 noted that a blocked Outlook delays every refresh by up to 10 s.

## Notes

Proposed by job 217-outlook-com-source (review-code nit 1).

Proposed by job 217-outlook-com-source (`factory/jobs/217-outlook-com-source/`).
