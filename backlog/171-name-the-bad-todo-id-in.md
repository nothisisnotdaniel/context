# 171 Name the bad todo id in the CLI warning

## What

When a CLI command repairs a todo whose stored id is not a GUID, name the todo (its text, shortened) in the stderr warning and print it after the command output, so the warning is easy to act on.

## Why

UX review of job 151: the warning comes before the output and does not say which todo was repaired.

## Notes

Proposed by job 151-survive-bad-todo-id (`factory/jobs/151-survive-bad-todo-id/`).
