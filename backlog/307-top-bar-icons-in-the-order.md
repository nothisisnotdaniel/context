# 307 Top bar icons in the order Home, Search, Capture, Calendar, More

As a desktop user, I want the top bar icons in the order Home, Search, Capture, Calendar and then More actions, so that the ones I use most come first.

## What

In src/Context.Desktop/TopBar.razor the buttons are now Capture, Search, Today (the home icon), Pick a day (calendar) and More actions. Reorder them to Today (home), Search, Capture, Pick a day, More actions. Keep each button's label, popover and keyboard behaviour; the Tab order follows the new visual order. Update tests and any docs or CSS that depend on the order.

## Why

User request on 2026-11-18: reorder the top bar icons to Home, search, capture, calendar, ...

## Notes

Requested by the user on 2026-11-18.
