# 205 Say why /done did nothing while a dialog is open

As a Context user, I want /done to tell me when it can't open its confirm dialog, so that I know my command was not lost.

Category: ux

## What

In TodoPanel.razor AskDone, when a dialog is already open or a todo write is running, show a short grey status line (for example "Finish the open dialog first.") instead of clearing the status line and doing nothing. Update the spec text and tests.

## Why

Today /done silently does nothing in that case, so it looks as if the command was ignored.

## Notes

Proposed by job 168-recurring-todos (review-ux round 1, nit 1).

Proposed by job 168-recurring-todos (`factory/jobs/168-recurring-todos/`).
