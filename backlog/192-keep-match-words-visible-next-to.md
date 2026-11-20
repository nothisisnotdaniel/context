# 192 Keep match words visible next to long related-note titles

As a Context user, I want the matching words beside a related note to stay readable, so that I can see why a note was suggested even when its title is long.

Category: ui

## What

In the current view's related-note rows (CurrentPanel.razor, app.css .related-note), give the grey match words a minimum width or let the title truncate first, so a long note title no longer squeezes the matches into a narrow ellipsis. Add a test for the CSS rule.

## Why

Today a very long note title pushes the grey match words off to a tiny ellipsis, which hides the reason the note was suggested.

## Notes

Proposed by job 160-meeting-prep-notes (review-ui round 1, nit 1).

Proposed by job 160-meeting-prep-notes (`factory/jobs/160-meeting-prep-notes/`).
