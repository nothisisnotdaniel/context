# 218 Make the wait guard check every desktop test file

As the Context developer, I want the wait-rule guard to cover every desktop test file by itself, so that a new test file can't slip past it and fail CI at random.

## What

Change WaitTimeoutSourceTests so it finds every .cs file under tests/Context.Desktop.Tests/ that calls WaitForAssertion (or WaitForState/WaitForElement) instead of checking a hand-kept list. Each such file must have exactly one named Wait constant (10-30 s) and pass it to every wait, and no file may change BunitContext.DefaultWaitTimeout. Bring the files it newly finds into line, and update the wait rule in CLAUDE.md to say the guard covers all files.

## Why

The guard checks a fixed list, and files were missing from it twice (SettingsPanelTests.cs caused the CI failure in job 174, PersonPanelTests.cs was also missing), so short default waits slipped through.

## Notes

From the test-stability discussion on 2026-11-15, after job 175.
