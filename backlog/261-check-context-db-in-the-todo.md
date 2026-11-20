# 261 Check context.db in the todo done tests for note todos

Category: test

## What

In the CLI tests for `todo done` on a todo inside a note (job 201), also assert that context.db (the index) reflects the done state, not only the note file.

## Why

The code review of job 201 found the tests do not pin the index, so a regression there would go unnoticed.

## Notes

Proposed by job 201-todos-inside-notes (`factory/jobs/201-todos-inside-notes/`).
