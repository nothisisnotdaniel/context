# 479 Active status and priority for todos

As someone with many open todos, I want to mark the ones I am working on as active and give todos a priority, so that the list shows first what matters now.

Category: feature

## What

`todos.json` todos get an optional `status` (`active`; absent is open) and an optional `priority` (`high`, `low`; absent is normal), written only when set so a file without them keeps its bytes, not part of `DerivedId`, any other value makes the file unreadable like a bad `repeat`. The desktop todo list shows active todos first with a marker, then by priority, then the current order, and offers Start/Stop and a priority choice per todo. `context todos` and `--json` show both fields. Note todos stay without them.

## Why

Context todos are only open or done. assistant has Open / Active / Done and a priority, which helps when the list is long.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
