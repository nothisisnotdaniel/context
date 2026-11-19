# 396 Reproduce the recent.json test race on a slow runner

As the user, I want the cause of the flaky recent.json test confirmed by a reproduction, so that I know job 297's fix addresses the real cause and not a guess.

## What

Job 297 made RecentNotesActionsTests wait for the pending recent.json write (WaitForRecentWrites) instead of polling the file inside WaitForAssertion, on the code-derived cause that bUnit re-checks an assertion only on a render, while the write finishes after the last render. Reproduce the old failure (for example by delaying the write with HoldRecentWrites, or under CPU load) against the pre-297 test code, and record the result in the test's comment or the story. No code change unless the reproduction shows another cause.

## Why

Code review of job 297 (nit 1): the root cause was derived from the code, not reproduced; the reviewer accepted it because job 246's DesktopFixture comment documents the same mechanism.

## Notes

Proposed by job 297-flaky-recent-test (review-code 1).

Proposed by job 297-flaky-recent-test (`factory/jobs/297-flaky-recent-test/`).
