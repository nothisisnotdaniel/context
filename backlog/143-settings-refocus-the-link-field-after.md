# 143 Settings: refocus the link field after a refusal

As a keyboard user, I want focus back in the calendar link field after a refused save, so that I can retype without reaching for the mouse.

## What

After a refused or failed save, move focus back to the link field (small JS interop or Blazor FocusAsync).

## Why

Review-ux (job 138): the field is disabled during the action, so focus is lost.

## Notes

Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
