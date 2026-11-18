# 302 Find the desktop test that failed once in job 220

As a developer, I want the one desktop test that failed once during job 220 found and steadied, so that full test runs stay trustworthy.

## What

During job 220 one Context.Desktop.Tests test failed on the first full run and passed in four later runs; its log was not kept. Run the desktop suite several times (for example 10 runs) with the logs kept, name the test that fails, and fix its timing or shared state.

## Why

The code review of job 220 noted an unexplained one-off failure; a flaky test hides real failures.

## Notes

Proposed by job 220-meeting-notes-hierarchy (review-code nit 1).

Proposed by job 220-meeting-notes-hierarchy (`factory/jobs/220-meeting-notes-hierarchy/`).
