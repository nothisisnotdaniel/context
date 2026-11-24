# 456 Check the Times panel's booking fields in dark mode

As a user of the dark theme, I want the Times panel's project select and name field to look like the other form fields, so that the booking form is readable.

Category: ui

## What

Look at TimesTodayPanel's select and name input in light and dark mode; add panel styles in app.css if the global form styles don't cover them.

## Why

The fields rely only on global form styles and were not checked by eye in dark mode.

## Notes

Proposed by job 383-times-desktop-panel (`factory/jobs/383-times-desktop-panel/`).

## Manual verification

Windows WebView2 at `29b1b32`, 2026-11-24: the selected project and entered name matched comparable controls in both themes. In Dark their measured contrasts were 11.20:1 and 15.63:1. The empty name field's only visible hint, `Checkpoint name`, measured 3.69:1 (#757575 on #1c1c1e), the same as another text field. In Light the hint reached 4.61:1. Repeat after the shared placeholder fix [#505](505-raise-the-contrast-of-desktop-text.md).
