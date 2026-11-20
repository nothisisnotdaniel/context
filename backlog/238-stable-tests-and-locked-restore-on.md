# 238 Stable tests and locked restore on CI

As the Context developer, I want the flaky desktop and calendar tests made deterministic and CI restore re-checked, so that CI only fails for real problems.

Category: ci

## What

- Wait for the page's own loads in MainTests.Open (was #217): Make MainTests.Open wait until the links panel (and any other Task.Run load the page starts) shows no Loading… before returning, like MainTests.OpenFull does since job 175, or merge the two helpers. Keep every test's checks as they are and run the desktop suite several times in parallel to confirm.
- Make the CalendarStore speed test stable under load (was #219): CalendarStoreTests.Day_HostileRules_FastAndReadable failed once when three full test runs ran at the same time (job 176). Check what it measures; replace the wall-clock limit with a bound that does not depend on machine speed (for example a step or iteration count), or give it a margin wide enough for a loaded CI runner while still catching a real slowdown. Run the suite several times in parallel to confirm.
- Force a fresh locked restore on CI (was #180): Add --force to the locked dotnet restore in ci.yml (and the release restore if it has one), so NuGet re-validates packages.lock.json instead of taking a no-op restore from an existing obj/. Update the workflow tests to require it.

## Why

- #217: Job 175 fixed the breadcrumb flake in OpenFull only; Open still returns before the links panel has loaded, so a test that compares elements after Open would flake the same way.
- #219: A wall-clock assertion fails when the machine is busy, which is a CI flake unrelated to any change.
- #180: Security review of job 155: a no-op restore without --force does not re-check a hand-edited lock file. GitHub-hosted runners start without obj/, so it does not matter today, but a self-hosted runner with a persistent workspace would lose the lock-file protection.

## Notes

- Merges #217, #219, #180 on 2026-11-16, to save the fixed cost of one job per item.
- Cheap mode is enough. This is test-stability work, so run the suite several times in parallel to confirm.
- #217: Proposed by job 175-flaky-desktop-tests (review-code round 1, nit 1). Proposed by job 175-flaky-desktop-tests (`factory/jobs/175-flaky-desktop-tests/`). Merged on 2026-11-15: #179 (MainTests.Breadcrumb_ChildOnly_AncestorsAsButtons_CurrentAsText fails now and then in a full run). It opens its page through MainTests.Open, so this fix should cover it. Check that test in the repeated runs.
- #219: Proposed by job 176-wait-guard-all-files (builder: failed once in parallel runs). Proposed by job 176-wait-guard-all-files (`factory/jobs/176-wait-guard-all-files/`).
- #180: Proposed by job 155-locked-package-restore (`factory/jobs/155-locked-package-restore/`).
