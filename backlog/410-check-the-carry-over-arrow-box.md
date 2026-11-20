# 410 Check the carry-over arrow box width in both themes

As a Context user, I want the arrow box of a `- [>]` line to line up with the `[ ]` and `[x]` boxes, so that a list with mixed markers looks tidy.

Category: ui

## What

Look at a note with `- [ ]`, `- [>]` and `- [x]` lines in light and dark mode. If the arrow glyph (U+21E5) is wider or narrower than the ballot box (U+2610) and the text shifts, give the carry box the same width as the others without changing how the other boxes look.

## Why

The UI review of job 330 noted the glyphs may have different widths in some fonts; then text after a `[>]` box would not line up with the lines around it.

## Notes

From 330 ui review.

Proposed by job 330-carry-over-marker (`factory/jobs/330-carry-over-marker/`).
