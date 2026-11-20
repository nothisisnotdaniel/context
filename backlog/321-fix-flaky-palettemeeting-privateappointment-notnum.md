# 321 Fix flaky PaletteMeeting_PrivateAppointment_NotNumbered test

As a developer, I want MeetingNoteActionsTests.PaletteMeeting_PrivateAppointment_NotNumbered to pass every time, so that a red test run always means a real problem and CI doesn't fail at random.

## What

MeetingNoteActionsTests.PaletteMeeting_PrivateAppointment_NotNumbered (Context.Desktop tests) failed once in a full dotnet test run during job 238 and then passed alone and in 6 parallel runs. Find what it shares with other tests (time, a shared folder, static state, ordering) and make it independent, without weakening what it checks.

## Why

Code review of job 238: the flake is unrelated to that diff, but a random failure under -warnaserror CI blocks merges and wastes rework rounds.

## Notes

Proposed by job 238-subpage-links (review-code 2).

Proposed by job 238-subpage-links (`factory/jobs/238-subpage-links/`).
