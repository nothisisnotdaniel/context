# 253 Capture through an icon in the top bar

As a Context user, I want a capture icon in the top bar, so that I can capture a thought with one click now that the capture box is gone.

## What

In the desktop app's top bar (TopBar.razor, added by job 198 for #250), add an icon-only Capture button next to Home and Calendar. It opens the same capture flow as the palette command /capture (same guard, same inbox target), and focus goes to the capture text so the user can type at once; Esc closes it as the date picker does. The icon shows typing: a letter with a text cursor (for example a 'T' or 'A' with an I-beam caret, like Lucide's text-cursor-input or type icons), drawn as an inline SVG shipped with the app (no CDN, no new package). If no such mark reads well at the size of the other top bar icons, use a keyboard icon instead; the spec decides and says why. The button has aria-label and title 'Capture', and is readable in light and dark mode.

## Why

Job 198 removed the capture box from the top of the window; capture is now only reachable through the palette, which is slower and easy to forget.

## Notes

Requested by the user on 2026-11-17 after job 198.
