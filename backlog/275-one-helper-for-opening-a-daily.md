# 275 One helper for opening a daily note in Main.razor

As a Context developer, I want the open-a-daily-note steps in Main.razor to live in one place, so that /today, Pick a day and the discard dialog cannot drift apart.

## What

Main.razor repeats the same running gate, Actions.OpenDailyNote and ShowDaily block in /today, PickDay and the Discard-changes confirm branch. Move it into one helper without breaking DesktopSourceTests.Razor_CallsActionsOnlyFromEventHandlers (the builder of job 212 found a plain helper trips that rule; the spec must decide how the rule treats it).

## Why

The code review of job 212 noted the duplication; three copies of the same flow invite a fix in one place that misses the others.

## Notes

Proposed by job 212-calendar-day-editor (code review nit).

Proposed by job 212-calendar-day-editor (`factory/jobs/212-calendar-day-editor/`).
