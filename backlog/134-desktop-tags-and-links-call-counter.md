# 134 Desktop tags and links: call counter for load tests

As a maintainer, I want the desktop tests to count TagIndex queries on DesktopState, so that the Sensitive guard and stale-result drop are proven directly.

## What

Add a test-visible call counter (or a test seam) on DesktopState so tests for AC-7 (no queries for Sensitive notes) and AC-8 (stale results dropped) assert on query calls instead of side effects.

## Why

Review-code (job 137) found AC-7 and AC-8 are proven only indirectly; a regression could slip past the current tests.

## Notes

Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).
