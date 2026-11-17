# 254 Month calendar in the day picker

As a Context user, I want the day picker in the top bar to show a real month calendar, so that I can see the weeks and jump to a day with one click instead of typing a date.

## What

In the desktop app's top bar (TopBar.razor, the .day-pick popover opened by the Pick a day button, app.css): replace the native date input with a month calendar grid. (1) A header shows the month and year with a previous-month and a next-month arrow button (icon-only, aria-label/title "Previous month" / "Next month"). (2) Weeks start on Monday; the column headers are Mo Tu We Th Fr Sa Su. (3) Each row starts with its ISO 8601 week number in a narrow, muted column. (4) Days of the previous and next month that fill the first and last rows are shown muted (the spec decides whether they are clickable). (5) Today is marked (for example a ring or bold) and the selected day, the day the main view currently shows, is marked differently (for example a filled accent background); both marks stay distinct when today is the selected day. (6) Clicking a day opens that day exactly as picking a date does today and closes the popover. (7) Keyboard: arrow keys move between days, PageUp/PageDown change the month, Enter opens the focused day, Esc closes as today; focus starts on the selected day. The grid has accessible names (each day button names the full date). Light and dark mode.

## Why

The native date input shows only a text field (tt.mm.jjjj), which gives no overview of the month or the week numbers the user plans with.

## Notes

Requested by the user on 2026-11-17 after the screenshots of job 198 (#250). No new package; built in Razor and CSS. Week numbers use ISO 8601 (System.Globalization.ISOWeek).
