# 528 Cap the length of the Outlook attendee list

As a Context user, I want the Outlook attendee list read with a length cap, so that a huge or broken attendee field can never slow down or bloat the appointment page.

Category: security

## What

In src/Context.Desktop/OutlookAttendees.cs, cap the RequiredAttendees/OptionalAttendees strings (and the number of names) before Split, and drop anything beyond the cap. Add a test for an overlong list.

## Why

The security review of job 483 found no length cap before Split. The input comes only from the user's local Outlook, so the risk is low, but an unbounded field could make the page slow.

## Notes

Proposed by job 483-attendees-by-role.7e6bcb6115c34d74ab8fe7e19bd682ff (`factory/jobs/483-attendees-by-role.7e6bcb6115c34d74ab8fe7e19bd682ff/`).
