# 140 Calendar link field: autocomplete new-password

As a Context user, I want the masked calendar link field to tell the browser it is a new secret, so that the web view never offers to save or autofill it.

## What

Set autocomplete="new-password" instead of "off" on the link field in SettingsPanel.razor and update AC-3's test.

## Why

Review-security (job 138): web views often ignore autocomplete=off on password fields.

## Notes

Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
