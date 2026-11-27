# 533 Check the six notes-bar buttons in the narrowest sidebar by eye

Category: ui

## What

On macOS, open the desktop app with the sidebar at its narrowest width and look at the notes bar (NotesPanel.razor) in both light and dark themes. Check that the six 18px buttons, including the new ! decisions button, fit on one line without wrapping or clipping. Fix the layout if they do not.

## Why

Job 488 added a sixth button to the notes bar. The UI and UX reviews could not see the real rendering, so nobody has checked by eye that the bar still fits at the narrowest width.

## Notes

Proposed by job 488-todo-checkbox-decisions-button.5a0356d9890440c29c256f1650cdaf07 (`factory/jobs/488-todo-checkbox-decisions-button.5a0356d9890440c29c256f1650cdaf07/`).
