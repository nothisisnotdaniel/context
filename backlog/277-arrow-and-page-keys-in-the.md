# 277 Arrow and Page keys in the Pick a day calendar do not scroll the page

As a desktop user moving through the Pick a day calendar with the keyboard, I want arrow and Page keys to move only the focused day, so that the page behind it does not scroll as well.

Category: bug

## What

Call preventDefault for ArrowLeft/Right/Up/Down, PageUp, PageDown, Home and End on the day buttons in TopBar.razor (a JS key filter or the existing interop), with a test.

## Why

Reviewers on job 213 found that a scrollable ancestor may scroll while the user moves around the calendar.

## Notes

Proposed by job 213-month-calendar-picker (`factory/jobs/213-month-calendar-picker/`).
