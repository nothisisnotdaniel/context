# 159 Fix the /todo hint in context current

As a CLI user, I want the empty-todos hint in `context current` to name a command that exists, so that I can follow it.

## What

`context current` still prints `Add one with /todo today <text>.` when there are no todos for today, a console command that no longer exists since the interactive console was removed. Replace it with the matching `context todo` CLI command (or the desktop app), and update the tests that pin the text.

## Why

Raised by the ui, ux, code and security reviews of job 145. Left as is, the hint points users to a command that fails.

## Notes

Proposed by job 145-remove-interactive-console (`factory/jobs/145-remove-interactive-console/`).
