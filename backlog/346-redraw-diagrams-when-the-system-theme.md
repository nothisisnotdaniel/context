# 346 Redraw diagrams when the system theme changes

As a desktop user, I want mermaid diagrams to switch to light or dark as soon as the system theme changes, so that a diagram never stays in the wrong colours until I reopen the note.

## What

Listen for a prefers-color-scheme change and redraw every rendered mermaid diagram with the matching theme, without losing the note's scroll position.

## Why

UI review of job 263: diagrams take the theme only when they are drawn, so a live theme switch leaves them in the old colours.

## Notes

Proposed by job 263-mermaid-diagrams (review-ui 2).

Proposed by job 263-mermaid-diagrams (`factory/jobs/263-mermaid-diagrams/`).
