# 281 One reset helper for the pending state in Main.razor

As a developer on Context, I want one helper that clears all pending* fields in Main.razor, so that a new pending flow can not be forgotten in one of the many reset blocks.

## What

Replace the repeated pending* reset blocks in Main.razor with one ClearPending() helper, no change in behaviour.

## Why

The code reviewer on job 214 found the reset block copied in many more places after pendingAppointment was added.

## Notes

Proposed by job 214-sidebar-today-appointments (`factory/jobs/214-sidebar-today-appointments/`).
