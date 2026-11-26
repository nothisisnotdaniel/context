# 335 Fix flaky Open_AnyRoute_PutsItFirst_Once test

As a developer, I want Open_AnyRoute_PutsItFirst_Once to pass every time, so that a red test run always means a real problem and CI doesn't fail at random.

Category: test

## What

Under load this Desktop test clicked an element that a re-render had already replaced (stale element). Re-find the element right before the click (or wait for the render) so the test is independent of render timing, without weakening what it checks.

## Why

Found by the builder of job 246 under load; a random failure under -warnaserror CI blocks merges.

## Notes

Proposed by job 246-flaky-palette-tests (build.md round 2).

Proposed by job 246-flaky-palette-tests (`factory/jobs/246-flaky-palette-tests/`).

## Folded in from #496 (backlog audit, 2026-11-26)

Make RecentNotesActionsTests.Open_AnyRoute_PutsItFirst_Once reliable: The test failed once during a full dotnet test run in job 395 and passed 3 of 3 times when run on its own, and on the next full run. Find what makes it order- or timing-dependent (shared state, clock, file system, parallel test collections) and fix the test or the code under test so it passes reliably in full runs. Do not just add a retry.
