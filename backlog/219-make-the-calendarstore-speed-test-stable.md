# 219 Make the CalendarStore speed test stable under load

As the Context developer, I want speed tests to pass on a busy machine, so that a slow CI runner never fails a run for nothing.

## What

CalendarStoreTests.Day_HostileRules_FastAndReadable failed once when three full test runs ran at the same time (job 176). Check what it measures; replace the wall-clock limit with a bound that does not depend on machine speed (for example a step or iteration count), or give it a margin wide enough for a loaded CI runner while still catching a real slowdown. Run the suite several times in parallel to confirm.

## Why

A wall-clock assertion fails when the machine is busy, which is a CI flake unrelated to any change.

## Notes

Proposed by job 176-wait-guard-all-files (builder: failed once in parallel runs).

Proposed by job 176-wait-guard-all-files (`factory/jobs/176-wait-guard-all-files/`).
