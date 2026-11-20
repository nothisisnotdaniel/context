# 314 Enter adds the attribute row in the Attributes box

As a desktop user, I want Enter in the new attribute's name or value field to add the row, so that I can add attributes without reaching for the Add button.

Category: ux

## What

In the Attributes box below the note title (src/Context.Desktop/NoteEditor.razor, add row), pressing Enter in the name or value field adds the row the same way the Add button does. Enter with an empty or invalid name does nothing, as the button does. Keep the Add button.

## Why

UX review of job 236: only the Add button adds the row, which is slower and unexpected in a form-like row.

## Notes

Proposed by job 236-attributes-collapsed-box (review-ux 1).

Proposed by job 236-attributes-collapsed-box (`factory/jobs/236-attributes-collapsed-box/`).
