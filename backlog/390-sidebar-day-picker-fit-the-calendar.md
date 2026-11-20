# 390 Sidebar day picker: fit the calendar in a narrow sidebar

As the user, I want the sidebar's month calendar to stay fully visible when the sidebar is narrow, so that I can always pick a day without the calendar being clipped.

## What

The sidebar DayPicker popover (.day-pick in src/Context.Desktop/wwwroot/app.css) is width: max-content with max-width: 100%; at the current 280px sidebar it fits, but a narrower sidebar would clip the grid. Shrink the grid cells or anchor the popover so it can overflow the sidebar, and add a test or source check for the narrow case.

## Why

UI review of job 289 (nit 1): harmless at today's width, but fragile if the sidebar ever gets narrower.

## Notes

Proposed by job 289-sidebar-day-picker (review-ui 1).

Proposed by job 289-sidebar-day-picker (`factory/jobs/289-sidebar-day-picker/`).
