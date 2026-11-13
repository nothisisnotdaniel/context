# 142 Lock the calendar link field while the replace dialog is open

As a Context user, I want the link field locked while I confirm a replace, so that Replace saves exactly what I saw when the dialog opened.

## What

Disable the link field in SettingsPanel.razor while the replace confirmation is shown.

## Why

Review-security (job 138): typing during the dialog changes what Replace confirms.

## Notes

Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
