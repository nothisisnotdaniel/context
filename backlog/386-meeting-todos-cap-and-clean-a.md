# 386 Meeting todos: cap and clean a stored meeting title on read

As the user, I want Context to cap and clean the meeting title stored on a tied todo when it reads todos.json, so that a hand-edited or damaged file can't put a huge or odd title on screen.

Category: security

## What

TodoStore.ReadMeeting (src/Context.Core/TodoStore.cs) reads a tied todo's stored meeting title as is: it is not length-checked and not passed through SafeText. Cap it (for example at the same limit used for todo text) and clean it with SafeText on read, and decide whether the cleaned title is written back on the next save. Add a test with a very long title and one with control characters.

## Why

Security review of job 287 (nit 2): the title is rendered encoded, on one line and cut off, so the risk is low, but it is the only meeting field that is not bounded on read.

## Notes

Proposed by job 287-todos-tied-to-meetings (review-security 2).

Proposed by job 287-todos-tied-to-meetings (`factory/jobs/287-todos-tied-to-meetings/`).
