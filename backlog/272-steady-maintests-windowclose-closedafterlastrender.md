# 272 Steady MainTests.WindowClose_ClosedAfterLastRender_IsStillSeen

## What

The desktop test MainTests.WindowClose_ClosedAfterLastRender_IsStillSeen failed once in about 25 full runs during job 209 (message not captured). Find the timing it depends on and make it deterministic, without weakening what it checks.

## Why

A flaky test can fail CI on unrelated pushes (code review, job 209).

## Notes

Proposed by job 209-remove-save-button (`factory/jobs/209-remove-save-button/`).
