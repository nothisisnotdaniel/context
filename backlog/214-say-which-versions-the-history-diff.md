# 214 Say which versions the history diff compares

As a Context user, I want the history panel to say which version its diff compares with what, so that I know what I would get back before I restore.

Category: ux

## What

In HistoryPanel.razor, add a visible line above the diff such as "Changes from <time> to the saved text", using the version's time as the list shows it. Keep the existing aria-label. Update the spec text and tests.

## Why

The diff shows no heading, so it is not clear which version is being compared with the current text.

## Notes

Proposed by job 174-note-history (review-ux round 1, nit 3).

Proposed by job 174-note-history (`factory/jobs/174-note-history/`).
