# 325 Tag board: hint when a card is dropped on No status

As a desktop user, I want feedback when I drop a card on the No status column, so that I know why nothing changed.

## What

In src/Context.Desktop/BoardPanel.razor dropping a card on the "No status" column silently does nothing. Either show a short hint (for example in the column or a status line, announced to screen readers) or make the drop remove the card's #status/ tag; decide in the spec and keep it consistent with the move buttons.

## Why

UX review of job 242: a silent no-op on drop looks like a bug.

## Notes

Proposed by job 242-tag-board-view (review-ux 2).

Proposed by job 242-tag-board-view (`factory/jobs/242-tag-board-view/`).
