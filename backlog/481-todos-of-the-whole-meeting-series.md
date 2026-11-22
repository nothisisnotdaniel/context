# 481 Todos of the whole meeting series

As someone in a weekly meeting, I want to see the open and done todos of all occurrences of that series, so that nothing tied to an earlier occurrence gets lost.

Category: feature

## What

The appointment page shows, next to the todos of this occurrence, the todos of every other occurrence of the same series (same `MeetingLink.Uid`), open first, then done (archive included), each with the occurrence date. Nothing new is stored: the Uid is already on each tied todo.

## Why

Context ties a todo to one occurrence and carries it forward, but the series view is missing. assistant shows tasks per appointment and per series.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
