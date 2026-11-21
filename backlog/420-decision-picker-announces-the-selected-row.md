# 420 Decision picker announces the selected row to screen readers

As a Context user with a screen reader, I want the decision picker to announce which row is selected, so that I can pick a decision without seeing the list.

Category: ux

## What

Give each row of the #!d decision picker in the editor an id and set aria-activedescendant on the editor content element to the selected row while the picker is open; clear it when the picker closes. Rebuild the editor bundle and add Jint tests for the attribute values.

## Why

Raised by review-ux in job 349: focus stays in the editor, so without aria-activedescendant screen readers do not announce the selection in the picker.

## Notes

Proposed by job 349-decision-folder-prefix (`factory/jobs/349-decision-folder-prefix/`).
