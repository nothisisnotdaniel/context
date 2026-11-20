# 316 Add attributes to a note that has none

As a desktop user, I want to add an attribute to a note that has no attribute lines yet, so that I don't have to type 'key:: value' lines by hand.

## What

A note without attributes shows no Attributes box today, and removing the last attribute removes the box with its add row. Offer a small way to add the first attribute (for example an 'Add attribute' link below the title), which writes the new 'key:: value' line at the top of the note body in the place the box reads them from. Keyboard and screen reader friendly, light and dark mode.

## Why

UX review of job 236: once the last attribute is removed, the user can't get one back from the app; the job 236 spec left this out of scope.

## Notes

Proposed by job 236-attributes-collapsed-box (review-ux 3).

Proposed by job 236-attributes-collapsed-box (`factory/jobs/236-attributes-collapsed-box/`).
