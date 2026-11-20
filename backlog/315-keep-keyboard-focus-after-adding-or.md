# 315 Keep keyboard focus after adding or removing an attribute

As a keyboard user, I want focus to stay in the Attributes box after I add or remove an attribute, so that I don't lose my place.

## What

After Add, focus goes back to the new attribute's name field; after Remove, focus moves to the next row's value field (or the previous one, or the add row's name field when none is left). Uses JS interop focus in src/Context.Desktop/NoteEditor.razor.

## Why

UX review of job 236: the rows re-render and focus is lost, so keyboard and screen reader users have to tab back from the top.

## Notes

Proposed by job 236-attributes-collapsed-box (review-ux 2).

Proposed by job 236-attributes-collapsed-box (`factory/jobs/236-attributes-collapsed-box/`).
