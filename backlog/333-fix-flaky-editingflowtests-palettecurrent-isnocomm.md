# 333 Fix flaky EditingFlowTests.PaletteCurrent_IsNoCommand test

As a developer, I want EditingFlowTests.PaletteCurrent_IsNoCommand to pass every time, so that a red test run always means a real problem and CI doesn't fail at random.

## What

EditingFlowTests.PaletteCurrent_IsNoCommand (Context.Desktop tests) failed once in a full dotnet test run during job 245 and passed on two reruns. Find what it shares with other tests (time, a shared folder, static state, ordering) and make it independent without weakening what it checks.

## Why

Approver of job 245: a random failure under -warnaserror CI blocks merges and wastes rework rounds.

## Notes

Proposed by job 245-agenda-carry-over (decision).

Proposed by job 245-agenda-carry-over (`factory/jobs/245-agenda-carry-over/`).
