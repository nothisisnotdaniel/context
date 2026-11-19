# 379 Search page: arrow keys move between results without scrolling the page

As a keyboard user, I want ArrowUp and ArrowDown on the search results page to move only between results, so that the page doesn't jump while I move through them.

## What

SearchPanel.razor (RowKey) handles ArrowUp/ArrowDown but doesn't preventDefault them, because Blazor's @onkeydown:preventDefault would block every key, Tab and Enter included. Add a small JS keydown listener on the results list that calls preventDefault for ArrowUp/ArrowDown only, keep Tab and Enter as they are, and add a test.

## Why

UX review of job 285 (nit 2): the page can scroll as well as move focus.

## Notes

Proposed by job 285-search-results-page (review-ux 2).

Proposed by job 285-search-results-page (`factory/jobs/285-search-results-page/`).
