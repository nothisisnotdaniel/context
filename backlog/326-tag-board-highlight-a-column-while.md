# 326 Tag board: highlight a column while a card is dragged over it

As a desktop user, I want the board column under a dragged card to be highlighted, so that I can see where the card will land.

Category: ui

## What

Add a dragover highlight to .board-column (src/Context.Desktop/BoardPanel.razor, wwwroot/app.css): set a state on dragenter/dragover and clear it on dragleave/drop/dragend, with a visible outline or background in light and dark mode that doesn't rely on colour alone.

## Why

UI review of job 242: there's no visual cue while dragging, so drops feel imprecise.

## Notes

Proposed by job 242-tag-board-view (review-ui 1).

Proposed by job 242-tag-board-view (`factory/jobs/242-tag-board-view/`).
