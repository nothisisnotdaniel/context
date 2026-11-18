# 272 Steady MainTests.WindowClose_ClosedAfterLastRender_IsStillSeen

## What

The desktop test MainTests.WindowClose_ClosedAfterLastRender_IsStillSeen failed once in about 25 full runs during job 209 (message not captured). Find the timing it depends on and make it deterministic, without weakening what it checks.

## Why

A flaky test can fail CI on unrelated pushes (code review, job 209).

## Notes

Proposed by job 209-remove-save-button (`factory/jobs/209-remove-save-button/`).

Failed again on 2026-11-18 in the develop CI run after job 220 (https://github.com/nothisisnotdaniel/context/actions/runs/44375249148, "Assert.Equal() Failure: Expected: 21 Actual: 22" at tests/Context.Desktop.Tests/MainTests.cs:1814). Approved by the user the same day; it replaces #302.
