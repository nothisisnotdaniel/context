# 496 Make RecentNotesActionsTests.Open_AnyRoute_PutsItFirst_Once reliable

As the maintainer, I want RecentNotesActionsTests.Open_AnyRoute_PutsItFirst_Once to pass on every full test run, so that a red run always means a real regression.

Category: test

## What

The test failed once during a full dotnet test run in job 395 and passed 3 of 3 times when run on its own, and on the next full run. Find what makes it order- or timing-dependent (shared state, clock, file system, parallel test collections) and fix the test or the code under test so it passes reliably in full runs. Do not just add a retry.

## Why

A flaky test erodes trust in the suite and can block or confuse factory jobs whose builder and approver run the full suite (seen in job 395).

## Notes

Raised by the code review of job 395-new-note-after-appointment (factory/jobs/395-new-note-after-appointment/round-1/review-code.md).
