# 179 Stop the MainTests breadcrumb test flaking

As a Context maintainer, I want MainTests.Breadcrumb_ChildOnly_AncestorsAsButtons_CurrentAsText to pass reliably, so that unrelated changes do not fail the test run.

## What

The builder of job 154 saw MainTests.Breadcrumb_ChildOnly_AncestorsAsButtons_CurrentAsText fail once in a full dotnet test run; it passed on two reruns. Find the timing dependence and make it deterministic or give its waits an explicit bounded timeout, as job 154 did for TagsPanelTests, without weakening what it checks.

## Why

The code review of job 154 flagged it as a separate flake. Left alone it can fail CI on develop for unrelated merges, as the TagsPanel test did after job 153.

## Notes

Proposed by job 154-tagspanel-test-flake (`factory/jobs/154-tagspanel-test-flake/`).
