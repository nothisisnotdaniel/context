# 435 Clear the typed licence key after a refused or failed activation

As a user, I want the licence key field emptied after an activation is refused or fails, so that the key doesn't sit in the page longer than needed.

Category: security

## What

In SettingsPanel, clear input.pro-key (and the component field holding its value) whenever DesktopActions.ActivatePro returns Refused or Failed, not only after Done.

## Why

Raised by the security review of job 367: the key stays in web-view memory and on screen after a failed attempt, which widens the window in which it can leak.

## Notes

Proposed by job 367-pro-licence-context.

Proposed by job 367-pro-licence-context (`factory/jobs/367-pro-licence-context/`).
