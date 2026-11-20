# 324 Tag board: show the target column name on the move buttons

As a sighted desktop user, I want the board's move buttons to show which column a card moves to, so that I don't have to hover to find out.

Category: ux

## What

In src/Context.Desktop/BoardPanel.razor the card move buttons show only ← and →; their aria-label and title already say "Move to <column>". Show the target column name visibly (for example "← Doing"), truncated if long, without breaking the card layout, in light and dark mode.

## Why

UX review of job 242: the arrows alone don't say where the card goes, so sighted users must hover or guess.

## Notes

Proposed by job 242-tag-board-view (review-ux 1).

Proposed by job 242-tag-board-view (`factory/jobs/242-tag-board-view/`).
