# 145 Look at the notes bar buttons in a narrow sidebar

As the owner, I want the Tags and Settings buttons checked by eye in a narrow sidebar, so that the Notes heading is not squeezed.

Category: ui

## What

Run the desktop app with a narrow sidebar and check the notes bar; fix the CSS if the heading is squeezed.

## Why

Review-ui (job 138): the two buttons now sit side by side in the narrow bar.

## Notes

Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).

## Windows verification, 2026-11-24

#247 replaced the visible Notes heading with a logo. #252 subsequently removed the logo and kept that space for icons. On develop `29b1b32c`, the 240 px sidebar displays five 30 × 30 px buttons and the list retains the accessible name Recent notes. The Tags and Settings buttons opened their pages by click and by Tab/Enter in both themes across nine client widths from 120 to 1084 px. At 400 px both buttons were fully visible, but their page headings had zero visible pixels. Both complete headings were visible at 800 and 1084 px. At the native minimum the content area measured 120 × 0 px.

Leave this check open until #503's narrow-window layout fix displays the opened page. Keep the current icon-only bar.
