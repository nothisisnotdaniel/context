# 194 Show confirmed overrides of a cancelled recurring series

As a Context user, I want a single moved meeting to still show when the rest of its series was cancelled, so that /current doesn't hide an appointment that is still on.

Category: bug

## What

Today a recurring event with STATUS:CANCELLED also hides its RECURRENCE-ID overrides with the same UID (a documented choice in job 162). Decide per RFC 5545 whether an override with its own non-cancelled STATUS should still show, and if so show it in /current, the CLI and the desktop. Add tests for both cases.

## Why

Calendars sometimes cancel a series but keep one moved occurrence; hiding it means a real meeting disappears from the day view.

## Notes

Proposed by job 162-recurring-appointments (review-code round 1, nit 2).

Proposed by job 162-recurring-appointments (`factory/jobs/162-recurring-appointments/`).
