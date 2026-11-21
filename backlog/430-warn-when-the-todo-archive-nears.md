# 430 Warn when the todo archive nears its 20 MB cap

As a Context user, I want to be told when the todo archive is nearly full, so that todos.json does not quietly grow back toward its read limit.

Category: security

## What

When todos-archive.json gets close to its 20 MB cap, or a move is skipped because of it, show a clear line (desktop and CLI) saying old done todos now stay in todos.json, and say what the user can do.

## Why

Raised by review-security in job 361: once the archive hits 20 MB, old done todos stay in todos.json, which can grow back toward its 5 MB read limit with no warning.

## Notes

Proposed by job 361-archive-done-todos (`factory/jobs/361-archive-done-todos/`).
