# 341 Fix flaky ToReviewPanelTests.Main_DirtyOpenNote_SaveFirst

As a developer, I want ToReviewPanelTests.Main_DirtyOpenNote_SaveFirst to pass every time, so that CI on develop is green and a red run always means a real problem.

## What

CI run https://github.com/nothisisnotdaniel/context/actions/runs/44474713960 (after job 256-review-dates merged) failed in Context.Desktop.Tests: ToReviewPanelTests.Main_DirtyOpenNote_SaveFirst (ToReviewPanelTests.cs:220) compared a folder snapshot that included a pending .recent.json.<guid>.tmp write against one taken after it finished. Make the snapshot wait for or exclude pending recent.json writes and background index updates, using the helpers job 246 added (WaitForRecentWrites, SnapshotUnderIndexGate), and check the other new tests from job 256 (ToReviewPanelTests, ReviewActionsTests) for the same pattern. Do not weaken what the tests check.

## Why

CI on develop is red after job 256; a random failure under CI blocks every later merge.

## Notes

CI failure after job 256-review-dates; same race as fixed in job 246-flaky-palette-tests.
