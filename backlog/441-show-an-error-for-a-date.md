# 441 Show an error for a date the search range can't read

As a Context user, I want the search to tell me when a date in the range can't be read, so that a typo never silently widens the search.

Category: security

## What

In SearchPanel.razor ParseDate, a from or to value that doesn't parse as yyyy-MM-dd now drops that bound without a word. Show a short inline message instead and keep the previous results until the date is fixed.

## Why

The security review of job 379 found that a bad date silently removes the bound. The date input normally only sends yyyy-MM-dd, but other callers could pass anything.

## Notes

Proposed by job 379-search-context-times (review-security, round 2).

Proposed by job 379-search-context-times (`factory/jobs/379-search-context-times/`).
