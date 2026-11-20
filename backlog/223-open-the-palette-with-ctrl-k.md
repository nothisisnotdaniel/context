# 223 Open the palette with Ctrl+K from a triage text field

As a Context user, I want Ctrl+K to open the command palette while I type in an inbox triage field, so that the shortcut works everywhere in the app.

Category: ux

## What

In TriagePanel.razor, let Ctrl+K through the text fields' key handling (stopPropagation) so the app-wide palette shortcut fires, while the triage keys still stay inside the field. Add a test.

## Why

The triage text fields stop key propagation, so Ctrl+K does nothing there, unlike the rest of the app.

## Notes

Proposed by job 179-inbox-triage (review-ux round 1, nit 1).

Proposed by job 179-inbox-triage (`factory/jobs/179-inbox-triage/`).
