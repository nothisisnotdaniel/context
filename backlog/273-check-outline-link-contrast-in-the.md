# 273 Check outline link contrast in the right sidebar

As a Context user, I want the outline links in the right sidebar to be easy to read in light and dark mode, so that I can scan a note's headings without strain.

Category: ui

## What

Check by hand, in light and dark mode, the contrast of the outline buttons (--accent-strong) on the right sidebar background (--panel), app.css around line 1559. If it is below WCAG AA (4.5:1 for text), pick a colour token that meets it in both themes.

## Why

Job 210 moved the outline from the note area into the right sidebar, so the links now sit on --panel instead of the note background; the ui review asked for a manual contrast check. Low contrast would make the outline hard to read.

## Notes

Proposed by job 210-outline-right-sidebar (ui review nit).

Proposed by job 210-outline-right-sidebar (`factory/jobs/210-outline-right-sidebar/`).

## Windows verification, 2026-11-24

On develop `29b1b32c`, actual screenshot pixels on three 14 px outline buttons measured 4.2149:1 in Light (#1e78c8 on #f5f5f7), below 4.5:1. Dark passed at 7.2533:1 (#63b8ff on #242426). Clicking the final link scrolled its heading into view in both themes. A scoped light outline color of #1a6db7 reaches 4.9261:1 on the same panel. Repeat after [#515](515-give-sidebar-outline-links-sufficient-contrast.md).
