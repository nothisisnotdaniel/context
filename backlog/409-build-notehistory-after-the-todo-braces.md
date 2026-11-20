# 409 Build NoteHistory after the todo-braces record check

As the maintainer of Context, I want the todo-braces conversion to create its NoteHistory only after it has checked the run-once record, so that a start where the conversion already ran does no needless work.

Category: tidy-up

## What

In `TodoMarkerConversion.Run`, move the creation of the `NoteHistory` below the check for `<root>/.migrations/todo-braces`, so it is only built when the conversion actually runs. No behaviour change.

## Why

The security review of job 329 noted that the history object is built before the record check; harmless today, but it is wasted work on every start and easy to misuse later.

## Notes

From 329 security review.

Proposed by job 329-global-todo-braces (`factory/jobs/329-global-todo-braces/`).
