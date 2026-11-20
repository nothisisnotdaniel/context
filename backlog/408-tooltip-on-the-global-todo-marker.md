# 408 Tooltip on the global todo marker

As a Context user, I want a tooltip on the marker of a global todo, so that I can tell what the small marker means without knowing it already.

Category: ux

## What

Add a tooltip (title) to the marker that job 329 shows next to global `- { }` todos in the editor, for example "Global todo: shows in the Todos view". Keep the screen-reader label as it is.

## Why

The UI and UX reviews of job 329 noted that the 6px marker is a faint cue with no explanation; without a tooltip users may not learn that braces mean global and brackets mean local.

## Notes

From 329 ui/ux review.

Proposed by job 329-global-todo-braces (`factory/jobs/329-global-todo-braces/`).
