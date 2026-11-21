# 440 Let #<n> work-item searches be saved

As a Context user, I want to save a search like #1234 like any other search, so that I can rerun a work item's notes and bookings with one click.

Category: feature

## What

Job 379 hides Save search for #<n> queries, because SavedSearches.IsValidQuery refuses a query that starts with '#'. Teach the saved-search validator to accept #<n> (digits only, tag names stay refused), show Save search for it again, and keep the refusal tests for tag filters.

## Why

The code review of job 379 noted the Save search guard as a stopgap. Without it the most useful cross-search, a work item, can't be saved.

## Notes

Proposed by job 379-search-context-times (review-code, round 2).

Proposed by job 379-search-context-times (`factory/jobs/379-search-context-times/`).
