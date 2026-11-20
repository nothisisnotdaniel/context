# 395 Fix flaky recent.json test that failed CI after job 295

As the user, I want the develop CI to pass reliably, so that a real failure is not hidden among flaky ones.

## What

CI run https://github.com/nothisisnotdaniel/context/actions/runs/44602596103 (build / Test) failed in RecentNotesActionsTests.MissingOrBroken_EmptyState_NextOpenWritesAValidFile(json: {"recent":["a\\\\b"]}) at tests/Context.Desktop.Tests/RecentNotesActionsTests.cs:226: WaitForAssertion timed out with recent.json still holding the old content instead of {"recent":["Plan"]}\n (render count 21). Find why the write of recent.json after opening a note can take longer than the wait on a slow runner (or is skipped when the broken file holds a backslash entry), and make the test deterministic, for example by awaiting the pending recent.json write the way job 246 did for the palette tests, instead of only raising the timeout. If the code has a real bug (the write is lost), fix the code. Run the test class many times to show it is stable.

## Why

CI on develop failed after job 295 merged, in a test that job 295 did not touch; a flaky test blocks auto runs and hides real failures.

## Notes

Added by /factory auto after the CI failure of job 295.
