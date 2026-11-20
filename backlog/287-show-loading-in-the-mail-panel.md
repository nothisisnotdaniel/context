# 287 Show Loading in the mail panel during the first read

As a desktop user, I want the recent mail panel to say it is loading while Outlook is slow, so that I know mail is on its way.

Category: ux

## What

RecentMailPanel.razor renders nothing while the first mail read runs (up to 10 s with a slow Outlook). Show a short grey `Loading…` line until the first result arrives, in light and dark mode.

## Why

The UX review of job 217 noted that a slow Outlook gives no sign that mail is loading.

## Notes

Proposed by job 217-outlook-com-source (review-ux nit 1).

Proposed by job 217-outlook-com-source (`factory/jobs/217-outlook-com-source/`).
