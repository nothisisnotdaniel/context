# 144 Settings: refusal text asks to type the link again

As a Context user, I want a refused calendar link save to tell me to type the link again, so that I know what to do with a masked field I cannot read.

## What

Change the refusal and failure texts in DesktopActions.SetCalendarLink to end with a hint to type the link again; update the spec-fixed strings in tests.

## Why

Review-ux (job 138): the masked field keeps a value the user cannot check.

## Notes

Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
