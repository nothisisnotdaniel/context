# 204 Cap todos.json size and refuse non-regular files

As a Context user, I want the todo file to be read safely, so that a huge or odd file in its place can't hang or crash the app.

## What

In TodoStore.Read, check that todos.json is a regular file (not a link to a device, FIFO or folder) and below a fixed size cap before File.ReadAllBytes and deserialising. On failure, report the same fixed error as other unreadable todo files and change nothing. Add tests.

## Why

todos.json is read whole with no size cap or file-type check, so a very large or non-regular file could exhaust memory or block the read.

## Notes

Proposed by job 168-recurring-todos (review-security round 1, nit 1).

Proposed by job 168-recurring-todos (`factory/jobs/168-recurring-todos/`).
