# 284 Cap the length of each text field read from Outlook

As a desktop user with Outlook, I want very long subjects, locations and sender names to be cut to a safe length, so that one odd mail or meeting can't bloat the sidebar or the snapshot.

## What

OutlookComSource.cs reads Subject, Location and SenderName with no cap on each field (only the item count is bounded). Cut each to a fixed maximum length when it is read, with a test using FakeOutlookSource-style input.

## Why

The security review of job 217 noted that there is no length cap per field on text read from Outlook.

## Notes

Proposed by job 217-outlook-com-source (review-security nit 1).

Proposed by job 217-outlook-com-source (`factory/jobs/217-outlook-com-source/`).
