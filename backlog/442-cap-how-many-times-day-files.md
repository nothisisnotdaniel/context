# 442 Cap how many Times day files one search reads

As a Context user with years of Times data, I want search to stay quick and bounded, so that a search without a date range never reads every day file.

Category: security

## What

SearchView.TimesHits reads every Times day file when no date range is set (each file is capped at 64 KiB). Add a cap on the number of files (newest first) or a default range, and say in the results when older bookings were left out.

## Why

The security review of job 379 noted the unbounded read. It is accepted for now, but it grows with every day of Times data.

## Notes

Proposed by job 379-search-context-times (review-security, round 2).

Proposed by job 379-search-context-times (`factory/jobs/379-search-context-times/`).
