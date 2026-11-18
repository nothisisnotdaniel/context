# 293 Narrower Pick a day calendar popover

As a desktop user, I want the Pick a day calendar to be only as wide as the month grid, so that it doesn't cover more of the window than it needs.

## What

The .day-pick popover (src/Context.Desktop/TopBar.razor, wwwroot/app.css around .day-pick and .day-pick .day with min-width 2.2em) is wider than it needs to be. Make it fit its content: the month header and the grid with week numbers, with tighter day cells, and no empty space at the sides. Keep it readable and clickable, and keep keyboard navigation, light and dark mode.

## Why

User review on 2026-11-18: make the calendar pop-up not as wide as it is now.

## Notes

Requested by the user on 2026-11-18.
