# 216 Fix the flaky breadcrumb test in MainTests

As the Context developer, I want the desktop test suite to pass every time, so that a CI run never fails on a test that has nothing to do with the change.

## What

Find why MainTests.Breadcrumb_ChildOnly_AncestorsAsButtons_CurrentAsText fails now and then in a full run but passes alone (shared state, timing, or test order). Make it deterministic without weakening what it checks, and run the full suite several times to confirm.

## Why

It failed once in the full run during jobs 173 and 174 and passed on every re-run, so it can fail CI at random.

## Notes

Proposed by job 174-note-history (approver: one-off failure in the first full run).

Proposed by job 174-note-history (`factory/jobs/174-note-history/`).
