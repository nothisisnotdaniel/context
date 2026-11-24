# 138 Look at the tags and links panels in a running window

As the owner, I want the new tags view and links panel checked by eye in light and dark mode, so that layout problems tests cannot see are caught.

Category: ui

## What

Run the desktop app and check the 30vh tag list, long-name truncation, pressed state and both themes; note any problems as backlog items.

## Why

Review-ui and review-ux (job 137): the CSS was never viewed in a running window.

## Notes

Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).

## Windows verification, 2026-11-24

On develop `29b1b32c`, Light and Dark passed the list and link checks at 1084 × 711 and 820 × 560 CSS px. With 80 tags the scrolling list measured 213.30 and 168 px (30 vh). Long tag and note buttons displayed ellipses, retained full titles and opened the correct results or note. The pressed fill was visible in both themes and moved exclusively to the selected tag. The links panel showed 29 relationships.

The selected tag's result heading overflows. With one valid 50-character tag the text extended 151.36 px beyond the main pane at normal size and 415.36 px at 820 px wide. Board and Close covered 110 px of the tag in both sizes and themes. A short tag had no overlap. Repeat after [#228](228-tags-and-links-panel-fixes.md).
