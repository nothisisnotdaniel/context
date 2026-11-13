# 141 Clear the typed calendar link when the settings panel closes

As a Context user, I want the typed calendar link dropped from memory whenever the settings panel goes away, so that the secret does not linger.

## What

Make StateComponent.Dispose overridable and clear SettingsPanel's typed value on dispose; also clear it when a Replace is cancelled.

## Why

Review-security and review-code (job 138): the typed link stays in the component if Main hides the panel without Close or a Replace is cancelled.

## Notes

Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
