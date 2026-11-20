# 260 Return focus to the … button after a menu item or outside click

As a Context user who works from the keyboard, I want focus to go back to the … button after I pick a More actions item or click outside the menu, so that I do not lose my place in the window.

Category: ux

## What

In TopBar.razor (the More actions menu from job 200): after a menu item runs or the backdrop is clicked, put focus back on the … button, except when the chosen action opens its own inline form (Rename, New subpage, Move) or a confirmation that takes focus itself; those keep their focus. Esc already returns focus to the button. Add tests for both cases.

## Why

Review-ux and review-code of job 200 noted focus is lost after an item runs or the backdrop is clicked; keyboard users then start again from the top of the page.

## Notes

Proposed by job 200 (review-ux 2 / review-code 2 nit). The builder left it because it competes with the form field focus Main sets after Rename, New subpage and Move.

Proposed by job 200-page-actions-menu (`factory/jobs/200-page-actions-menu/`).
