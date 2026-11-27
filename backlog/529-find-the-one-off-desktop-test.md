# 529 Find the one-off Desktop test failure under parallel runs

As a factory user, I want the intermittent Desktop test failure found and fixed, so that builds and approvals stop reporting failures nobody can reproduce.

Category: test

## What

In job 483, one Desktop test failed once in a full dotnet test run while it ran in parallel with Context.Tests. The approver saw RecentNotesActionsTests fail once, then pass in 3 reruns. Find the test, make it stable, and check it with several full runs.

## Why

The code review of job 483 noted the failure was never identified. Flaky tests hide real failures and cost reruns in every job.

## Notes

Proposed by job 483-attendees-by-role.7e6bcb6115c34d74ab8fe7e19bd682ff (`factory/jobs/483-attendees-by-role.7e6bcb6115c34d74ab8fe7e19bd682ff/`).
