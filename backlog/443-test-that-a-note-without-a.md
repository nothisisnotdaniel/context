# 443 Test that a note without a modified date is left out of a date range

As a Context developer, I want a test for the rule that drops notes with no modified date from a dated search, so that the rule can't change by accident.

Category: test

## What

Add a test for SearchView.InRange: with a from/to range set, a note whose Modified is null is not in the results; without a range it is.

## Why

The security review of job 379 found the rule untested. It fails closed today, but nothing guards it.

## Notes

Proposed by job 379-search-context-times (review-security, round 2). The builder thought it needs a race between Sync and List; a fake index entry with a null Modified may be enough.

Proposed by job 379-search-context-times (`factory/jobs/379-search-context-times/`).
