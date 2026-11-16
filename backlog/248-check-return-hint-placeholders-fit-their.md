# 248 Check Return-hint placeholders fit their columns

As a Context user, I want the "Return to …" hints in text boxes to be fully visible, so that I know Return submits the box.

## What

In the desktop app, check in the running app (light and dark) whether the placeholders added by job 194 fit: "New note title - Return to create" in the 240 px notes column (NotesPanel.razor) and the todo text box sharing its row with date, time, Today and Tomorrow in the 280 px column (TodoPanel.razor). If a hint is cut off or the todo row wraps badly, shorten the placeholder (for example "New note – Return") or let the row wrap cleanly, keeping the Return hint visible.

## Why

The ui and ux reviews of job 194 noted the hint is the part lost when a placeholder is clipped; nobody checked it visually.

## Notes

Proposed by job 194-capture-bar-enter-submit (review-ui 1, review-ux 1).

Proposed by job 194-capture-bar-enter-submit (`factory/jobs/194-capture-bar-enter-submit/`).
