# 302 One path to EndReload in StateComponent

As a developer on Context, I want StateComponent to end a reload in one obvious place, so that the reload counter is easy to follow and change.

## What

In src/Context.Desktop/StateComponent.cs, EndReload is reached from the finally, the catch and a ContinueWith on the dispatched task. The once-flag makes the extra calls harmless. Simplify to fewer paths while still covering a dispatched task that ends without running Reload (the dispose case from job 221), and keep the window-close test steady.

## Why

Code review of job 221: three paths to the same call are harder to reason about; the builder kept the ContinueWith because it covers the dispose case.

## Notes

Proposed by job 221-steady-windowclose-test (`factory/jobs/221-steady-windowclose-test/`).
