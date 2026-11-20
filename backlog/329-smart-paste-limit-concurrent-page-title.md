# 329 Smart paste: limit concurrent page-title fetches

As a Context user, I want the app to fetch only a few page titles at once, so that pasting many links quickly can't flood the network or pile up requests.

Category: security

## What

Cap concurrent PageTitleFetcher requests (for example 2–4 with a SemaphoreSlim); a paste beyond the cap waits or keeps its plain link when the short timeout runs out. Keep the existing timeout and size limits.

## Why

Security review of job 244: nothing bounds how many title fetches run at the same time.

## Notes

Proposed by job 244-smart-paste (review-security 3).

Proposed by job 244-smart-paste (`factory/jobs/244-smart-paste/`).
