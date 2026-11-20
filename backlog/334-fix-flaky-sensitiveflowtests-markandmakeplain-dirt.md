# 334 Fix flaky SensitiveFlowTests.MarkAndMakePlain_Dirty_AskToSaveFirst test

As a developer, I want SensitiveFlowTests.MarkAndMakePlain_Dirty_AskToSaveFirst to pass every time, so that a red test run always means a real problem and CI doesn't fail at random.

Category: test

## What

Under load (two Desktop suite runs at once) this test's folder snapshot caught a temporary recent.json file. Wait for pending recent.json writes before snapshotting (DesktopFixture.WaitForRecentWrites from job 246), without weakening what it checks.

## Why

Found by the builder of job 246 while proving its fix: the same race job 246 fixed for the palette tests; a random failure under -warnaserror CI blocks merges.

## Notes

Proposed by job 246-flaky-palette-tests (build.md round 2).

Proposed by job 246-flaky-palette-tests (`factory/jobs/246-flaky-palette-tests/`).
