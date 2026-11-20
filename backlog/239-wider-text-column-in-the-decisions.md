# 239 Wider text column in the Decisions panel

As a Context user, I want long decisions to show more of their text in the Decisions panel, so that I can read them without hovering on a narrow window.

Category: ui

## What

In app.css, rebalance `.decision-rows .note-button` (30%) and `.people` (25%) so the decision text gets more of the row (for example cap the note and people columns with a max width or let them shrink first). Keep the full text in `title`. Check light and dark themes at the minimum window size.

## Why

The note and people columns take about 55% of the row, so long decisions are cut early on a narrow window.

## Notes

Proposed by job 180-decision-log (review-ui round 1, nit 1).

Proposed by job 180-decision-log (`factory/jobs/180-decision-log/`).
