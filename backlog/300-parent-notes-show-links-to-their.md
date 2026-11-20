# 300 Parent notes show links to their subpages

As a desktop user on a note that has subpages, I want to see links to the pages one level below it, so that I can move down the hierarchy without searching.

## What

On a note with subpages (NoteStore subpage names use '/', for example Projects with Projects/Alpha and Projects/Beta), show a list of links to its direct children only (not grandchildren) in the note view, for example a 'Subpages' section below the note body or next to the attributes box from #298. Each link shows the child's own name (Alpha, not Projects/Alpha) and opens the note on click and from the keyboard, saving the current note first as other navigation does. Sort by name. Update the list when a subpage is created, renamed or deleted. A note without subpages shows nothing. Say in the spec where it sits and whether a child with children of its own is marked. Light and dark mode.

## Why

User request on 2026-11-18: a note higher up in the hierarchy should show links to all pages in the next lower level.

## Notes

Requested by the user on 2026-11-18. Fits with #297 (title shows only the own name) and the breadcrumb in Main.razor.
