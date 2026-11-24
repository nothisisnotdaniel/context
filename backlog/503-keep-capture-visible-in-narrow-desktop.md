# 503 Keep Capture visible in narrow desktop windows

As a Context user with a small window, I want to see and open Capture and read the whole input while typing, so I can send a thought to my inbox.

Category: bug

## What

- Make the current top-bar Capture icon and its entire input reachable by pointer and keyboard at every supported desktop size, including about 400 CSS pixels wide. Keep focus on the text field when it opens and Enter's existing inbox submission. The fixed side columns and the main pane's clipping must leave the controls visible.
- Make the native Windows minimum leave enough content area to show the Capture control and input. Check the actual outer size, client area, and common narrow widths with a real window. Keep the existing capture icon/popover flow from #250 and #253.
- Re-run manual checks #152 and #259 in Light and Dark. At the native minimum and at 400 CSS pixels the icon must be visible initially and clickable, the full field must fit, the typed note must be readable, and pressing Enter must append the exact line once to a disposable inbox. Retain the normal-size behavior.

## Why

At 96 DPI on Windows, develop 29b1b32 accepts a 136 x 39 outer window, leaving a 120 x 0 client. At the minimum width with normal height (120 x 711 CSS) and at 400 x 500 CSS the Capture icon initially has no clickable area. Tab can reach it, but the main pane then shows only 31 pixels of a 72- or 320-pixel field. Keyboard submission still writes the correct line. Both themes reproduce this. At the normal 1084 x 711 client the full 320-pixel field is visible and clickable.

## Notes

Manual checks #152 and #259 remain open for verification after this fix. #250 retired the old capture row, and #253 introduced the current Capture icon and popover. Repeat against the actual native Windows window and the private inbox.

## Sidebar regression from #145

In the same fixed three-column layout, at a 400 px client width the tracks measure 240, 0 and 280 px. The 30 × 30 px Tags and Settings buttons are visible, and click or Enter opens either page, but zero pixels of the resulting heading are visible. At 800 and 1084 px both headings are readable. Also verify the two sidebar actions at 400 px and at the supported minimum after the shared layout and minimum-size changes. The selected destination and its content must be visible in either theme. This is the remaining check in #145.
