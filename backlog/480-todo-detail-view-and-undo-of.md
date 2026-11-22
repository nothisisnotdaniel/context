# 480 Todo detail view and undo of a Done

As someone who ticks a todo by mistake, I want to undo the Done and open a todo to see all of it, so that a slip costs nothing and long todos are readable.

Category: feature

## What

A todo detail view (from the todo list) shows the whole text, due date, time, repeat, meeting link, created and done times, with edit of text and due date. After a Done, the status line offers `Undo` for 10 seconds: it reopens the todo (clears `done`; for a recurring todo also removes the next todo it created if that one is still open and unchanged) and removes the line it added to the done log only if that line is still the last one and unchanged. Note todos undo by turning `{x}` back into `{ }` under the same checks as `NoteTodos.Complete`.

## Why

Today a Done can only be undone by editing `todos.json` by hand. assistant has a task detail and undo.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
