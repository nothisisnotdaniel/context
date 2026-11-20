# 411 Loading state and group counts on the Todos page

As a desktop user, I want the Todos page to show that it is loading and how many todos each group holds, so that I do not mistake a slow load for an empty list.

Category: ux

## What

On the Todos page (job 331), show a short loading line until the todos are read, before the empty state can appear, and add the count of todos to each group heading (overdue, dated, undated). Keep the todo sidebar as it is.

## Why

The UX review of job 331 noted that there is no state between loading and the empty line and no group counts; on a large data folder the page can briefly say there are no todos.

## Notes

From 331 ux review.

Proposed by job 331-todos-page-icon (`factory/jobs/331-todos-page-icon/`).
