# 411 Group counts on the Todos page

As a desktop user, I want each group on the Todos page to show how many todos it holds, so that I see the size of each group at a glance.

Category: ux

## What

On the Todos page (job 331), add the count of todos to each group heading (overdue, dated, undated). Keep the todo sidebar as it is. The loading line is done (AllTodosPanel.razor, commit 887fcff).

## Why

The UX review of job 331 noted that there is no state between loading and the empty line and no group counts; on a large data folder the page can briefly say there are no todos.

## Notes

From 331 ux review.

Proposed by job 331-todos-page-icon (`factory/jobs/331-todos-page-icon/`).

Reworded in the backlog audit, 2026-11-26.
