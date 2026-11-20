# 353 Fix flaky Record_WhileAnotherActionIsBusy_StillListed test

As a developer, I want RecentNotesActionsTests.Record_WhileAnotherActionIsBusy_StillListed to pass every time, so that a red CI run always means a real problem.

## What

RecentNotesActionsTests.Record_WhileAnotherActionIsBusy_StillListed (Context.Desktop tests) failed once in 39 runs (3 Desktop test runs in parallel) during job 267; its error message was not captured. Reproduce it by running the test class repeatedly in parallel, capture the failure, find the race (timing, shared state, the busy gate or the pending recent write) and make the test deterministic without weakening what it checks.

## Why

Found by the builder of job 267 while checking that the snapshot fix holds. A random failure on CI blocks merges and starts needless fix jobs.

## Notes

Proposed by job 267-flaky-snapshot-tests (build.md, AC-7 run).

Proposed by job 267-flaky-snapshot-tests (`factory/jobs/267-flaky-snapshot-tests/`).
