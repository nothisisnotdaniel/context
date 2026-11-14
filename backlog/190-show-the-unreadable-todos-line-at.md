# 190 Show the unreadable-todos line at the top of the person page

As a Context user, I want the "Can't read the todos." warning near the top of a person page, so that I notice it even when the page is long.

## What

In PersonPanel.razor, move the red "Can't read the todos." line up next to the alias-clash lines, above the notes and appointments sections. Update AC-9 of job 159's layout tests (PersonPanelTests) to pin the new place.

## Why

Review-ux noted the line sits below all sections, so on a long person page it is easy to miss that the todos could not be read.

## Notes

Proposed by job 159-todo-person-links (review-ux round 1, nit 1).

Proposed by job 159-todo-person-links (`factory/jobs/159-todo-person-links/`).
