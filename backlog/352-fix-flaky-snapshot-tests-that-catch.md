# 352 Fix flaky snapshot tests that catch the recent.json temp file

As a developer, I want the desktop tests that compare file snapshots to ignore the recent-notes write in flight, so that CI on develop stays green and only fails for real problems.

## What

CI on develop failed after job 265 (run 44487689674, step build / Test): MainTests.TopBar_PickDay_SensitiveNote_OpensThroughUnlock (tests/Context.Desktop.Tests/MainTests.cs:3700) saw an extra `.recent.json.<guid>.tmp` file in its after-snapshot. It is the same race job 258 fixed for one review-panel test: RecentNotes writes `.recent.json.<guid>.tmp` then renames it, and FileSnapshot.Take only ignores paths starting with `recent.json`. Fix it for every snapshot test, not just this one: either wait for pending recent writes before each after-snapshot (DesktopFixture.WaitForRecentWrites) in all tests that compare snapshots, or make FileSnapshot ignore the recent-notes temp files, whichever keeps the tests' checks as strong. Run the Desktop tests several times in parallel to show it holds.

## Why

Develop CI is red: a random failure blocks every merge in /factory auto. Job 258 fixed only one test of this kind, so others still flake.

## Notes

CI fix job after job 265-standup-notes; the failure is unrelated to that diff.
