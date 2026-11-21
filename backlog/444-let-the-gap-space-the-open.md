# 444 Let the gap space the (open) label in search results

As a Context user, I want the (open) label in search results spaced the same way as the rest of the row, so that it lines up in every theme and width.

Category: ui

## What

In SearchPanel.razor the text ' (open)' has a leading space that the flex row collapses; the gap does the spacing. Drop the leading space and update the AC-8 test that checks the text.

## Why

The UI review of job 379 noted the space does nothing. It is harmless but misleading for whoever edits the row next.

## Notes

Proposed by job 379-search-context-times (review-ui, round 2).

Proposed by job 379-search-context-times (`factory/jobs/379-search-context-times/`).
