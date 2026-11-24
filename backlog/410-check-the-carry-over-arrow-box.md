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

## Windows verification, 2026-11-24

Passed on develop `29b1b32c` in native Windows WebView2 158.0.4370.33 at 100% scale. In the dated 2026-10-13 fixture, an open, carry-over and completed item showed the same text. In both themes all three rendered markers occupied pixels x=269 through x=278, and the first letter stem began at x=287 for all three. The measured advances were 12.0625 px for both boxes and 12.078125 px for the arrow. That 1/64 CSS px difference produced no visible text shift.
