# 243 Open Triage, Tags and Settings as their own pages with an × close

As a Context user, I want the Triage, Tags and Settings buttons to each open their own page in the main area, so that I work on one thing at a time instead of a stack of panels above my note.

## What

In the desktop app (src/Context.Desktop), the panels opened from the sidebar buttons stop stacking at the top of the main region (Main.razor, under CaptureBox). (1) Triage, Tags (with the tag list it opens) and Settings each become a page: when one is shown it fills the main region below the capture bar in place of the open note or current view, and only one such page is shown at a time (opening another replaces it). (2) Each page has a close control at its top right: an icon-only × button (inline SVG or the × character, aria-label and title 'Close'), replacing the text 'Close' button in TriagePanel, TagsPanel and SettingsPanel. Closing returns to what was shown before (the open note or the current view), and Esc keeps working where it already closes a panel. (3) The palette commands that open these views (/settings, /triage, /tags) open the same pages. The unsaved-changes guard for the open note and the Settings busy state keep working as today. Light and dark mode.

## Why

Today every button adds another panel at the top of the main window, several can be open at once and each needs its own text Close button, which makes the window cluttered right after the sidebar was tidied (job 192).

## Notes

Requested by the user on 2026-11-16 after job 192. Review, Find, Search and Decisions panels are out of scope unless they share the same code path.
