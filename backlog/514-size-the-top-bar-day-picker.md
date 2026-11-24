# 514 Size the top-bar day picker to its calendar grid

As a Context user picking a day from the top bar, I want to see the month, week numbers and day buttons and click the date I want.

Category: bug

## What

- Make the upper Pick a day popover fit the intrinsic size of its header, six ISO week numbers and seven day columns. Inspect the interaction between the global fixed-layout 100%-width table and the max-content popover in app.css. Scope the change so other app tables and the already usable default-width sidebar calendar retain their layout.
- At normal Windows client widths of 1084 and 800 CSS pixels, verify in Light and Dark that the whole month header, both month arrows, all week numbers and 42 usable day targets lie inside the visible popover. Dates from adjacent months remain selectable. Verify that today, another selected day, and today when selected can be distinguished visually and that an actual pointer click opens the correct daily note.
- Re-run manual #309 in the real desktop WebView and retain a reproducible regression check for the sizing interaction. Check keyboard date selection. The keyboard-induced scrolling of the page behind the calendar has its own existing item #277.

## Why

Windows WebView2 158.0.4370.33 on develop 29b1b32 rendered the upper popover 1,000,018 CSS pixels wide and its table 1,000,000 pixels wide at both 1084 and 800 client widths. All six week labels, all 42 dates, the month text and the previous-month button were off screen in both themes. A real click in a visible row hit an empty table cell and left the note and popover unchanged. The separate default-width sidebar calendar rendered a 229-pixel grid with 42 reachable dates.

## Notes

Manual #309 remains open for visual verification. #293 introduced the compact top-bar design. #390 covers a narrower-than-current sidebar and #277 covers background scrolling. Neither covers this normal-size top-bar sizing regression.
