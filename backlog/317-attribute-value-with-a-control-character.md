# 317 Attribute value with a control character isn't counted as changed

As a desktop user, I want the Attributes box to save only values I actually changed, so that a note isn't rewritten when I didn't edit anything.

Category: bug

## What

In NoteEditor.razor ChangeAttribute, the unchanged-row check compares the raw value while the field shows DesktopText.OneLine(value), so a value holding a lone control character counts as changed on its first edit. Compare against the shown value (or keep the raw value when the shown one is unchanged) so the saved text stays byte-equal.

## Why

Code review of job 236: rare, but it rewrites a value the user didn't change.

## Notes

Proposed by job 236-attributes-collapsed-box (review-code 2).

Proposed by job 236-attributes-collapsed-box (`factory/jobs/236-attributes-collapsed-box/`).
