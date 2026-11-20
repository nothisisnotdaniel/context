# 347 Keep the old diagram while it redraws

As a desktop user, I want the last drawn diagram to stay visible while I edit its code, so that the picture doesn't flicker back to code on every keystroke.

Category: ui

## What

While a mermaid block redraws after an edit, keep showing the previous picture (or debounce the redraw) instead of the code plus "Drawing diagram…". This changes the waiting state from job 263's AC-4, so spec it as a behaviour change.

## Why

UX review of job 263: redrawing on each edit swaps the picture for the code and back, which is jumpy while typing.

## Notes

Proposed by job 263-mermaid-diagrams (review-ux 1).

Proposed by job 263-mermaid-diagrams (`factory/jobs/263-mermaid-diagrams/`).
