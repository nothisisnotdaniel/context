# 209 Announce the search result count to screen readers

As a Context user with a screen reader, I want to hear how many notes a search found, so that I know whether to read the list.

## What

In SearchPanel.razor, add a role=status line (or aria-live) with the number of hits, for example "12 notes found", and label the hit list. Update the spec text and tests.

## Why

The hit list is a bare list, so a screen-reader user doesn't learn the result count without moving through it.

## Notes

Proposed by job 170-full-text-search (review-ux round 1, nit 1).

Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
