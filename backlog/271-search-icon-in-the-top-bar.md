# 271 Search icon in the top bar, opens like Capture

## What

In the desktop app: move the Search button from the left sidebar (NotesPanel.razor) into the top bar (TopBar.razor), next to the Capture, Today and Pick a day icons, as an icon-only button (magnifier icon, aria-label and title 'Search'). Clicking it opens a popover that looks and behaves like the Capture popover: one text input with a placeholder (for example 'Search notes'), focused at once, Enter runs the search and shows the results in the search panel as today, Esc closes and returns focus to the icon. Saved searches and /search keep working. Light and dark mode.

## Why

The user wants the global actions together in the top bar, and Search should feel the same as Capture.

## Notes

Requested by the user on 2026-11-18 ("Move the search icon to the middle bar and make clicking it look the same like the capture input."). The 'middle bar' is the top bar above the main view.
