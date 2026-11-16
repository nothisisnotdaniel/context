# 250 Top bar with Home and Calendar in place of the capture box

As a Context user, I want a slim bar at the top of the main area with a Home and a Calendar icon instead of the capture box, so that I can jump to today or any other day in one click.

## What

In the desktop app (src/Context.Desktop): (1) The quick capture box (CaptureBox.razor, .capture-box) is removed from the top of the main area (Main.razor). Capturing stays possible through the command palette (/capture), which keeps working. (2) In its place is a bar across the full width of the main area with no padding left and right (it starts directly at the top edge, as the capture bar does today). (3) Aligned to the right in that bar are icon-only buttons (inline SVG shipped with the app, no CDN or new package; aria-label and title for each; readable in light and dark mode, same size and style as the sidebar's Triage, Tags and Settings icons): a house, 'Today', which shows today's day view (the 'Today, <date>' page from CurrentPanel.razor), closing any open note or page the same way opening another view does today (the unsaved-changes guard keeps working); and a calendar, 'Pick a day', which opens a small date picker (a native date input or a simple month grid; Esc and clicking outside close it) and, once a date is picked, shows the day view for that date. The spec checks how the day view and DesktopState can show a day other than today (todos due, appointments, notes of that day) and keeps it to what the app already stores; if End the day only makes sense for today, it is hidden for other days. (4) The bar leaves room on the left for more icons later; nothing else goes there now.

## Why

The capture box takes the most visible spot of the window although capture is rarely used from there, and there is no quick way back to today or to another day's journal.

## Notes

Requested by the user on 2026-11-16 after the screenshots of job 196. A '…' menu for page actions goes into the same bar in a follow-up item.
