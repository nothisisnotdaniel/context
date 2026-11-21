# 452 Say when a meeting booking loses its quicklist project

As a user booking a meeting from a sensitive or locked note, I want to be told when its work item could not be read, so that I know why the booking has no project.

Category: security

## What

In `DesktopActions` (around lines 3459-3467) any exception from `notes.TryOpen` is swallowed and the booking goes on without a quicklist position. Keep failing safe, but show a short grey hint such as `Booked without a project: can't read <note>.`.

## Why

Review-security of job 381 (rounds 1 and 2): it fails safe (no wrong project), but a sensitive or locked note quietly loses its project.

## Notes

Proposed by job 381-meeting-flow (`factory/jobs/381-meeting-flow/`).
