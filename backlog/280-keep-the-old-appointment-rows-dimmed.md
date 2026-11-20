# 280 Keep the old appointment rows dimmed while stepping days

As a desktop user stepping through days in the sidebar, I want the old appointments to stay visible but dimmed until the new day loads, so that the list does not flicker through Loading….

Category: ui

## What

In DayAppointmentsPanel.razor, keep the previous rows (dimmed, aria-busy) while the next day loads instead of swapping to Loading…; tests for the step.

## Why

The UX reviewer on job 214 found that the Loading… swap can flicker on every arrow click.

## Notes

Proposed by job 214-sidebar-today-appointments (`factory/jobs/214-sidebar-today-appointments/`).
