# 365 Pin icon contrast on the open note row

As the user, I want the pin icon to stay easy to see on the row of the note I have open, so that I can tell it is pinned in both light and dark mode.

Category: ui

## What

The sidebar pin icon (app.css .pin-icon) uses var(--grey), which may be low-contrast on the open row's --panel background. Check the contrast in both themes and, if it is below 3:1 for a non-text icon, use a token that passes on both the normal and the open row; add a source test for the chosen token.

## Why

UI review of job 278 (nit 1): the builder left it because a new colour is a design change that needs its own review.

## Notes

Proposed by job 278-recent-pinned-notes (review-ui 1).

Proposed by job 278-recent-pinned-notes (`factory/jobs/278-recent-pinned-notes/`).
