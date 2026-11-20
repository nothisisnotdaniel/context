# 286 Culture-safe dates in the Outlook Restrict filter

As a desktop user with an unusual date format, I want Outlook appointments to load reliably, so that my calendar shows regardless of my regional settings.

Category: bug

## What

OutlookComSource.cs Filter builds the Restrict text from current-culture `g` dates. Check what format Outlook's Restrict accepts across cultures and make the filter robust (for example a fixed format Outlook accepts), keeping today's behaviour where it works.

## Why

The security review of job 217 noted that a culture whose short date has unusual separators may break the filter, which then reads as Failed.

## Notes

Proposed by job 217-outlook-com-source (review-security nit 3). Needs a check on Windows with classic Outlook.

Proposed by job 217-outlook-com-source (`factory/jobs/217-outlook-com-source/`).
