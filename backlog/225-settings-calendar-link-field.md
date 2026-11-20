# 225 Settings calendar link field

As a Context user, I want the calendar link field in Settings to be clear, safe and easy to use, so that setting up my calendar works the first time.

## What

- Calendar link field: autocomplete new-password (was #140): Set autocomplete="new-password" instead of "off" on the link field in SettingsPanel.razor and update AC-3's test.
- Clear the typed calendar link when the settings panel closes (was #141): Make StateComponent.Dispose overridable and clear SettingsPanel's typed value on dispose; also clear it when a Replace is cancelled.
- Lock the calendar link field while the replace dialog is open (was #142): Disable the link field in SettingsPanel.razor while the replace confirmation is shown.
- Settings: refocus the link field after a refusal (was #143): After a refused or failed save, move focus back to the link field (small JS interop or Blazor FocusAsync).
- Settings: refusal text asks to type the link again (was #144): Change the refusal and failure texts in DesktopActions.SetCalendarLink to end with a hint to type the link again; update the spec-fixed strings in tests.

## Why

- #140: Review-security (job 138): web views often ignore autocomplete=off on password fields.
- #141: Review-security and review-code (job 138): the typed link stays in the component if Main hides the panel without Close or a Replace is cancelled.
- #142: Review-security (job 138): typing during the dialog changes what Replace confirms.
- #143: Review-ux (job 138): the field is disabled during the action, so focus is lost.
- #144: Review-ux (job 138): the masked field keeps a value the user cannot check.

## Notes

- Merges #140, #141, #142, #143, #144 on 2026-11-16, to save the fixed cost of one job per item.
- Cheap mode is enough: small UI and text changes in one place.
- #140: Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
- #141: Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
- #142: Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
- #143: Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
- #144: Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
