# 387 Meeting todos: move focus to the meeting picker when Push finds no next meeting

As a keyboard user, I want focus to move to the meeting picker when Push to next meeting finds no next occurrence, so that I can pick another appointment without tabbing to find it.

Category: ux

## What

In MeetingTodosPanel.razor, Push on a one-off meeting shows a picker of other appointments, announced with role="status", but focus stays on the Push button. Move focus to the picker's first choice (or its heading) when it opens, and return it to the todo's Push button when the picker closes with no choice. Add a bunit test for the focus calls.

## Why

UX review of job 287 (nit 2): today keyboard users must tab to reach the picker. Moving focus is new focus behaviour, so the builder left it for its own item.

## Notes

Proposed by job 287-todos-tied-to-meetings (review-ux 2).

Proposed by job 287-todos-tied-to-meetings (`factory/jobs/287-todos-tied-to-meetings/`).
