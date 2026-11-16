# 252 Remove the sidebar logo and the Return hints

As a Context user, I want no logo in the sidebar and no 'Return to …' hints in text boxes, so that the window is quieter and the sidebar header has room for more icons.

## What

In the desktop app (src/Context.Desktop): (1) Remove the small logo at the top left of the sidebar header (NotesPanel.razor, added by job 195) and its CSS; the Triage, Tags and Settings icons stay where they are, aligned right, and the space on the left stays empty for later icons. The note list keeps its accessible name. (2) In every text box, remove the ' - Return to …' part of the placeholder and keep only what the box is for, for example 'New note title', 'New todo', 'Subpage title', 'New name', 'Empty: top level', 'https:// or webcal:// link', 'Note title', 'Todo text', 'Note name', 'yyyy-MM-dd' (NotesPanel, TodoPanel, Main, SettingsPanel, TriagePanel; the spec lists every one, and CaptureBox if it still exists). Return keeps submitting each box exactly as today. Tests that assert the old placeholders are updated, and the CLAUDE.md rule about Return hints (from job 194) is changed to match. Light and dark mode.

## Why

The hints were added in job 194 but make every box long and get cut off in the narrow columns; Return is the expected way to submit a single box anyway. The logo takes the room the user wants for more icons.

## Notes

Requested by the user on 2026-11-16 after the screenshots of job 196. Replaces the proposal #248 (check that the Return hints fit), which is dropped.
