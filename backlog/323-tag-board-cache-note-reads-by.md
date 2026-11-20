# 323 Tag board: cache note reads by note version

As a desktop user with many tagged notes, I want the tag board to open and refresh quickly, so that a large tag doesn't make the board slow.

## What

BoardView.Build (src/Context.Core/BoardView.cs) reads every tagged note in full (ReadForEdit, bounded by MaxEditBytes) under indexGate on each board load and each State.Changed. Cache the parsed cards per note by note version (or file timestamp), so only changed notes are read again. Keep the result identical to a full re-read.

## Why

Security review of job 242: bounded and fine for a personal store today, but boards on large tags re-read every note on each change.

## Notes

Proposed by job 242-tag-board-view (review-security 1).

Proposed by job 242-tag-board-view (`factory/jobs/242-tag-board-view/`).
